using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using Serilog;

namespace PhantomVault.UI.Services.Sync
{
    /// <summary>
    /// One-shot client used to push TOTP entries to PhantomAttestor's
    /// <see cref="TotpSyncPipeServer"/>-equivalent over a named pipe. Purely additive:
    /// if the other app isn't running or the pipe rejects the connection, the caller
    /// falls back to the existing totp-sync.json file-watcher transport.
    /// </summary>
    public sealed class TotpSyncPipeClient
    {
        public const string TargetPipeName = "PhantomAttestorTotpSync";

        public async Task<bool> TryPushEntriesAsync(List<SharedTotpEntry> entries, int timeoutMs = 1000, CancellationToken ct = default)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", TargetPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(timeoutMs, ct);

                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

                var request = JsonSerializer.Serialize(new { action = "pushEntries", entries });
                await writer.WriteLineAsync(request.AsMemory(), ct);

                var response = await reader.ReadLineAsync(ct);
                if (string.IsNullOrEmpty(response)) return false;

                using var doc = JsonDocument.Parse(response);
                return doc.RootElement.TryGetProperty("success", out var success) && success.GetBoolean();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "TotpSyncPipeClient: push to {Pipe} failed (PhantomAttestor likely not running); falling back to file sync", TargetPipeName);
                return false;
            }
        }
    }
}
