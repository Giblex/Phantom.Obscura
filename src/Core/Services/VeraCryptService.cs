using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Minimal VeraCrypt CLI wrapper implementing IVeraCryptService.
    /// Writes password to stdin, captures stdout/stderr, and returns a VeraCryptResult.
    /// Includes timeout logic, exit code mapping, and retry mechanisms for robustness.
    /// VeraCrypt is optional - the service gracefully handles cases where it's not installed.
    /// </summary>
    public sealed class VeraCryptService : IVeraCryptService
    {
        private readonly string _exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "VeraCrypt.exe" : "veracrypt";
        private readonly string _veraCryptPath;
        private readonly CircuitBreaker _circuitBreaker;

        // Default timeout for operations (5 minutes for container creation, 30 seconds for mount/dismount)
        private static readonly TimeSpan DefaultCreateTimeout = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan DefaultMountTimeout = TimeSpan.FromSeconds(30);
        private const int MaxRetries = 3;

        // Circuit breaker configuration
        private const int CircuitBreakerFailureThreshold = 5;
        private static readonly TimeSpan CircuitBreakerResetTimeout = TimeSpan.FromMinutes(2);

        public VeraCryptService()
        {
            _veraCryptPath = FindVeraCryptExecutable();
            _circuitBreaker = new CircuitBreaker(CircuitBreakerFailureThreshold, CircuitBreakerResetTimeout);
        }

        public string VeraCryptPath => _veraCryptPath;

        public bool IsVeraCryptInstalled()
        {
            return !string.IsNullOrEmpty(_veraCryptPath) && File.Exists(_veraCryptPath);
        }

        private string FindVeraCryptExecutable()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Check common Windows installation paths
                string[] possiblePaths =
                {
                    @"C:\Program Files\VeraCrypt\VeraCrypt.exe",
                    @"C:\Program Files (x86)\VeraCrypt\VeraCrypt.exe",
                    @"C:\VeraCrypt\VeraCrypt.exe"
                };

                foreach (var path in possiblePaths)
                {
                    if (File.Exists(path))
                        return path;
                }

                // Try PATH environment variable
                var pathEnv = Environment.GetEnvironmentVariable("PATH");
                if (!string.IsNullOrEmpty(pathEnv))
                {
                    var paths = pathEnv.Split(Path.PathSeparator);
                    foreach (var dir in paths)
                    {
                        var fullPath = Path.Combine(dir, "VeraCrypt.exe");
                        if (File.Exists(fullPath))
                            return fullPath;
                    }
                }
            }
            else
            {
                // Linux/macOS - check for veracrypt in PATH
                return "veracrypt";
            }

            return string.Empty;
        }

        public Task<VeraCryptResult> CreateVolumeAsync(string containerPath, ReadOnlySpan<char> password, long sizeBytes, CancellationToken cancellationToken = default)
        {
            return CreateVolumeAsync(containerPath, new string(password.ToArray()), sizeBytes, null, cancellationToken);
        }

        public Task<VeraCryptResult> CreateVolumeAsync(string containerPath, ReadOnlySpan<char> password, long sizeBytes, string? keyfilePath, CancellationToken cancellationToken = default)
        {
            return CreateVolumeAsync(containerPath, new string(password.ToArray()), sizeBytes, keyfilePath, cancellationToken);
        }

        private async Task<VeraCryptResult> CreateVolumeAsync(string containerPath, string password, long sizeBytes, string? keyfilePath, CancellationToken cancellationToken)
        {
            if (!IsVeraCryptInstalled())
            {
                return NotInstalledResult();
            }

            Directory.CreateDirectory(Path.GetDirectoryName(containerPath) ?? ".");
            using var fs = new FileStream(containerPath, FileMode.Create, FileAccess.Write, FileShare.None);
            fs.SetLength(sizeBytes);

            var args = $"/create \"{containerPath}\" /size {sizeBytes} /encryption AES /hash SHA-512 /filesystem none /quiet";
            
            // Add keyfile if provided
            if (!string.IsNullOrEmpty(keyfilePath))
            {
                args += $" /keyfile \"{keyfilePath}\"";
            }
            
            // Use /stdin only if password is provided
            if (!string.IsNullOrEmpty(password))
            {
                args += " /stdin";
            }
            
            return await RunProcessWithPasswordAsync(args, password, cancellationToken, DefaultCreateTimeout).ConfigureAwait(false);
        }

        public Task<VeraCryptResult> MountVolumeAsync(string containerPath, char driveLetter, ReadOnlySpan<char> password, CancellationToken cancellationToken = default)
        {
            return MountVolumeAsync(containerPath, driveLetter, new string(password.ToArray()), null, cancellationToken);
        }

        public Task<VeraCryptResult> MountVolumeAsync(string containerPath, char driveLetter, ReadOnlySpan<char> password, string? keyfilePath, CancellationToken cancellationToken = default)
        {
            return MountVolumeAsync(containerPath, driveLetter, new string(password.ToArray()), keyfilePath, cancellationToken);
        }

        private Task<VeraCryptResult> MountVolumeAsync(string containerPath, char driveLetter, string password, string? keyfilePath, CancellationToken cancellationToken)
        {
            if (!IsVeraCryptInstalled())
            {
                return Task.FromResult(NotInstalledResult());
            }

            var letter = char.ToUpperInvariant(driveLetter);
            var args = $"/mount \"{containerPath}\" /letter {letter} /quit";
            
            // Add keyfile if provided
            if (!string.IsNullOrEmpty(keyfilePath))
            {
                args += $" /keyfile \"{keyfilePath}\"";
            }
            
            // Use /stdin only if password is provided (allows keyfile-only mounting)
            if (!string.IsNullOrEmpty(password))
            {
                args += " /stdin";
            }
            else
            {
                // Keyfile-only mode: use /p "" to indicate empty password with keyfile
                args += " /p \"\"";
            }
            
            return RunProcessWithPasswordAsync(args, password, cancellationToken, DefaultMountTimeout);
        }

        public Task<VeraCryptResult> DismountVolumeAsync(char driveLetter, CancellationToken cancellationToken = default)
        {
            if (!IsVeraCryptInstalled())
            {
                return Task.FromResult(NotInstalledResult());
            }

            var letter = char.ToUpperInvariant(driveLetter);
            var args = $"/dismount /letter {letter} /quit";
            return RunProcessWithPasswordAsync(args, string.Empty, cancellationToken, DefaultMountTimeout);
        }

        public async Task<(bool success, string message)> CreateContainerAsync(
            string containerPath,
            int sizeMB,
            string password,
            string encryptionAlgorithm,
            string hashAlgorithm,
            string filesystem,
            IProgress<int>? progress = null)
        {
            try
            {
                if (!IsVeraCryptInstalled())
                {
                    return (false, "VeraCrypt is not installed or could not be found. " +
                        "Please install VeraCrypt from https://www.veracrypt.fr/ to create encrypted containers. " +
                        "You can continue using PhantomVault without VeraCrypt for basic vault functionality.");
                }

                // Create directory if needed
                var dir = Path.GetDirectoryName(containerPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // Create placeholder file
                long sizeBytes = (long)sizeMB * 1024 * 1024;
                using var fs = new FileStream(containerPath, FileMode.Create, FileAccess.Write, FileShare.None);
                fs.SetLength(sizeBytes);

                // Build arguments
                var encryption = MapEncryptionAlgorithm(encryptionAlgorithm);
                var hash = MapHashAlgorithm(hashAlgorithm);
                var fs_arg = MapFilesystem(filesystem);

                var args = $"/create \"{containerPath}\" /size {sizeMB}M /encryption {encryption} /hash {hash}";
                if (filesystem != "None" && filesystem != "None (format later)")
                {
                    args += $" /filesystem {fs_arg}";
                }
                args += " /quit /stdin";

                // Report initial progress
                progress?.Report(10);

                // Execute VeraCrypt with timeout and retry logic
                var result = await ExecuteWithRetryAsync(
                    () => RunProcessWithPasswordAsync(args, password, CancellationToken.None, DefaultCreateTimeout),
                    maxRetries: 2 // Less retries for creation (it's usually not transient)
                ).ConfigureAwait(false);

                // Report completion
                progress?.Report(100);

                if (result.Success)
                {
                    return (true, "Container created successfully.");
                }
                else
                {
                    var errorMsg = MapExitCodeToMessage(result.ExitCode, result.StdErr, result.StdOut);
                    return (false, errorMsg);
                }
            }
            catch (TimeoutException)
            {
                return (false, "Operation timed out. Large containers may take several minutes to create. " +
                    "Please ensure VeraCrypt is not waiting for user input and try again.");
            }
            catch (Exception ex)
            {
                return (false, $"Error creating container: {ex.Message}");
            }
        }

        private string MapEncryptionAlgorithm(string algorithm)
        {
            return algorithm switch
            {
                "AES" => "aes",
                "Serpent" => "serpent",
                "Twofish" => "twofish",
                "AES-Twofish" => "aes-twofish",
                "AES-Twofish-Serpent" => "aes-twofish-serpent",
                "Serpent-AES" => "serpent-aes",
                "Serpent-Twofish-AES" => "serpent-twofish-aes",
                "Twofish-Serpent" => "twofish-serpent",
                _ => "aes"
            };
        }

        private string MapHashAlgorithm(string algorithm)
        {
            return algorithm switch
            {
                "SHA-512" => "sha512",
                "Whirlpool" => "whirlpool",
                "SHA-256" => "sha256",
                "Streebog" => "streebog",
                _ => "sha512"
            };
        }

        private string MapFilesystem(string filesystem)
        {
            return filesystem switch
            {
                "FAT" => "FAT",
                "exFAT" => "exFAT",
                "NTFS" => "NTFS",
                "ext4" => "ext4",
                _ => "FAT"
            };
        }

        /// <summary>
        /// Maps VeraCrypt exit codes to user-friendly error messages with troubleshooting guidance.
        /// </summary>
        private string MapExitCodeToMessage(int exitCode, string stderr, string stdout)
        {
            var baseMessage = exitCode switch
            {
                0 => "Operation completed successfully.",
                1 => "Incorrect password or corrupted volume header. Please verify your password and try again.",
                2 => "Volume file not found or inaccessible. Check the file path and permissions.",
                3 => "Volume already mounted. Please dismount it first.",
                4 => "Drive letter already in use. Choose a different drive letter.",
                5 => "Insufficient privileges. Run as administrator or with elevated permissions.",
                6 => "Operation cancelled by user.",
                7 => "Volume creation failed - disk may be full or path invalid.",
                8 => "Encryption algorithm not supported.",
                9 => "Filesystem format failed.",
                10 => "Random number generator initialization failed.",
                11 => "Volume header damaged or password incorrect.",
                12 => "Keyfile not found or invalid.",
                _ => $"VeraCrypt operation failed with exit code {exitCode}."
            };

            // Append sanitized stderr/stdout if available for additional context
            // Filter out empty lines, debug output, and noisy VeraCrypt messages
            var details = SanitizeVeraCryptOutput(stderr) ?? SanitizeVeraCryptOutput(stdout);
            if (!string.IsNullOrWhiteSpace(details))
            {
                baseMessage += $"\n\nDetails: {details}";
            }

            // Add general troubleshooting tip
            if (exitCode != 0)
            {
                baseMessage += "\n\nTroubleshooting:\n" +
                    "• Ensure VeraCrypt is installed and up to date\n" +
                    "• Verify you have write permissions to the target location\n" +
                    "• Check that no antivirus is blocking VeraCrypt\n" +
                    "• Try running the application as administrator";
            }

            return baseMessage;
        }

        /// <summary>
        /// Sanitizes VeraCrypt stdout/stderr output by removing debug noise, 
        /// empty lines, and cryptic error messages that confuse users.
        /// </summary>
        private static string? SanitizeVeraCryptOutput(string? output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return null;

            // Split into lines and filter out noise
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var meaningfulLines = new List<string>();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                
                // Skip empty or whitespace-only lines
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;
                
                // Skip debug/parsing noise that VeraCrypt sometimes outputs
                // Silenced per user request: parsing errors are not useful to end users
                if (trimmed.StartsWith("Error parsing", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("Parsing", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Contains("parsing", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Contains("parse error", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Contains("line ", StringComparison.OrdinalIgnoreCase) && trimmed.Length < 30 ||
                    trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal) ||
                    trimmed.Equals(".", StringComparison.Ordinal) ||
                    trimmed.Equals("..", StringComparison.Ordinal))
                {
                    continue;
                }

                // Keep meaningful error messages
                meaningfulLines.Add(trimmed);
            }

            if (meaningfulLines.Count == 0)
                return null;

            // Join and truncate if too long
            var result = string.Join(" ", meaningfulLines);
            if (result.Length > 500)
            {
                result = result.Substring(0, 497) + "...";
            }

            return result;
        }

        /// <summary>
        /// Executes an operation with retry logic for transient failures and circuit breaker protection.
        /// </summary>
        private async Task<VeraCryptResult> ExecuteWithRetryAsync(
            Func<Task<VeraCryptResult>> operation,
            int maxRetries = MaxRetries)
        {
            // Check circuit breaker first
            if (!_circuitBreaker.AllowRequest())
            {
                return new VeraCryptResult
                {
                    ExitCode = -1,
                    StdErr = "Circuit breaker is open - VeraCrypt operations are temporarily disabled due to repeated failures",
                    StdOut = string.Empty
                };
            }

            int attempt = 0;
            Exception? lastException = null;

            while (attempt < maxRetries)
            {
                try
                {
                    var result = await operation().ConfigureAwait(false);

                    // Success or non-transient error
                    if (result.Success || !IsTransientError(result.ExitCode))
                    {
                        if (result.Success)
                        {
                            _circuitBreaker.RecordSuccess();
                        }
                        else
                        {
                            _circuitBreaker.RecordFailure();
                        }
                        return result;
                    }

                    // Transient error - retry with exponential backoff
                    _circuitBreaker.RecordFailure();
                    lastException = null;
                }
                catch (Exception ex) when (IsRetriableException(ex))
                {
                    _circuitBreaker.RecordFailure();
                    lastException = ex;
                }

                attempt++;
                if (attempt < maxRetries)
                {
                    // Exponential backoff: 1s, 2s, 4s
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                    await Task.Delay(delay).ConfigureAwait(false);
                }
            }

            // All retries exhausted
            _circuitBreaker.RecordFailure();

            if (lastException != null)
            {
                throw new InvalidOperationException(
                    $"Operation failed after {maxRetries} attempts. Last error: {lastException.Message}",
                    lastException);
            }

            // Return failure result from last attempt
            return new VeraCryptResult
            {
                ExitCode = -1,
                StdErr = $"Operation failed after {maxRetries} attempts",
                StdOut = string.Empty
            };
        }

        /// <summary>
        /// Determines if an error code indicates a transient failure that may succeed on retry.
        /// </summary>
        private bool IsTransientError(int exitCode)
        {
            return exitCode switch
            {
                10 => true, // RNG initialization failure (may be transient)
                _ => false  // Most errors are not transient
            };
        }

        /// <summary>
        /// Determines if an exception is retriable (e.g., network issues, temporary resource unavailability).
        /// </summary>
        private bool IsRetriableException(Exception ex)
        {
            return ex is IOException or TimeoutException;
        }

        private async Task<VeraCryptResult> RunProcessWithPasswordAsync(
            string arguments,
            string password,
            CancellationToken cancellationToken,
            TimeSpan? timeout = null)
        {
            var exePath = !string.IsNullOrEmpty(_veraCryptPath) ? _veraCryptPath : _exeName;
            var effectiveTimeout = timeout ?? DefaultMountTimeout;

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start VeraCrypt process");
            
            // Register the process with the tracker so it can be cleaned up on app exit
            SpawnedProcessTracker.Instance.RegisterProcess(proc.Id, "VeraCrypt", arguments.Split(' ')[0]);
            
            using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                if (!string.IsNullOrEmpty(password))
                {
                    await proc.StandardInput.WriteLineAsync(password).ConfigureAwait(false);
                    await proc.StandardInput.FlushAsync().ConfigureAwait(false);
                    proc.StandardInput.Close();
                }

                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                var stderrTask = proc.StandardError.ReadToEndAsync();

                await proc.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
                
                // Process completed normally - unregister from tracker
                SpawnedProcessTracker.Instance.UnregisterProcess(proc.Id);;

                var stdout = await stdoutTask.ConfigureAwait(false);
                var stderr = await stderrTask.ConfigureAwait(false);

                return new VeraCryptResult { ExitCode = proc.ExitCode, StdOut = stdout, StdErr = stderr };
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                // Kill the process if it times out
                try
                {
                    if (!proc.HasExited)
                    {
                        proc.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Ignore errors during cleanup
                }

                throw new TimeoutException($"VeraCrypt operation timed out after {effectiveTimeout.TotalSeconds} seconds.");
            }
        }

        private static VeraCryptResult NotInstalledResult()
        {
            return new VeraCryptResult
            {
                ExitCode = -1,
                StdErr = "VeraCrypt is not installed on this system."
            };
        }
    }

    /// <summary>
    /// Simple circuit breaker implementation to prevent cascading failures
    /// </summary>
    internal sealed class CircuitBreaker
    {
        private enum State { Closed, Open, HalfOpen }

        private readonly int _failureThreshold;
        private readonly TimeSpan _resetTimeout;
        private readonly object _lock = new();

        private State _state = State.Closed;
        private int _failureCount = 0;
        private DateTimeOffset _lastFailureTime;

        public CircuitBreaker(int failureThreshold, TimeSpan resetTimeout)
        {
            _failureThreshold = failureThreshold;
            _resetTimeout = resetTimeout;
        }

        public bool AllowRequest()
        {
            lock (_lock)
            {
                if (_state == State.Closed)
                {
                    return true;
                }

                if (_state == State.Open)
                {
                    // Check if enough time has passed to try half-open
                    if (DateTimeOffset.UtcNow - _lastFailureTime >= _resetTimeout)
                    {
                        _state = State.HalfOpen;
                        return true;
                    }
                    return false;
                }

                // HalfOpen - allow one request through
                return true;
            }
        }

        public void RecordSuccess()
        {
            lock (_lock)
            {
                _failureCount = 0;
                _state = State.Closed;
            }
        }

        public void RecordFailure()
        {
            lock (_lock)
            {
                _failureCount++;
                _lastFailureTime = DateTimeOffset.UtcNow;

                if (_state == State.HalfOpen)
                {
                    // Failed in half-open, go back to open
                    _state = State.Open;
                }
                else if (_failureCount >= _failureThreshold)
                {
                    _state = State.Open;
                }
            }
        }
    }
}
