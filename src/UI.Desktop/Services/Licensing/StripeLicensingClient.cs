using System;
using Serilog;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models.Licensing;
using PhantomVault.Core.Services.Network;

namespace PhantomVault.UI.Services.Licensing
{
    /// <summary>
    /// Production licensing client. Drives Stripe's hosted Checkout: it asks the
    /// licensing backend for a Checkout URL, opens it in the system browser, then
    /// polls for the signed license token the backend mints from the Stripe
    /// webhook. No card data is ever handled in-app (PCI scope stays on Stripe).
    ///
    /// All HTTP to giblex.com passes through <see cref="IInternetGateway"/> with
    /// SPKI pinning and respects <see cref="IInternetGateway.OfflineMode"/>.
    ///
    /// Backend contract (see worker/licensing.js on the Giblex site):
    ///   POST {Base}/api/checkout  { tier, usbBindingId? } -> { url, claimId }
    ///   GET  {Base}/api/license?claim=ID -> { status: pending|ready|expired, token? }
    /// </summary>
    public sealed class StripeLicensingClient : ILicensingClient
    {
        // Override at runtime with PHANTOM_LICENSING_BASEURL for staging/self-host.
        private const string DefaultBaseUrl = "https://giblex.com";

        // Fallback Stripe Payment Link — opened when the licensing backend
        // isn't reachable (e.g. the /api/checkout endpoint isn't deployed yet).
        // Set via PHANTOM_STRIPE_PAYMENT_LINK env var — no hardcoded link ships.
        private const string DefaultStripePaymentLink = "";

        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan PollTimeout = TimeSpan.FromMinutes(15);

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly IInternetGateway _gateway;
        private readonly Uri? _baseUri;

