using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.Privileged;

namespace PhantomVault.PrivilegedBroker
{
    /// <summary>
    /// Hardened named-pipe server. One request per connection: the client sends a
    /// single <see cref="BrokerRequest"/> JSON line; the server verifies the caller,
    /// executes the operation in-process (elevated), and streams progress + a final
    /// result/error line back.
    /// </summary>
    internal sealed class BrokerPipeServer
    {
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

        private readonly Action<string> _log;
        private readonly UsbWriteProtectionService _writeProtection = new();
        private readonly BlackSecureRawVolumeService _rawVolume = new();
        private readonly PhantomVolumeService _phantomVolume = new();

        public BrokerPipeServer(Action<string> log)
        {
            _log = log;
            // This process is the elevated authority; never broker back to itself.
            PrivilegedExecution.ForceInProcess = true;
        }

        public async Task RunAsync(CancellationToken stoppingToken)
        {
            _log($"Broker pipe server starting on '{BrokerProtocol.PipeName}'.");
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var server = CreateSecuredPipe();
                    await server.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                    await HandleConnectionAsync(server, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _log($"Accept loop error: {ex.Message}");
                    await Task.Delay(500, stoppingToken).ConfigureAwait(false);
                }
            }
            _log("Broker pipe server stopped.");
        }

        private static NamedPipeServerStream CreateSecuredPipe()
        {
            var security = new PipeSecurity();

            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var authedUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);

            security.AddAccessRule(new PipeAccessRule(system, PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(admins, PipeAccessRights.FullControl, AccessControlType.Allow));
            // The non-elevated UI needs to connect and exchange messages.
            security.AddAccessRule(new PipeAccessRule(
                authedUsers,
                PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                AccessControlType.Allow));

            return NamedPipeServerStreamAcl.Create(
                BrokerProtocol.PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                inBufferSize: 0,
                outBufferSize: 0,
                pipeSecurity: security);
        }

