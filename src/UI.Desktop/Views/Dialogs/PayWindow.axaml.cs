using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using PhantomVault.Core.Models.Licensing;
using PhantomVault.UI.Services.Licensing;
using Serilog;
using PhantomVault.UI.Views;

namespace PhantomVault.UI.Views.Dialogs
{
    /// <summary>
    /// Stripe-hosted checkout window. Payment is handled entirely by Stripe in the
    /// system browser — no card data is entered or stored here.
    /// On success, <see cref="ResultToken"/> holds the signed token to persist.
    /// </summary>
    public partial class PayWindow : ThemeAwareWindow
    {
        private readonly ILicensingClient? _client;
        private readonly string? _usbBindingId;
        private bool _activated;

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

        private async void OnSubscribe(object? sender, RoutedEventArgs e)
        {
            Log.Information("[PayWindow] OnSubscribe clicked (activated={Activated})", _activated);

            // Once activated the primary button becomes "Done" — close on second click.
            if (_activated)
            {
                Close();
                return;
            }

            if (_client is null)
            {
                ShowStatus("Licensing is unavailable in this build.");
                return;
            }

            SubscribeButton.IsEnabled = false;
            ShowStatus("Opening secure checkout…");

            try
            {
                Log.Information("[PayWindow] Calling ActivateAsync via {ClientType}", _client.GetType().Name);
                var result = await _client.ActivateAsync(PremiumTier.Premium, _usbBindingId);
                Log.Information("[PayWindow] ActivateAsync returned Kind={Kind}", result.Kind);

                switch (result.Kind)
                {
                    case LicensingResultKind.Success:
                        ResultToken = result.Token;
                        _activated = true;
                        PaymentPanel.IsVisible = false;
                        ShowStatus("Payment accepted — Premium activated. Click Done to continue.");
                        SubscribeButton.Content = "Done";
                        SubscribeButton.IsEnabled = true;
                        CancelButton.IsVisible = false;
                        return;
                    case LicensingResultKind.NotConfigured:
                        ShowStatus(result.Message ?? "Checkout opened in your browser. Complete payment there.");
                        break;
                    default:
                        ShowStatus(result.Message ?? "Subscription could not be completed.");
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[PayWindow] ActivateAsync threw");
                ShowStatus($"Subscription failed: {ex.Message}");
            }
            finally
            {
                if (!_activated)
                    SubscribeButton.IsEnabled = true;
            }
        }

        private void ShowStatus(string message)
        {
            StatusText.Text = message;
            StatusText.IsVisible = true;
        }
    }
}
