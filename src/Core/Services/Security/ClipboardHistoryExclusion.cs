using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services.Security
{

    public static class ClipboardHistoryExclusion
    {

        private const uint CF_TEXT = 1;
        private const uint CF_UNICODETEXT = 13;

        private const string CLIPBOARD_EXCLUDE_FORMAT = "ExcludeClipboardContentFromMonitorProcessing";

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

        private const uint GMEM_MOVEABLE = 0x0002;
        private const uint GMEM_ZEROINIT = 0x0040;
        private const uint GHND = GMEM_MOVEABLE | GMEM_ZEROINIT;

        private static uint? _excludeFormat;
        private static readonly object _lock = new();

        public static bool IsSupported()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        }

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

                    if (SetClipboardData(CF_UNICODETEXT, hGlobalText) == IntPtr.Zero)
                    {
                        GlobalFree(hGlobalText);
                        CloseClipboard();
                        return false;
                    }
                    hGlobalText = IntPtr.Zero;

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
                                hGlobalExclude = IntPtr.Zero;
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

        public static async Task<bool> CopyWithExclusionAndAutoClearAsync(string text, TimeSpan clearAfter)
        {
            if (!CopyWithExclusion(text))
            {
                return false;
            }

            _ = Task.Run(async () =>
            {
                await Task.Delay(clearAfter).ConfigureAwait(false);
                ClearClipboard();
            });

            return await Task.FromResult(true);
        }

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