        public StripeLicensingClient(IInternetGateway gateway)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));

            var configured = Environment.GetEnvironmentVariable("PHANTOM_LICENSING_BASEURL");
            var raw = string.IsNullOrWhiteSpace(configured) ? DefaultBaseUrl : configured.Trim();
            Uri.TryCreate(raw, UriKind.Absolute, out _baseUri);
        }

        public bool IsConfigured =>
            _baseUri is not null &&
            _baseUri.Scheme == Uri.UriSchemeHttps;

        public async Task<LicensingResult> ActivateAsync(PremiumTier tier, string? usbBindingId,
            BillingInterval interval = BillingInterval.Monthly, CancellationToken ct = default)
        {
            // Check the master kill-switch before every path, including the direct
            // Stripe fallback used when the backend URL is missing or invalid.
            // Opening a browser is still outbound activity initiated by Obscura.
            if (_gateway.OfflineMode)
                return LicensingResult.Failed(
                    "Premium checkout is blocked while offline mode is on. Turn off offline mode in Privacy settings, then try again.");

            if (!IsConfigured)
                return TryDirectPaymentLinkFallback("Licensing endpoint is not configured for this build.", interval);

            var grant = await _gateway.RequestAccessAsync(LicensingGatewayPolicy.CreateRequest(), ct)
                .ConfigureAwait(false);
            if (grant is null)
                return LicensingResult.Failed("Internet access for licensing was not granted.");

            using var http = _gateway.CreateClient(grant);

            CheckoutResponse? checkout;
            try
            {
                var resp = await http.PostAsJsonAsync(
                    new Uri(_baseUri!, "/api/checkout"),
                    new CheckoutRequest
                    {
                        Tier = (int)tier,
                        UsbBindingId = usbBindingId,
                        Interval = interval == BillingInterval.Yearly ? "yearly" : "monthly",
                    },
                    ct).ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode)
                    return TryDirectPaymentLinkFallback($"Checkout API returned {(int)resp.StatusCode}.", interval);

                checkout = await resp.Content.ReadFromJsonAsync<CheckoutResponse>(JsonOpts, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return LicensingResult.Failed("Checkout was cancelled.");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Licensing] Checkout request failed.");
                return TryDirectPaymentLinkFallback("Could not reach the licensing service.", interval);
            }

            if (checkout is null || string.IsNullOrWhiteSpace(checkout.Url) || string.IsNullOrWhiteSpace(checkout.ClaimId))
                return TryDirectPaymentLinkFallback("The licensing service returned an invalid checkout session.", interval);

            if (!TryOpenBrowser(checkout.Url))
                return LicensingResult.Failed("Could not open the checkout page in your browser.");

            return await PollForTokenAsync(http, checkout.ClaimId, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// When the backend isn't reachable, open a plain Stripe Payment Link so
        /// the user still lands on Stripe's hosted checkout. The token is not
        /// auto-issued in this path — the user activates via a returned link/code
        /// after Stripe emails the receipt. Returns NotConfigured so the UI shows
        /// a friendly "checkout opened in your browser" message instead of an error.
        /// </summary>
        private LicensingResult TryDirectPaymentLinkFallback(string reason, BillingInterval interval)
        {
            var intervalVar = interval == BillingInterval.Yearly
                ? "PHANTOM_STRIPE_PAYMENT_LINK_YEARLY"
                : "PHANTOM_STRIPE_PAYMENT_LINK_MONTHLY";

            var configuredLink = Environment.GetEnvironmentVariable(intervalVar);
            if (string.IsNullOrWhiteSpace(configuredLink))
                configuredLink = Environment.GetEnvironmentVariable("PHANTOM_STRIPE_PAYMENT_LINK");

            var link = string.IsNullOrWhiteSpace(configuredLink) ? DefaultStripePaymentLink : configuredLink.Trim();

            if (string.IsNullOrWhiteSpace(link) || !Uri.TryCreate(link, UriKind.Absolute, out var linkUri))
            {
                Log.Warning("[Licensing] Checkout unavailable and no fallback link set. Reason: {Reason}", reason);
                return LicensingResult.Failed(
                    "Premium checkout is unavailable right now — the licensing service could not be reached. "
                    + "Your vault and existing features are unaffected. Please try again later.");
            }

            if (!IsStripeCheckoutUri(linkUri))
                return LicensingResult.Failed($"{reason} The configured fallback checkout URL is not a Stripe checkout link.");

            if (!TryOpenBrowser(link))
                return LicensingResult.Failed($"{reason} Could not open the fallback checkout page.");

            return LicensingResult.NotConfigured(
                "Opened Stripe checkout in your browser. Complete payment there — your activation code will arrive by email.");
        }

        private static bool IsStripeCheckoutUri(Uri uri)
        {
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
                return false;

            var host = uri.IdnHost;
            return string.Equals(host, "stripe.com", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".stripe.com", StringComparison.OrdinalIgnoreCase);
        }

        public Task<LicensingResult> RenewAsync(string? currentToken, string? usbBindingId, CancellationToken ct = default)
            => ActivateAsync(PremiumTier.Premium, usbBindingId, BillingInterval.Monthly, ct);

        private async Task<LicensingResult> PollForTokenAsync(HttpClient http, string claimId, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow + PollTimeout;
            var pollUri = new Uri(_baseUri!, "/api/license?claim=" + Uri.EscapeDataString(claimId));

            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    await Task.Delay(PollInterval, ct).ConfigureAwait(false);
                    var status = await http.GetFromJsonAsync<LicenseStatusResponse>(pollUri, JsonOpts, ct).ConfigureAwait(false);

                    if (status is null) continue;
                    if (string.Equals(status.Status, "ready", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(status.Token))
                    {
                        return LicensingResult.Success(status.Token);
                    }
                    if (string.Equals(status.Status, "expired", StringComparison.OrdinalIgnoreCase))
                    {
                        return LicensingResult.Failed("The checkout session expired before payment completed.");
                    }
                }
                catch (OperationCanceledException)
                {
                    return LicensingResult.Failed("Checkout was cancelled.");
                }
                catch (HttpRequestException)
                {
                    // Transient network blip — keep trying until the deadline.
                }
            }

            return LicensingResult.Failed("Timed out waiting for payment to complete.");
        }

        private static bool TryOpenBrowser(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StripeLicensingClient] Failed to open browser: {ex.Message}");
                return false;
            }
        }

        private sealed class CheckoutRequest
        {
            [JsonPropertyName("tier")] public int Tier { get; set; }
            [JsonPropertyName("usbBindingId")] public string? UsbBindingId { get; set; }

            /// <summary>"monthly" or "yearly" — selects which Stripe price the backend uses.</summary>
            [JsonPropertyName("interval")] public string? Interval { get; set; }
        }

        private sealed class CheckoutResponse
        {
            [JsonPropertyName("url")] public string? Url { get; set; }
            [JsonPropertyName("claimId")] public string? ClaimId { get; set; }
        }

        private sealed class LicenseStatusResponse
        {
            [JsonPropertyName("status")] public string? Status { get; set; }
            [JsonPropertyName("token")] public string? Token { get; set; }
        }
    }
}
