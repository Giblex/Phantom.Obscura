using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PhantomVault.UI.Services.AutoFill
{
    /// <summary>
    /// Named pipe client used by the <c>--native-messaging</c> subprocess to send
    /// NDJSON requests to the running PhantomVault desktop instance and receive responses.
    /// </summary>
    public sealed class PipeNativeHostClient : IDisposable
    {
        private NamedPipeClientStream? _pipe;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public bool IsConnected => _pipe?.IsConnected ?? false;

        /// <summary>
        /// Attempts to connect to the desktop app pipe. Returns false if the app is not running.
        /// </summary>
        public async Task<bool> ConnectAsync(int timeoutMs = 3000, CancellationToken ct = default)
        {
            try
            {
                _pipe = new NamedPipeClientStream(
                    ".",
                    NativeHostPipeServer.PipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);

                await _pipe.ConnectAsync(timeoutMs, ct);
                _reader = new StreamReader(_pipe, Encoding.UTF8, leaveOpen: true);
                _writer = new StreamWriter(_pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
                return true;
            }
            catch
            {
                _pipe?.Dispose();
                _pipe = null;
                return false;
            }
        }

        /// <summary>
        /// Sends a JSON request line and returns the JSON response line. Thread-safe.
        /// Returns null if the connection is not established or was lost.
        /// </summary>
        public async Task<string?> SendAsync(string requestJson, CancellationToken ct = default)
        {
            if (_writer == null || _reader == null) return null;

            await _semaphore.WaitAsync(ct);
            try
            {
                await _writer.WriteLineAsync(requestJson.AsMemory(), ct);
                return await _reader.ReadLineAsync(ct);
            }
            catch
            {
                return null;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public void Dispose()
        {
            _reader?.Dispose();
            _writer?.Dispose();
            _pipe?.Dispose();
            _semaphore.Dispose();
        }
    }
}
