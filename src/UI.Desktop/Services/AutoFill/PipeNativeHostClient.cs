using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PhantomVault.UI.Services.AutoFill
{

    public sealed class PipeNativeHostClient : IDisposable
    {
        private NamedPipeClientStream? _pipe;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public bool IsConnected => _pipe?.IsConnected ?? false;

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

