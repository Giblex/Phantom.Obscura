using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Provides functionality to exclude clipboard data from Windows Clipboard History.
    /// Windows 10 version 1809+ stores clipboard history which can expose sensitive data.
    /// This service uses special clipboard formats to prevent history storage.
    /// </summary>
    public static class ClipboardHistoryExclusion
    {
        // Windows Clipboard Format constants
        private const uint CF_TEXT = 1;
        private const uint CF_UNICODETEXT = 13;

        // Windows Clipboard History exclusion format
        // When this format is present, Windows excludes the data from clipboard history
        private const string CLIPBOARD_EXCLUDE_FORMAT = "ExcludeClipboardContentFromMonitorProcessing";

        // P/Invoke declarations for clipboard manipulation
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint RegisterClipboardFormat(string lpszFormat);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr hMem);

        // GlobalAlloc flags
        private const uint GMEM_MOVEABLE = 0x0002;
        private const uint GMEM_ZEROINIT = 0x0040;
        private const uint GHND = GMEM_MOVEABLE | GMEM_ZEROINIT;

        private static uint? _excludeFormat;
        private static readonly object _lock = new();

        /// <summary>
        /// Checks if clipboard history exclusion is supported on this platform.
        /// Only supported on Windows 10 version 1809 and later.
        /// </summary>
        public static bool IsSupported()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        }

        /// <summary>
        /// Copies text to clipboard while excluding it from Windows Clipboard History.
        /// The data will still be available for immediate paste but won't appear in
        /// clipboard history (Win+V) or be synced across devices.
        /// </summary>
        /// <param name="text">The text to copy to clipboard.</param>
        /// <returns>True if the operation succeeded.</returns>
        public static bool CopyWithExclusion(string text)
        {
            if (!IsSupported())
            {
                return false;
            }

            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            lock (_lock)
            {
                // Register the exclusion format if not already done
                _excludeFormat ??= RegisterClipboardFormat(CLIPBOARD_EXCLUDE_FORMAT);

                if (_excludeFormat == 0)
                {
                    return false;
                }

                IntPtr hGlobalText = IntPtr.Zero;
                IntPtr hGlobalExclude = IntPtr.Zero;

                try
                {
                    if (!OpenClipboard(IntPtr.Zero))
                    {
                        return false;
                    }

                    if (!EmptyClipboard())
                    {
                        CloseClipboard();
                        return false;
                    }

                    // Allocate and set the text data
                    var textBytes = System.Text.Encoding.Unicode.GetBytes(text + "\0");
                    hGlobalText = GlobalAlloc(GHND, (UIntPtr)textBytes.Length);

                    if (hGlobalText == IntPtr.Zero)
                    {
                        CloseClipboard();
                        return false;
                    }

                    var pText = GlobalLock(hGlobalText);
                    if (pText == IntPtr.Zero)
                    {
                        GlobalFree(hGlobalText);
                        CloseClipboard();
                        return false;
                    }

                    Marshal.Copy(textBytes, 0, pText, textBytes.Length);
                    GlobalUnlock(hGlobalText);

                    // Set the text data (ownership transfers to clipboard)
                    if (SetClipboardData(CF_UNICODETEXT, hGlobalText) == IntPtr.Zero)
                    {
                        GlobalFree(hGlobalText);
                        CloseClipboard();
                        return false;
                    }
                    hGlobalText = IntPtr.Zero; // Ownership transferred

                    // Now add the exclusion marker format
                    // The data content doesn't matter - just the presence of this format
                    hGlobalExclude = GlobalAlloc(GHND, (UIntPtr)1);
                    if (hGlobalExclude != IntPtr.Zero)
                    {
                        var pExclude = GlobalLock(hGlobalExclude);
                        if (pExclude != IntPtr.Zero)
                        {
                            Marshal.WriteByte(pExclude, 0);
                            GlobalUnlock(hGlobalExclude);

                            if (SetClipboardData(_excludeFormat.Value, hGlobalExclude) != IntPtr.Zero)
                            {
                                hGlobalExclude = IntPtr.Zero; // Ownership transferred
                            }
                        }
                    }

                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
                finally
                {
                    // Clean up any memory that wasn't transferred to clipboard
                    if (hGlobalText != IntPtr.Zero)
                    {
                        GlobalFree(hGlobalText);
                    }
                    if (hGlobalExclude != IntPtr.Zero)
                    {
                        GlobalFree(hGlobalExclude);
                    }

                    CloseClipboard();
                }
            }
        }

        /// <summary>
        /// Copies text to clipboard with exclusion and schedules automatic clearing.
        /// </summary>
        /// <param name="text">The text to copy.</param>
        /// <param name="clearAfter">Time after which to clear the clipboard.</param>
        /// <returns>True if the initial copy succeeded.</returns>
        public static async Task<bool> CopyWithExclusionAndAutoClearAsync(string text, TimeSpan clearAfter)
        {
            if (!CopyWithExclusion(text))
            {
                return false;
            }

            // Schedule clipboard clearing
            _ = Task.Run(async () =>
            {
                await Task.Delay(clearAfter).ConfigureAwait(false);
                ClearClipboard();
            });

            return await Task.FromResult(true);
        }

        /// <summary>
        /// Clears the clipboard contents.
        /// </summary>
        public static bool ClearClipboard()
        {
            if (!IsSupported())
            {
                return false;
            }

            lock (_lock)
            {
                try
                {
                    if (!OpenClipboard(IntPtr.Zero))
                    {
                        return false;
                    }

                    var result = EmptyClipboard();
                    CloseClipboard();
                    return result;
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}
