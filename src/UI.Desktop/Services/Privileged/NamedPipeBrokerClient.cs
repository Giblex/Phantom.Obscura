using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services.Privileged;

namespace PhantomVault.UI.Services.Privileged
{
    /// <summary>
    /// UI-side implementation of <see cref="IPrivilegedVolumeOperations"/>. Each call
    /// opens a short-lived connection to the elevated broker's named pipe, sends one
    /// <see cref="BrokerRequest"/>, then reads progress lines followed by a final
    /// result/error line.
    ///
    /// If the pipe cannot be reached, the optional <see cref="EnsureAvailableAsync"/>
    /// callback is invoked once to offer the user a one-time "Enable privileged helper"
    /// install; on success the operation is retried. If the helper is still
    /// unavailable a <see cref="PrivilegedBrokerUnavailableException"/> is thrown.
    /// </summary>
    public sealed class NamedPipeBrokerClient : IPrivilegedVolumeOperations
    {
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
        private const int ConnectTimeoutMs = 5000;

        /// <summary>
        /// Optional async gate invoked when the broker cannot be reached. It should
        /// prompt the user to enable the helper and return <c>true</c> when the helper
        /// is now installed and running (so the call can be retried), or <c>false</c>
        /// to fail with <see cref="PrivilegedBrokerUnavailableException"/>. Marshalling
        /// to the UI thread is the callback's responsibility.
        /// </summary>
        public Func<Task<bool>>? EnsureAvailableAsync { get; set; }

        public bool ApplyProtection(string driveRoot, UsbWriteProtectionState state)
        {
            var request = new BrokerRequest
            {
                Operation = BrokerOperation.ApplyProtection,
                DriveRoot = driveRoot,
                StateJson = JsonSerializer.Serialize(state, Json)
            };

            BrokerMessage result = ExchangeAsync(request, progress: null, CancellationToken.None)
                .GetAwaiter().GetResult();

            // Copy the broker-updated state (sentinels + last-asserted) back in place.
            if (!string.IsNullOrEmpty(result.StateJson))
            {
                var updated = JsonSerializer.Deserialize<UsbWriteProtectionState>(result.StateJson, Json);
                if (updated is not null)
                {
                    state.ReadOnly = updated.ReadOnly;
                    state.Hidden = updated.Hidden;
                    state.PartitionTypeGuid = updated.PartitionTypeGuid;
                    state.ExpectedSentinelFiles = updated.ExpectedSentinelFiles;
                    state.LastAsserted = updated.LastAsserted;
                    state.CompatibilityMode = updated.CompatibilityMode;
                }
            }

            return result.BoolResult;
        }

        public bool EnableWriteAccess(string driveRoot)
            => ExchangeAsync(new BrokerRequest { Operation = BrokerOperation.EnableWriteAccess, DriveRoot = driveRoot },
                progress: null, CancellationToken.None).GetAwaiter().GetResult().BoolResult;

        public bool DisableWriteAccess(string driveRoot)
            => ExchangeAsync(new BrokerRequest { Operation = BrokerOperation.DisableWriteAccess, DriveRoot = driveRoot },
                progress: null, CancellationToken.None).GetAwaiter().GetResult().BoolResult;

        public async Task CreateVolumeFromDirectoryAsync(string physicalDevicePath, string sourceRoot, CancellationToken cancellationToken = default)
            => await ExchangeAsync(new BrokerRequest
            {
                Operation = BrokerOperation.CreateVolumeFromDirectory,
                PhysicalDevicePath = physicalDevicePath,
                SourceRoot = sourceRoot
            }, progress: null, cancellationToken).ConfigureAwait(false);

        public async Task InvalidateVolumeHeaderAsync(string physicalDevicePath, CancellationToken cancellationToken = default)
            => await ExchangeAsync(new BrokerRequest
            {
                Operation = BrokerOperation.InvalidateVolumeHeader,
                PhysicalDevicePath = physicalDevicePath
            }, progress: null, cancellationToken).ConfigureAwait(false);

        public async Task<string> ExtractVolumeAsync(string physicalDevicePath, string destinationRoot, bool verify, IProgress<double>? progress, CancellationToken cancellationToken = default)
        {
            var result = await ExchangeAsync(new BrokerRequest
            {
                Operation = BrokerOperation.ExtractVolume,
                PhysicalDevicePath = physicalDevicePath,
                DestinationRoot = destinationRoot,
                Verify = verify
            }, progress, cancellationToken).ConfigureAwait(false);
            return result.StringResult ?? destinationRoot;
        }

