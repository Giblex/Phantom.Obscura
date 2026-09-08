using System.Threading;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services.Autofill
{
    /// <summary>
    /// Relays a WebAuthn assertion request from the browser to whichever component holds the
    /// passkeys. Separate from <c>ICredentialRepository</c> because it is not a lookup: it
    /// asks a hardware-backed authenticator to sign, which prompts the user.
    /// </summary>
    public interface IPasskeyAssertionRelay
    {
        /// <summary>
        /// Signs <paramref name="clientDataJson"/> with the passkey registered for
        /// <paramref name="rpId"/>, or returns null when no eligible passkey exists, the page
        /// is not entitled to that relying party, or the user declined.
        /// </summary>
        /// <param name="origin">
        /// The requesting page's origin, established by the browser rather than read from
        /// page content — it is what binds the assertion to a site.
        /// </param>
        /// <param name="rpId">Relying party the page is asking to authenticate against.</param>
        /// <param name="clientDataJson">
        /// The browser's clientDataJSON verbatim. Hashed by the implementation rather than
        /// accepted pre-hashed, so the signature covers what the site will actually verify.
        /// </param>
        /// <param name="ct">Cancels the wait on the user's verification prompt.</param>
        Task<PasskeyAssertionResult?> AssertAsync(
            string origin, string rpId, string clientDataJson, CancellationToken ct = default);
    }

    /// <summary>
    /// A completed assertion, base64url-encoded and ready to be handed back to the page as a
    /// <c>PublicKeyCredential</c> response.
    /// </summary>
    public sealed record PasskeyAssertionResult(
        string CredentialId,
        string AuthenticatorData,
        string Signature,
        string? UserHandle);
}
