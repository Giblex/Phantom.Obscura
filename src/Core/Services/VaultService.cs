using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using PhantomVault.Core.Options;
using PhantomVault.Core.Security;
using System.Linq;
using System.Text;

namespace PhantomVault.Core.Services
{

    public sealed class VaultService
    {
        private readonly VaultOptions _options;
        private readonly PhantomContainerService _containerService;
        private readonly EncryptionService _encryptionService;

        public VaultService() : this(new VaultOptions(), new EncryptionService())
        {
        }

        public VaultService(VaultOptions options) : this(options, new EncryptionService())
        {
        }

        public VaultService(VaultOptions options, EncryptionService encryptionService)
        {
            _options = options;
            _encryptionService = encryptionService;
            _containerService = new PhantomContainerService(encryptionService);
        }

        public async Task CreateVaultAsync(string containerPath, long sizeBytes, string? passphrase, string? keyfilePath = null, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(containerPath)) throw new ArgumentException("Container path must be provided", nameof(containerPath));
            if (sizeBytes <= 0) throw new ArgumentOutOfRangeException(nameof(sizeBytes));

            KeyfileGuard.Require(keyfilePath, nameof(keyfilePath));

            Directory.CreateDirectory(Path.GetDirectoryName(containerPath)!);

            if (File.Exists(containerPath))
            {
                File.Delete(containerPath);
            }

            await _containerService.CreateContainerAsync(
                containerPath,
                sizeBytes,
                passphrase,
                keyfilePath,
                null,
                progress,
                cancellationToken
            ).ConfigureAwait(false);
        }

        public async Task<string> MountVaultAsync(string containerPath, string mountName, string? passphrase, string? keyfilePath = null, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(containerPath)) throw new FileNotFoundException("Container file not found", containerPath);
            if (string.IsNullOrEmpty(mountName)) throw new ArgumentException("Mount name must be provided", nameof(mountName));

            await using (var validationStream = Stream.Null)
            {
                await _containerService.OpenContainerToStreamAsync(containerPath, validationStream, passphrase, keyfilePath, cancellationToken).ConfigureAwait(false);
            }

            string sessionRoot = Path.Combine(Path.GetTempPath(), "PhantomVaultSessions");
            Directory.CreateDirectory(sessionRoot);
            string sessionPath = Path.Combine(sessionRoot, $"{SanitizeMountName(mountName)}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(sessionPath);
            return sessionPath;
        }

        public async Task DismountVaultAsync(string mountPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(mountPath)) throw new ArgumentException("Mount path must be provided", nameof(mountPath));

            if (Directory.Exists(mountPath))
            {
                try
                {
                    Directory.Delete(mountPath, recursive: true);
                }
                catch (Exception ex)
                {

                    Debug.WriteLine($"Warning: Could not delete temp directory {mountPath}: {ex.Message}");
                }
            }

            await Task.CompletedTask;
        }

        public async Task<Stream> OpenVaultPayloadStreamAsync(string containerPath, string? passphrase, string? keyfilePath = null, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(containerPath)) throw new FileNotFoundException("Container file not found", containerPath);

            var payloadStream = new MemoryStream();
            await _containerService.OpenContainerToStreamAsync(containerPath, payloadStream, passphrase, keyfilePath, cancellationToken).ConfigureAwait(false);
            payloadStream.Position = 0;
            return payloadStream;
        }

        public async Task SaveVaultPayloadAsync(string containerPath, Stream payloadStream, string? passphrase, string? keyfilePath = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(containerPath)) throw new ArgumentException("Container path must be provided", nameof(containerPath));
            if (payloadStream == null || !payloadStream.CanRead) throw new ArgumentException("Payload stream must be readable", nameof(payloadStream));

            long existingPayloadSize = File.Exists(containerPath)
                ? await _containerService.GetPayloadSizeAsync(containerPath, passphrase, keyfilePath, cancellationToken).ConfigureAwait(false)
                : payloadStream.Length;

            if (payloadStream.CanSeek)
            {
                payloadStream.Position = 0;
            }

            long requiredPayloadSize = payloadStream.CanSeek ? payloadStream.Length : existingPayloadSize;
            long finalPayloadSize = Math.Max(existingPayloadSize, requiredPayloadSize);
            string tempPath = Path.Combine(Path.GetDirectoryName(containerPath) ?? AppContext.BaseDirectory, $"{Path.GetFileName(containerPath)}.{Guid.NewGuid():N}.tmp");

            await _containerService.CreateContainerFromStreamAsync(
                tempPath,
                payloadStream,
                finalPayloadSize,
                passphrase,
                keyfilePath,
                manifest: null,
                progress: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (File.Exists(containerPath))
            {
                File.Delete(containerPath);
            }

            File.Move(tempPath, containerPath);
        }

        private static string SanitizeMountName(string mountName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(mountName.Length);
            foreach (char ch in mountName)
            {
                builder.Append(invalidChars.Contains(ch) ? '_' : ch);
            }

            return builder.Length == 0 ? "vault" : builder.ToString();
        }
    }
}

