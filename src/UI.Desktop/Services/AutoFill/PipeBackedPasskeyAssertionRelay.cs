using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Services.Autofill;

namespace PhantomVault.UI.Services.AutoFill
{
    /// <summary>
    /// Carries an assertion request from the native-messaging host process across the pipe to
    /// the running app, which holds the vault and can reach Attestor.
    ///
    /// <para>
    /// The host process deliberately does no passkey work of its own: it has no vault, and the
    /// decision about whether a page may assert for a relying party has to be made where the
    /// credential lives.
    /// </para>
    /// </summary>
    public sealed class PipeBackedPasskeyAssertionRelay : IPasskeyAssertionRelay
    {
        private readonly PipeNativeHostClient _client;

        public PipeBackedPasskeyAssertionRelay(PipeNativeHostClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public async Task<PasskeyAssertionResult?> AssertAsync(
            string origin, string rpId, string clientDataJson, CancellationToken ct = default)
        {
            var request = JsonSerializer.Serialize(new
            {
                action = "webauthnAssert",
                origin,
                rpId,
                clientDataJson
            });

            var response = await _client.SendAsync(request, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(response))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                if (!root.TryGetProperty("success", out var ok) || !ok.GetBoolean())
                    return null;

                return new PasskeyAssertionResult(
                    root.GetProperty("credentialId").GetString() ?? string.Empty,
                    root.GetProperty("authenticatorData").GetString() ?? string.Empty,
                    root.GetProperty("signature").GetString() ?? string.Empty,
                    root.TryGetProperty("userHandle", out var uh) && uh.ValueKind == JsonValueKind.String
                        ? uh.GetString()
                        : null);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
