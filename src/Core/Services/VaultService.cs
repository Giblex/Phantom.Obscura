using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using PhantomVault.Core.Options;
using System.Linq;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Provides functionality to create, mount and unmount encrypted
    /// container files. This service wraps the VeraCrypt command line tool
    /// on Windows, Linux and macOS. To improve security, the path to the
    /// VeraCrypt binary is configurable and commands are executed via the
    /// safest available options. No passwords are ever passed on the
    /// command line; instead they are piped via standard input.
    /// </summary>
    public sealed class VaultService
    {
        private readonly VaultOptions _options;

        public VaultService(VaultOptions options)
        {
            _options = options;
        }

        /// <summary>
        /// Creates a new encrypted container file. On supported platforms this
        /// invokes VeraCrypt in command line mode. Because container creation
        /// may be long‑running, the method accepts a progress callback and
        /// cancellation token.
        /// </summary>
        /// <param name="containerPath">Absolute path to the new container.</param>
        /// <param name="sizeBytes">Container size in bytes.</param>
        /// <param name="passphrase">User passphrase. NOTE: Strings in .NET are immutable and cannot be securely wiped from memory. Callers should consider using SecureString or char arrays where possible.</param>
        /// <param name="keyfilePath">Optional path to a keyfile.</param>
        /// <param name="cancellationToken">Cancellation token to abort creation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task CreateVaultAsync(string containerPath, long sizeBytes, string? passphrase, string? keyfilePath = null, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(containerPath)) throw new ArgumentException("Container path must be provided", nameof(containerPath));
            if (File.Exists(containerPath)) throw new InvalidOperationException($"File already exists: {containerPath}");
            if (sizeBytes <= 0) throw new ArgumentOutOfRangeException(nameof(sizeBytes));

            // At least one authentication method (passphrase or keyfile) must be provided
            if (string.IsNullOrEmpty(passphrase) && string.IsNullOrEmpty(keyfilePath))
            {
                throw new ArgumentException("Either a passphrase or keyfile must be provided");
            }

            // Ensure the directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(containerPath)!);

            // Build the VeraCrypt command. Prefer stdin for password.
            static string Q(string s) => $"\"{s}\"";
            // VeraCrypt expects size with units; use MB to avoid large number issues.
            var sizeMB = (long)Math.Ceiling(sizeBytes / (1024.0 * 1024.0));
            var args = new List<string>
            {
                "/create", Q(containerPath),
                "/size", $"{sizeMB}M",
                "/encryption", "AES",
                "/hash", "SHA-512",
                "/filesystem", "NTFS",
                "/pim", "0",
                "/non-interactive",
                "/force",
                "/quiet"
            };

            if (!string.IsNullOrEmpty(keyfilePath))
            {
                args.Add("/keyfile");
                args.Add(Q(keyfilePath));
            }

            if (!string.IsNullOrEmpty(passphrase))
            {
                args.Add("/stdin");
            }
            else if (!string.IsNullOrEmpty(keyfilePath))
            {
                // Keyfile-only container: explicitly set empty password.
                args.Add("/p");
                args.Add("\"\"");
            }

            string argumentList = string.Join(" ", args);

            var startInfo = new ProcessStartInfo
            {
                FileName = _options.VeraCryptPath,
                Arguments = argumentList,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            var tcs = new TaskCompletionSource<int>();
            process.Exited += (_, __) => tcs.TrySetResult(process.ExitCode);
            process.Start();

            // Provide password via stdin (empty line allowed when only keyfile is used)
            // VeraCrypt expects password followed by newline
            // NOTE: String passphrase cannot be securely wiped from memory in .NET.
            // The GC will eventually collect it, but it may remain in memory until then.
            try
            {
                if (!string.IsNullOrEmpty(passphrase))
                {
                    // Convert to char array for more control, though still not perfect
                    var passphraseChars = passphrase.ToCharArray();
                    try
                    {
                        await process.StandardInput.WriteLineAsync(passphraseChars).ConfigureAwait(false);
                        await process.StandardInput.FlushAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        // Zero out the char array
                        if (passphraseChars != null)
                        {
                            Array.Clear(passphraseChars, 0, passphraseChars.Length);
                        }
                    }
                }
                process.StandardInput.Close();
            }
            catch { /* best effort */ }

            // Clear local reference to passphrase parameter (though the string itself can't be wiped)
            passphrase = null;

            // Monitor progress by reading output. Because VeraCrypt does not
            // provide progress events, we periodically report an estimate
            // based on file length. This is approximate.
            var progressTask = Task.Run(async () =>
            {
                while (!process.HasExited && progress != null)
                {
                    if (File.Exists(containerPath))
                    {
                        long length = new FileInfo(containerPath).Length;
                        double ratio = Math.Min(1.0, (double)length / sizeBytes);
                        progress.Report(ratio);
                    }
                    await Task.Delay(1000, cancellationToken);
                }
            }, cancellationToken);

            using (cancellationToken.Register(() => process.Kill(true)))
            {
                await tcs.Task.ConfigureAwait(false);
            }

            await progressTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                string error = await process.StandardError.ReadToEndAsync();
                string output = await process.StandardOutput.ReadToEndAsync();
                var details = string.IsNullOrWhiteSpace(error) ? output : error;
                throw new InvalidOperationException($"VeraCrypt failed with exit code {process.ExitCode}: {details}");
            }
        }

        /// <summary>
        /// Mounts an existing encrypted container. The mounted volume path
        /// depends on the platform. On Windows this will be a drive letter,
        /// while on Linux/macOS it will be under the specified mount point.
        /// </summary>
        public async Task<string> MountVaultAsync(string containerPath, string mountName, string passphrase, string? keyfilePath = null, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(containerPath)) throw new FileNotFoundException("Container file not found", containerPath);
            if (string.IsNullOrEmpty(passphrase)) throw new ArgumentException("Passphrase must be provided", nameof(passphrase));
            if (string.IsNullOrEmpty(mountName)) throw new ArgumentException("Mount name must be provided", nameof(mountName));

            string mountPath;
            if (OperatingSystem.IsWindows())
            {
                // Choose a free drive letter; return path as X:\
                char letter = GetFreeDriveLetter();
                mountPath = $"{letter}:\\";
            }
            else
            {
                mountPath = Path.Combine(_options.MountRoot, mountName);
                Directory.CreateDirectory(mountPath);
            }

            static string QQ(string s) => $"\"{s}\"";
            var args = new List<string>
            {
                "/mount", QQ(containerPath),
                "/pim", "0",
                "/non-interactive",
                "/force",
                "/quit"
            };

            if (OperatingSystem.IsWindows())
            {
                args.Insert(2, "/letter");
                args.Insert(3, mountPath[0].ToString());
            }
            else
            {
                args.Insert(2, QQ(mountPath));
            }

            if (!string.IsNullOrEmpty(keyfilePath))
            {
                args.Add("/keyfile");
                args.Add(QQ(keyfilePath));
            }

            if (!string.IsNullOrEmpty(passphrase))
            {
                args.Add("/stdin");
            }
            else if (!string.IsNullOrEmpty(keyfilePath))
            {
                args.Add("/p");
                args.Add("\"\"");
            }

            string argumentList = string.Join(" ", args);
            var startInfo = new ProcessStartInfo
            {
                FileName = _options.VeraCryptPath,
                Arguments = argumentList,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(startInfo)!;
            try
            {
                if (!string.IsNullOrEmpty(passphrase))
                {
                    // Convert to char array for secure handling, matching CreateVaultAsync behavior
                    var passphraseChars = passphrase.ToCharArray();
                    try
                    {
                        await process.StandardInput.WriteLineAsync(passphraseChars).ConfigureAwait(false);
                        await process.StandardInput.FlushAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        // Zero out the char array to minimize exposure in memory
                        if (passphraseChars != null)
                        {
                            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(passphraseChars.AsSpan()));
                        }
                    }
                }
                process.StandardInput.Close();
            }
            catch { /* best effort */ }

            // Clear local reference to passphrase parameter (though the string itself can't be wiped)
            passphrase = null!;

            using (cancellationToken.Register(() => process.Kill(true)))
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            if (process.ExitCode != 0)
            {
                string error = await process.StandardError.ReadToEndAsync();
                string output = await process.StandardOutput.ReadToEndAsync();
                var details = string.IsNullOrWhiteSpace(error) ? output : error;
                throw new InvalidOperationException($"Failed to mount container (exit {process.ExitCode}): {details}");
            }
            return mountPath;
        }

        /// <summary>
        /// Dismounts a mounted volume. The mount identifier must match what
        /// was passed to <see cref="MountVaultAsync"/>. If the volume
        /// cannot be unmounted, an exception is thrown.
        /// </summary>
        public async Task DismountVaultAsync(string mountName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(mountName)) throw new ArgumentException("Mount name must be provided", nameof(mountName));
            string argumentList;
            if (OperatingSystem.IsWindows())
            {
                // Accept either a full path like X:\ or just the letter X
                char letter = mountName[0];
                if (!char.IsLetter(letter) && mountName.Length > 1 && char.IsLetter(mountName[0]))
                    letter = char.ToUpperInvariant(mountName[0]);
                var argsWin = new[] { "/dismount", "/letter", letter.ToString(), "/quit", "/force" };
                argumentList = string.Join(" ", argsWin);
            }
            else
            {
                string mountPath = Path.Combine(_options.MountRoot, mountName);
                var argsNix = new[] { "/dismount", mountPath, "/force" };
                argumentList = string.Join(" ", argsNix);
            }
            var startInfo = new ProcessStartInfo
            {
                FileName = _options.VeraCryptPath,
                Arguments = argumentList,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(startInfo)!;
            using (cancellationToken.Register(() => process.Kill(true)))
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            if (process.ExitCode != 0)
            {
                string error = await process.StandardError.ReadToEndAsync();
                string output = await process.StandardOutput.ReadToEndAsync();
                var details = string.IsNullOrWhiteSpace(error) ? output : error;
                throw new InvalidOperationException($"Failed to dismount volume (exit {process.ExitCode}): {details}");
            }
        }

        private static char GetFreeDriveLetter()
        {
            // Prefer starting from P: upwards to Z: to avoid common system letters
            var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
            for (char c = 'P'; c <= 'Z'; c++)
            {
                if (!used.Contains(c)) return c;
            }
            // Fallback
            for (char c = 'D'; c < 'P'; c++)
            {
                if (!used.Contains(c)) return c;
            }
            return 'X';
        }
    }
}
