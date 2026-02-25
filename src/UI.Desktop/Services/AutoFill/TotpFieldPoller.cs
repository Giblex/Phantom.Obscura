using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models.AutoInject;
using PhantomVault.Core.Services.Autofill;
using PhantomVault.Core.Services.Platform.Windows;
using Serilog;

namespace PhantomVault.UI.Services.AutoFill
{
    /// <summary>
    /// Polls for a TOTP / 2FA input field to appear after the password fill step.
    ///
    /// Browser path: Listens for a <see cref="NativeMessagingHostService.FormDetected"/>
    /// event that contains a field classified as <c>FormFieldType.TwoFactor</c>.
    ///
    /// Native-app path: Polls <see cref="WindowsNativeLoginDetector.DetectTotpField"/>
    /// every <paramref name="pollIntervalMs"/> milliseconds.
    /// </summary>
    public sealed class TotpFieldPoller : ITotpFieldPoller
    {
        private static readonly string[] TotpKeywords =
            { "2fa", "mfa", "otp", "token", "code", "verification-code", "auth-code", "totp", "one-time" };

        private readonly NativeMessagingHostService? _nativeHost;
        private readonly WindowsNativeLoginDetector _nativeDetector;
        private readonly FormFieldDetector _fieldDetector;

        public TotpFieldPoller(
            WindowsNativeLoginDetector nativeDetector,
            FormFieldDetector fieldDetector,
            NativeMessagingHostService? nativeHost = null)
        {
            _nativeDetector = nativeDetector;
            _fieldDetector = fieldDetector;
            _nativeHost = nativeHost;
        }

        public async Task<TotpFieldDescriptor?> WaitForTotpFieldAsync(
            AutoInjectContext context,
            bool isBrowserContext,
            int pollIntervalMs = 500,
            int timeoutMs = 8000,
            CancellationToken ct = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            try
            {
                if (isBrowserContext)
                    return await WaitForBrowserTotpFieldAsync(cts.Token);
                else
                    return await PollNativeAppTotpFieldAsync(context, pollIntervalMs, cts.Token);
            }
            catch (OperationCanceledException)
            {
                Log.Debug("[TotpFieldPoller] TOTP field not detected within {Timeout}ms", timeoutMs);
                return null;
            }
        }

        // --- Browser path ---

        private Task<TotpFieldDescriptor?> WaitForBrowserTotpFieldAsync(CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<TotpFieldDescriptor?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            ct.Register(() => tcs.TrySetResult(null));

            if (_nativeHost is null)
            {
                tcs.TrySetResult(null);
                return tcs.Task;
            }

            void OnFormDetected(object? sender, FormDetectedEventArgs e)
            {
                var totpField = e.Fields.FirstOrDefault(f =>
                {
                    var type = _fieldDetector.DetectFieldType(f);
                    return type == FormFieldType.TwoFactor;
                });

                if (totpField is null) return;

                _nativeHost.FormDetected -= OnFormDetected;

                tcs.TrySetResult(new TotpFieldDescriptor
                {
                    IsBrowserField = true,
                    BrowserFieldId = string.IsNullOrEmpty(totpField.Id) ? totpField.Name : totpField.Id,
                    MaxLength = InferMaxLength(totpField)
                });
            }

            _nativeHost.FormDetected += OnFormDetected;
            return tcs.Task;
        }

        // --- Native-app path ---

        private async Task<TotpFieldDescriptor?> PollNativeAppTotpFieldAsync(
            AutoInjectContext context,
            int pollIntervalMs,
            CancellationToken ct)
        {
            if (!context.Metadata.TryGetValue("WindowHandle", out var handleStr) ||
                !long.TryParse(handleStr, out var handleLong))
                return null;

            var hwnd = new IntPtr(handleLong);

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(pollIntervalMs, ct);

                var result = _nativeDetector.DetectTotpField(hwnd);
                if (result?.TotpAutomationId is not null)
                {
                    Log.Debug("[TotpFieldPoller] Native TOTP field detected: {Id}", result.TotpAutomationId);
                    return new TotpFieldDescriptor
                    {
                        IsBrowserField = false,
                        NativeAutomationId = result.TotpAutomationId,
                        MaxLength = 6
                    };
                }
            }

            return null;
        }

        private static int InferMaxLength(FormFieldInfo field)
        {
            // Try to derive from autocomplete attribute or field name heuristics
            // Common TOTP lengths are 6 (TOTP) or 8 (HOTP extended)
            var combined = $"{field.Id} {field.Name} {field.Label} {field.AutoComplete}".ToLowerInvariant();
            return combined.Contains("backup") || combined.Contains("recovery") ? 8 : 6;
        }
    }
}
