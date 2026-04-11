using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using PhantomVault.Core.Models.AutoInject;
using PhantomVault.Platform.Services;
using CoreActiveWindowDetector = PhantomVault.Core.Services.Platform.IActiveWindowDetector;
using PlatformActiveWindowDetector = PhantomVault.Platform.Services.IActiveWindowDetector;

namespace PhantomVault.Core.Services.Platform.Windows
{
    /// <summary>
    /// Windows implementation for detecting active window context using Win32 APIs,
    /// with UI Automation support for native login form detection via
    /// <see cref="WindowsNativeLoginDetector"/>.
    /// </summary>
    public class WindowsActiveWindowDetector : PlatformActiveWindowDetector, CoreActiveWindowDetector
    {
        private readonly WindowsNativeLoginDetector _nativeDetector = new();
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        private static readonly string[] SupportedBrowsers = new[]
        {
            "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc"
        };

        public AutoInjectContext GetCurrentContext()
        {
            var context = new AutoInjectContext();

            try
            {
                IntPtr handle = GetForegroundWindow();
                if (handle == IntPtr.Zero)
                    return context;

                // Get window title
                const int capacity = 256;
                var title = new StringBuilder(capacity);
                GetWindowText(handle, title, capacity);
                context.WindowTitle = title.ToString();

                // Get process info
                GetWindowThreadProcessId(handle, out uint processId);
                var process = Process.GetProcessById((int)processId);
                context.ProcessName = process.ProcessName.ToLowerInvariant();

                // Try to extract URL from browser
                if (IsActiveBrowser())
                {
                    context.Url = TryGetBrowserUrl();
                    if (!string.IsNullOrEmpty(context.Url))
                    {
                        context.Domain = ExtractDomain(context.Url);
                    }
                }

                // Add metadata
                context.Metadata["ProcessPath"] = process.MainModule?.FileName ?? string.Empty;
                context.Metadata["WindowHandle"] = handle.ToString();
            }
            catch (Exception ex)
            {
                // Log error but return partial context
                context.Metadata["Error"] = ex.Message;
            }

            return context;
        }

        public bool IsActiveBrowser()
        {
            try
            {
                IntPtr handle = GetForegroundWindow();
                if (handle == IntPtr.Zero)
                    return false;

                GetWindowThreadProcessId(handle, out uint processId);
                var process = Process.GetProcessById((int)processId);
                var processName = process.ProcessName.ToLowerInvariant();

                foreach (var browser in SupportedBrowsers)
                {
                    if (processName.Contains(browser))
                        return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public string? TryGetBrowserUrl()
        {
            try
            {
                IntPtr handle = GetForegroundWindow();
                if (handle == IntPtr.Zero)
                    return null;

                const int capacity = 256;
                var title = new StringBuilder(capacity);
                GetWindowText(handle, title, capacity);
                var windowTitle = title.ToString();

                // Chrome/Edge/Brave format: "Page Title - Domain - Chrome"
                // Firefox format: "Page Title — Mozilla Firefox"
                var url = ExtractUrlFromTitle(windowTitle);
                return url;
            }
            catch
            {
                return null;
            }
        }

        private string? ExtractUrlFromTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return null;

            // Try to find domain patterns in window title
            // Chrome/Edge format: "Title - google.com - Chrome"
            var match = Regex.Match(title, @" - ([a-zA-Z0-9\-\.]+\.[a-zA-Z]{2,}) - ");
            if (match.Success)
            {
                return $"https://{match.Groups[1].Value}";
            }

            // Look for URL patterns in title
            match = Regex.Match(title, @"https?://([a-zA-Z0-9\-\.]+\.[a-zA-Z]{2,})");
            if (match.Success)
            {
                return match.Value;
            }

            return null;
        }

        private string ExtractDomain(string url)
        {
            try
            {
                var uri = new Uri(url);
                return uri.Host;
            }
            catch
            {
                return url;
            }
        }

        /// <inheritdoc/>
        public NativeLoginContext? DetectNativeLoginFields()
            => _nativeDetector.DetectLoginFields();

        /// <inheritdoc/>
        public Task<bool> TryFillNativeLoginAsync(NativeLoginContext context, string username, string password)
            => _nativeDetector.TryFillLoginAsync(context, username, password);
    }
}