        public async Task<bool> IsBlackSecureVolumeAsync(string physicalDevicePath, CancellationToken cancellationToken = default)
        {
            var result = await ExchangeAsync(new BrokerRequest
            {
                Operation = BrokerOperation.IsBlackSecureVolume,
                PhysicalDevicePath = physicalDevicePath
            }, progress: null, cancellationToken).ConfigureAwait(false);
            return result.BoolResult;
        }

        public bool ProvisionPhantomVolume(string containerPath, long sizeBytes)
            => ExchangeAsync(new BrokerRequest
            {
                Operation = BrokerOperation.ProvisionPhantomVolume,
                ContainerPath = containerPath,
                SizeBytes = sizeBytes
            }, progress: null, CancellationToken.None).GetAwaiter().GetResult().BoolResult;

        public string MountPhantomVolume(string containerPath)
        {
            var result = ExchangeAsync(new BrokerRequest
            {
                Operation = BrokerOperation.MountPhantomVolume,
                ContainerPath = containerPath
            }, progress: null, CancellationToken.None).GetAwaiter().GetResult();
            return result.StringResult ?? "";
        }

        public bool UnmountPhantomVolume(string containerPath)
            => ExchangeAsync(new BrokerRequest
            {
                Operation = BrokerOperation.UnmountPhantomVolume,
                ContainerPath = containerPath
            }, progress: null, CancellationToken.None).GetAwaiter().GetResult().BoolResult;

        public async Task<string> GetIntegrityVerdictAsync(string challenge, CancellationToken cancellationToken = default)
        {
            var result = await ExchangeAsync(new BrokerRequest
            {
                Operation = BrokerOperation.GetIntegrityVerdict,
                Challenge = challenge
            }, null, cancellationToken).ConfigureAwait(false);
            return result.StringResult ?? throw new InvalidDataException("Watchdog returned no integrity verdict.");
        }

        public async Task<string> AuthorizeIntegrityWriteAsync(string relativePath, int changeKind,
            string? expectedOldHash, string? expectedNewHash, long maximumLength,
            CancellationToken cancellationToken = default)
        {
            var result = await ExchangeAsync(new BrokerRequest
            {
                Operation = BrokerOperation.AuthorizeIntegrityWrite,
                RelativePath = relativePath,
                ChangeKind = changeKind,
                ExpectedOldHash = expectedOldHash,
                ExpectedNewHash = expectedNewHash,
                MaximumLength = maximumLength
            }, null, cancellationToken).ConfigureAwait(false);
            return result.StringResult ?? throw new InvalidDataException("Watchdog returned no write authorization.");
        }

        private async Task<BrokerMessage> ExchangeAsync(BrokerRequest request, IProgress<double>? progress, CancellationToken ct)
        {
            try
            {
                return await ExchangeOnceAsync(request, progress, ct).ConfigureAwait(false);
            }
            catch (PrivilegedBrokerUnavailableException)
            {
                // Offer the one-time "Enable privileged helper" install, then retry once.
                var ensure = EnsureAvailableAsync;
                if (ensure is null || !await ensure().ConfigureAwait(false))
                    throw;

                return await ExchangeOnceAsync(request, progress, ct).ConfigureAwait(false);
            }
        }

        private static async Task<BrokerMessage> ExchangeOnceAsync(BrokerRequest request, IProgress<double>? progress, CancellationToken ct)
        {
            await using var pipe = new NamedPipeClientStream(".", BrokerProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            try
            {
                await pipe.ConnectAsync(ConnectTimeoutMs, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
            {
                throw new PrivilegedBrokerUnavailableException(
                    "The Phantom Obscura privileged helper is not running. Enable it to perform this operation.", ex);
            }

            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);

            string requestLine = JsonSerializer.Serialize(request, Json);
            await writer.WriteLineAsync(requestLine.AsMemory(), ct).ConfigureAwait(false);

            while (true)
            {
                string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null)
                    throw new PrivilegedBrokerUnavailableException("The privileged helper closed the connection unexpectedly.");
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                BrokerMessage? message = JsonSerializer.Deserialize<BrokerMessage>(line, Json);
                if (message is null)
                    continue;

                switch (message.Type)
                {
                    case BrokerMessageType.Progress:
                        progress?.Report(message.Progress);
                        break;
                    case BrokerMessageType.Result:
                        return message;
                    case BrokerMessageType.Error:
                        throw new InvalidOperationException(message.Message ?? "The privileged helper reported an error.");
                }
            }
        }
    }
}
