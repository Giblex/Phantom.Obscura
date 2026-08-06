using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models.Licensing;

namespace PhantomVault.UI.Services.Licensing
{
    /// <summary>
    /// Production licensing client. Drives Stripe's hosted Checkout: it asks the
    /// licensing backend for a Checkout URL, opens it in the system browser, then
    /// polls for the signed license token the backend mints from the Stripe
    /// webhook. No card data is ever handled in-app (PCI scope stays on Stripe).
    ///
    /// Backend contract (see worker/licensing.js on the Giblex site):
    ///   POST {Base}/api/checkout  { tier, usbBindingId? } -> { url, claimId }
    ///   GET  {Base}/api/license?claim=ID -> { status: pending|ready|expired, token? }
    /// </summary>
    public sealed class StripeLicensingClient : ILicensingClient
    {
        // Override at runtime with PHANTOM_LICENSING_BASEURL for staging/self-host.
        private const string DefaultBaseUrl = "https://giblex.com.au";

        // Fallback Stripe Payment Link — opened when the licensing backend
        // isn't reachable (e.g. the /api/checkout endpoint isn't deployed yet).
        // Override with PHANTOM_STRIPE_PAYMENT_LINK for a different product.
        private const string DefaultStripePaymentLink =
            "https://buy.stripe.com/aFa14n2VD3zdanIfe2eZ200"; // Phantom Obscura Premium (test link — replace when live)

        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan PollTimeout = TimeSpan.FromMinutes(15);

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _http;
        private readonly Uri? _baseUri;

        public StripeLicensingClient(HttpClient? http = null)
        {
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

            var configured = Environment.GetEnvironmentVariable("PHANTOM_LICENSING_BASEURL");
            var raw = string.IsNullOrWhiteSpace(configured) ? DefaultBaseUrl : configured.Trim();
            Uri.TryCreate(raw, UriKind.Absolute, out _baseUri);
        }

        public bool IsConfigured =>
            _baseUri is not null &&
            (_baseUri.Scheme == Uri.UriSchemeHttp || _baseUri.Scheme == Uri.UriSchemeHttps);

        public async Task<LicensingResult> ActivateAsync(PremiumTier tier, string? usbBindingId, CancellationToken ct = default)
        {
            if (!IsConfigured)
                return TryDirectPaymentLinkFallback("Licensing endpoint is not configured for this build.");

            CheckoutResponse? checkout;
            try
            {
                var resp = await _http.PostAsJsonAsync(
                    new Uri(_baseUri!, "/api/checkout"),
                    new CheckoutRequest { Tier = (int)tier, UsbBindingId = usbBindingId },
                    ct).ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode)
                    return TryDirectPaymentLinkFallback($"Checkout API returned {(int)resp.StatusCode}.");

                checkout = await resp.Content.ReadFromJsonAsync<CheckoutResponse>(JsonOpts, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return LicensingResult.Failed("Checkout was cancelled.");
            }
            catch (Exception ex)
            {
                return TryDirectPaymentLinkFallback($"Could not reach the licensing service: {ex.Message}");
            }

            if (checkout is null || string.IsNullOrWhiteSpace(checkout.Url) || string.IsNullOrWhiteSpace(checkout.ClaimId))
                return TryDirectPaymentLinkFallback("The licensing service returned an invalid checkout session.");

            if (!TryOpenBrowser(checkout.Url))
                return LicensingResult.Failed("Could not open the checkout page in your browser.");

            return await PollForTokenAsync(checkout.ClaimId, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// When the backend isn't reachable, open a plain Stripe Payment Link so
        /// the user still lands on Stripe's hosted checkout. The token is not
        /// auto-issued in this path — the user activates via a returned link/code
        /// after Stripe emails the receipt. Returns NotConfigured so the UI shows
        /// a friendly "checkout opened in your browser" message instead of an error.
        /// </summary>
        private LicensingResult TryDirectPaymentLinkFallback(string reason)
        {
            var configuredLink = Environment.GetEnvironmentVariable("PHANTOM_STRIPE_PAYMENT_LINK");
            var link = string.IsNullOrWhiteSpace(configuredLink) ? DefaultStripePaymentLink : configuredLink.Trim();

            if (string.IsNullOrWhiteSpace(link) || !Uri.TryCreate(link, UriKind.Absolute, out _))
                return LicensingResult.Failed($"{reason} No fallback checkout URL is configured.");

            if (!TryOpenBrowser(link))
                return LicensingResult.Failed($"{reason} Could not open the fallback checkout page.");

            return LicensingResult.NotConfigured(
                "Opened Stripe checkout in your browser. Complete payment there — your activation code will arrive by email.");
        }

        public Task<LicensingResult> RenewAsync(string? currentToken, string? usbBindingId, CancellationToken ct = default)
            => ActivateAsync(PremiumTier.Premium, usbBindingId, ct);

        private async Task<LicensingResult> PollForTokenAsync(string claimId, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow + PollTimeout;
            var pollUri = new Uri(_baseUri!, "/api/license?claim=" + Uri.EscapeDataString(claimId));

            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    await Task.Delay(PollInterval, ct).ConfigureAwait(false);
                    var status = await _http.GetFromJsonAsync<LicenseStatusResponse>(pollUri, JsonOpts, ct).ConfigureAwait(false);

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
                    // "pending" — keep polling.
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
