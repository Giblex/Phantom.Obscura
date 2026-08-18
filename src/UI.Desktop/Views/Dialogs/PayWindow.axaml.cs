using System;
using Avalonia.Controls;
using Avalonia.Media;
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

        // Resolved explicitly rather than via the compiler-generated x:Name fields.
        //
        // This class hand-rolls InitializeComponent (just AvaloniaXamlLoader.Load), which
        // does not populate those generated fields — so every one of them was null, and
        // the first click on "Continue to secure checkout" dereferenced SubscribeButton
        // and took the whole app down with an unhandled NullReferenceException.
        // MainWindow already uses this FindControl pattern for exactly the same reason.
        private Button? _subscribeButton;
        private Button? _cancelButton;
        private TextBlock? _statusText;
        private Border? _paymentPanel;
        private RadioButton? _yearlyOption;

        public string? ResultToken { get; private set; }

        public PayWindow()
        {
            InitializeComponent();

            _subscribeButton = this.FindControl<Button>("SubscribeButton");
            _cancelButton = this.FindControl<Button>("CancelButton");
            _statusText = this.FindControl<TextBlock>("StatusText");
            _paymentPanel = this.FindControl<Border>("PaymentPanel");
            _yearlyOption = this.FindControl<RadioButton>("YearlyOption");
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
            // An unhandled exception in an async void handler is rethrown on the
            // dispatcher and terminates the process — that is how a null control
            // reference here became a full app crash rather than a broken button.
            // Nothing a checkout button does is worth taking the vault down for.
            try
            {
                await SubscribeAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[PayWindow] OnSubscribe failed");
                ShowStatus("Subscription could not be started. Please try again.", StatusKind.Error);
                if (_subscribeButton is not null) _subscribeButton.IsEnabled = true;
            }
        }

        private async System.Threading.Tasks.Task SubscribeAsync()
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
                ShowStatus("Licensing is unavailable in this build.", StatusKind.Error);
                return;
            }

            if (_subscribeButton is not null) _subscribeButton.IsEnabled = false;
            ShowStatus("Opening secure checkout…");

            try
            {
                Log.Information("[PayWindow] Calling ActivateAsync via {ClientType}", _client.GetType().Name);
                // Monthly is the default; only an explicit yearly selection changes it.
                var interval = _yearlyOption?.IsChecked == true
                    ? BillingInterval.Yearly
                    : BillingInterval.Monthly;

                Log.Information("[PayWindow] Selected billing interval: {Interval}", interval);
                var result = await _client.ActivateAsync(PremiumTier.Premium, _usbBindingId, interval);
                Log.Information("[PayWindow] ActivateAsync returned Kind={Kind}", result.Kind);

                switch (result.Kind)
                {
                    case LicensingResultKind.Success:
                        ResultToken = result.Token;
                        _activated = true;
                        if (_paymentPanel is not null) _paymentPanel.IsVisible = false;
                        ShowStatus("Payment accepted — Premium activated. Click Done to continue.", StatusKind.Success);
                        if (_subscribeButton is not null)
                        {
                            _subscribeButton.Content = "Done";
                            _subscribeButton.IsEnabled = true;
                        }
                        if (_cancelButton is not null) _cancelButton.IsVisible = false;
                        return;
                    case LicensingResultKind.NotConfigured:
                        ShowStatus(result.Message ?? "Checkout opened in your browser. Complete payment there.");
                        break;
                    default:
                        ShowStatus(result.Message ?? "Subscription could not be completed.", StatusKind.Error);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[PayWindow] ActivateAsync threw");
                ShowStatus($"Subscription failed: {ex.Message}", StatusKind.Error);
            }
            finally
            {
                if (!_activated && _subscribeButton is not null)
                    _subscribeButton.IsEnabled = true;
            }
        }

        private enum StatusKind { Info, Success, Error }

        private void ShowStatus(string message, StatusKind kind = StatusKind.Info)
        {
            if (_statusText is null)
            {
                // Surface it somewhere rather than losing it silently.
                Log.Warning("[PayWindow] status text control unavailable: {Message}", message);
                return;
            }

            // Semantic colour, resolved from the theme so it tracks the active skin.
            var key = kind switch
            {
                StatusKind.Success => "SuccessBrush",
                StatusKind.Error => "ErrorBrush",
                _ => "PrimaryTextBrush"
            };

            if (this.TryFindResource(key, out var brush) && brush is IBrush b)
            {
                _statusText.Foreground = b;
            }

            _statusText.Text = message;
            _statusText.IsVisible = true;
        }
    }
}