        private async Task HandleConnectionAsync(NamedPipeServerStream server, CancellationToken stoppingToken)
        {
            string? clientPath = NativeMethods.TryGetClientProcessPath(server.SafePipeHandle);
            if (!IsClientAllowed(clientPath))
            {
                _log($"Rejected connection from unverified client: '{clientPath ?? "<unknown>"}'.");
                try { server.Disconnect(); } catch { }
                return;
            }

            using var reader = new StreamReader(server, new UTF8Encoding(false), false, 4096, leaveOpen: true);
            using var writer = new StreamWriter(server, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
            var writeGate = new object();

            try
            {
                string? line = await reader.ReadLineAsync(stoppingToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line))
                    return;

                BrokerRequest? request = JsonSerializer.Deserialize<BrokerRequest>(line, Json);
                if (request is null || request.ProtocolVersion != BrokerProtocol.ProtocolVersion)
                {
                    WriteMessage(writer, writeGate, Error("Unsupported or malformed request."));
                    return;
                }

                await DispatchAsync(request, writer, writeGate, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log($"Request handling error: {ex.Message}");
                try { WriteMessage(writer, writeGate, Error(ex.Message)); } catch { }
            }
            finally
            {
                try { server.Disconnect(); } catch { }
            }
        }

        private static bool IsClientAllowed(string? clientPath)
        {
            if (string.IsNullOrWhiteSpace(clientPath))
                return false;

            string? allowed = BrokerConfig.LoadAllowedClientPath();
            if (string.IsNullOrWhiteSpace(allowed))
                return false; // fail closed until install records the allow-listed UI exe

            try
            {
                return string.Equals(
                    Path.GetFullPath(clientPath).TrimEnd('\\'),
                    Path.GetFullPath(allowed).TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private async Task DispatchAsync(BrokerRequest request, StreamWriter writer, object writeGate, CancellationToken ct)
        {
            switch (request.Operation)
            {
                case BrokerOperation.Ping:
                    WriteMessage(writer, writeGate, BoolResult(true));
                    break;

                case BrokerOperation.ApplyProtection:
                {
                    var state = string.IsNullOrEmpty(request.StateJson)
                        ? new UsbWriteProtectionState()
                        : JsonSerializer.Deserialize<UsbWriteProtectionState>(request.StateJson, Json) ?? new UsbWriteProtectionState();
                    bool ok = _writeProtection.ApplyProtection(Require(request.DriveRoot, "driveRoot"), state);
                    var msg = BoolResult(ok);
                    msg.StateJson = JsonSerializer.Serialize(state, Json);
                    WriteMessage(writer, writeGate, msg);
                    break;
                }

                case BrokerOperation.EnableWriteAccess:
                    WriteMessage(writer, writeGate, BoolResult(_writeProtection.EnableWriteAccess(Require(request.DriveRoot, "driveRoot"))));
                    break;

                case BrokerOperation.DisableWriteAccess:
                    WriteMessage(writer, writeGate, BoolResult(_writeProtection.DisableWriteAccess(Require(request.DriveRoot, "driveRoot"))));
                    break;

                case BrokerOperation.CreateVolumeFromDirectory:
                    await _rawVolume.CreateVolumeFromDirectoryAsync(
                        Require(request.PhysicalDevicePath, "devicePath"),
                        Require(request.SourceRoot, "sourceRoot"), ct).ConfigureAwait(false);
                    WriteMessage(writer, writeGate, BoolResult(true));
                    break;

                case BrokerOperation.InvalidateVolumeHeader:
                    await _rawVolume.InvalidateVolumeHeaderAsync(
                        Require(request.PhysicalDevicePath, "devicePath"), ct).ConfigureAwait(false);
                    WriteMessage(writer, writeGate, BoolResult(true));
                    break;

                case BrokerOperation.ExtractVolume:
                {
                    var progress = new Progress<double>(p =>
                        WriteMessage(writer, writeGate, new BrokerMessage { Type = BrokerMessageType.Progress, Progress = p }));
                    string dest = await _rawVolume.ExtractVolumeAsync(
                        Require(request.PhysicalDevicePath, "devicePath"),
                        Require(request.DestinationRoot, "destRoot"),
                        progress,
                        request.Verify,
                        ct).ConfigureAwait(false);
                    WriteMessage(writer, writeGate, new BrokerMessage { Type = BrokerMessageType.Result, StringResult = dest });
                    break;
                }

                case BrokerOperation.IsBlackSecureVolume:
                    WriteMessage(writer, writeGate, BoolResult(
                        await _rawVolume.IsBlackSecureVolumeAsync(Require(request.PhysicalDevicePath, "devicePath"), ct).ConfigureAwait(false)));
                    break;

                case BrokerOperation.ProvisionPhantomVolume:
                    WriteMessage(writer, writeGate, BoolResult(
                        _phantomVolume.Provision(Require(request.ContainerPath, "containerPath"), request.SizeBytes)));
                    break;

                case BrokerOperation.MountPhantomVolume:
                {
                    var root = _phantomVolume.Mount(Require(request.ContainerPath, "containerPath"));
                    WriteMessage(writer, writeGate, new BrokerMessage
                    {
                        Type = BrokerMessageType.Result,
                        StringResult = root,
                        BoolResult = !string.IsNullOrEmpty(root)
                    });
                    break;
                }

                case BrokerOperation.UnmountPhantomVolume:
                    WriteMessage(writer, writeGate, BoolResult(
                        _phantomVolume.Unmount(Require(request.ContainerPath, "containerPath"))));
                    break;

                default:
                    WriteMessage(writer, writeGate, Error($"Unknown operation {request.Operation}."));
                    break;
            }
        }

        private static string Require(string? value, string name)
            => string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException($"Missing required argument '{name}'.")
                : value;

        private static void WriteMessage(StreamWriter writer, object gate, BrokerMessage message)
        {
            string line = JsonSerializer.Serialize(message, Json);
            lock (gate)
            {
                writer.WriteLine(line);
            }
        }

        private static BrokerMessage BoolResult(bool value)
            => new() { Type = BrokerMessageType.Result, BoolResult = value };

        private static BrokerMessage Error(string message)
            => new() { Type = BrokerMessageType.Error, Message = message };
    }
}
