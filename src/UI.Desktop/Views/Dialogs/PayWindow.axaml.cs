using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using PhantomVault.Core.Models.Licensing;
using PhantomVault.UI.Services.Licensing;

namespace PhantomVault.UI.Views.Dialogs
{
    /// <summary>
    /// Premium checkout window. Routes through <see cref="ILicensingClient"/>; the
    /// production client (<see cref="StripeLicensingClient"/>) opens Stripe's hosted
    /// checkout in the browser and waits for the signed token, while the env-gated
    /// dev authority issues one locally. No card data is collected in-app.
    /// On success, <see cref="ResultToken"/> holds the signed token to persist.
    /// </summary>
    public partial class PayWindow : Window
    {
        private readonly ILicensingClient? _client;
        private readonly string? _usbBindingId;
        private bool _activated;
        private bool _busy;

        public string? ResultToken { get; private set; }

        public PayWindow()
        {
            InitializeComponent();
        }

        public PayWindow(ILicensingClient client, string? usbBindingId) : this()
        {
            _client = client;
            _usbBindingId = usbBindingId;
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void OnCancel(object? sender, RoutedEventArgs e) => Close();

        // Hard-coded Stripe Payment Link fallback — used when the backend
        // isn't reachable (which is always, in this build). Replace with a
        // real link from your Stripe dashboard.
        private const string StripePaymentLink =
            "https://buy.stripe.com/aFa14n2VD3zdanIfe2eZ200";

        private async void OnSubscribe(object? sender, RoutedEventArgs e)
        {
            // Guard everything — async void means uncaught exceptions kill the app.
            try
            {
                if (_activated)
                {
                    Close();
                    return;
                }
                if (_busy) return;

                _busy = true;
                SubscribeButton.IsEnabled = false;

                // ALWAYS open the browser first — never leave the user staring
                // at a button that appears to do nothing. Backend flow (if
                // available) then polls for the signed token in the background.
                var link = Environment.GetEnvironmentVariable("PHANTOM_STRIPE_PAYMENT_LINK");
                if (string.IsNullOrWhiteSpace(link)) link = StripePaymentLink;

                bool opened = false;
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = link, UseShellExecute = true });
                    opened = true;
                }
                catch (Exception ex)
                {
                    ShowStatus($"Could not open browser: {ex.Message}");
                }

                if (!opened)
                {
                    _busy = false;
                    SubscribeButton.IsEnabled = true;
                    return;
                }

                ShowStatus("Stripe checkout opened in your browser. Complete payment there — your activation code will arrive by email.");
                SubscribeButton.Content = "Done";
                SubscribeButton.IsEnabled = true;
                _activated = true; // treat as "you can close this now"

                // If a real licensing client is wired up, kick off the poll
                // in the background so the token can auto-activate if payment
                // completes while this window is still open.
                if (_client is not null)
                {
                    try
                    {
                        var result = await _client.ActivateAsync(PremiumTier.Premium, _usbBindingId);
                        if (result.Kind == LicensingResultKind.Success)
                        {
                            ResultToken = result.Token;
                            ShowStatus("Payment accepted — Premium activated. Click Done to continue.");
                        }
                    }
                    catch { /* silent — user still gets the manual code path */ }
                }
            }
            catch (Exception ex)
            {
                try { ShowStatus($"Checkout error: {ex.Message}"); } catch { }
                _busy = false;
                try { SubscribeButton.IsEnabled = true; } catch { }
            }
        }

        private void ShowStatus(string message)
        {
            StatusText.Text = message;
            StatusText.IsVisible = true;
        }
    }
}
