using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using PhantomVault.Core.Models.AutoInject;
using Serilog;

#if WINDOWS
using System.Windows.Automation;
#endif

namespace PhantomVault.Core.Services.Platform.Windows
{

    public sealed class WindowsNativeLoginDetector
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int count);

        private static readonly string[] UsernameKeywords =
            { "user", "email", "login", "account", "mail", "username" };

        private static readonly string[] TotpKeywords =
            { "code", "otp", "totp", "mfa", "2fa", "token", "verification", "auth", "one-time" };

        public NativeLoginContext? DetectLoginFields()
        {
#if WINDOWS
            try
            {
                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return null;

                var root = AutomationElement.FromHandle(hwnd);
                if (root is null) return null;

                var editCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
                var allEdits = root.FindAll(TreeScope.Descendants, editCondition);

                string? usernameId = null;
                string? passwordId = null;

                foreach (AutomationElement edit in allEdits)
                {
                    try
                    {
                        var automationId = edit.Current.AutomationId ?? string.Empty;
                        var name = edit.Current.Name ?? string.Empty;
                        var isPassword = edit.Current.IsPassword;

                        var identifier = string.IsNullOrEmpty(automationId) ? name : automationId;

                        if (isPassword)
                        {
                            passwordId ??= identifier;
                        }
                        else if (usernameId is null)
                        {
                            var lowerName = name.ToLowerInvariant();
                            var lowerId = automationId.ToLowerInvariant();
                            if (UsernameKeywords.Any(k => lowerName.Contains(k) || lowerId.Contains(k)))
                            {
                                usernameId = identifier;
                            }
                        }
                    }
                    catch (ElementNotAvailableException) { }
                }

                if (usernameId is null || passwordId is null)
                    return null;

                GetWindowThreadProcessId(hwnd, out var pid);
                var proc = Process.GetProcessById((int)pid);
                var sb = new StringBuilder(256);
                GetWindowText(hwnd, sb, 256);

                return new NativeLoginContext
                {
                    WindowHandle = hwnd,
                    UsernameAutomationId = usernameId,
                    PasswordAutomationId = passwordId,
                    ProcessName = proc.ProcessName.ToLowerInvariant(),
                    WindowTitle = sb.ToString()
                };
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[NativeLoginDetector] Failed to detect login fields via UI Automation");
                return null;
            }
#else
            return null;
#endif
        }

        public async Task<bool> TryFillLoginAsync(NativeLoginContext context, string username, string password)
        {
#if WINDOWS
            try
            {
                var root = AutomationElement.FromHandle(context.WindowHandle);
                if (root is null) return false;

                var filled = 0;

                if (context.UsernameAutomationId is not null)
                    filled += SetValueById(root, context.UsernameAutomationId, username) ? 1 : 0;

                await Task.Delay(80);

                if (context.PasswordAutomationId is not null)
                    filled += SetValueById(root, context.PasswordAutomationId, password) ? 1 : 0;

                return filled == 2;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[NativeLoginDetector] TryFillLoginAsync failed — caller should use SendInput fallback");
                return false;
            }
#else
            await Task.CompletedTask;
            return false;
#endif
        }

        public async Task<bool> TryFillTotpAsync(NativeLoginContext context, string totpCode)
        {
#if WINDOWS
            try
            {
                if (context.TotpAutomationId is null) return false;

                var root = AutomationElement.FromHandle(context.WindowHandle);
                if (root is null) return false;

                var success = SetValueById(root, context.TotpAutomationId, totpCode);
                await Task.Delay(50);
                return success;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[NativeLoginDetector] TryFillTotpAsync failed");
                return false;
            }
#else
            await Task.CompletedTask;
            return false;
#endif
        }

        public NativeLoginContext? DetectTotpField(IntPtr hwnd)
        {
#if WINDOWS
            try
            {
                var root = AutomationElement.FromHandle(hwnd);
                if (root is null) return null;

                var editCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
                var allEdits = root.FindAll(TreeScope.Descendants, editCondition);

                foreach (AutomationElement edit in allEdits)
                {
                    try
                    {
                        if (edit.Current.IsPassword) continue;

                        var automationId = edit.Current.AutomationId ?? string.Empty;
                        var name = edit.Current.Name ?? string.Empty;
                        var lowerName = name.ToLowerInvariant();
                        var lowerId = automationId.ToLowerInvariant();

                        if (TotpKeywords.Any(k => lowerName.Contains(k) || lowerId.Contains(k)))
                        {
                            var identifier = string.IsNullOrEmpty(automationId) ? name : automationId;
                            return new NativeLoginContext
                            {
                                WindowHandle = hwnd,
                                TotpAutomationId = identifier
                            };
                        }
                    }
                    catch (ElementNotAvailableException) { }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[NativeLoginDetector] DetectTotpField failed");
            }
#endif
            return null;
        }

#if WINDOWS
        private static bool SetValueById(AutomationElement root, string automationId, string value)
        {
            try
            {

                AutomationElement? el = null;
                if (!string.IsNullOrEmpty(automationId))
                {
                    el = root.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
                }

                if (el is null)
                {
                    el = root.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.NameProperty, automationId));
                }

                if (el is null) return false;

                if (el.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) &&
                    pattern is ValuePattern vp)
                {
                    vp.SetValue(value);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[NativeLoginDetector] SetValueById failed for id={Id}", automationId);
                return false;
            }
        }
#endif
    }
}

