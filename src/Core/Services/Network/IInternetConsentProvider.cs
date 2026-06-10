using System.Threading;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services.Network
{
    /// <summary>
    /// Abstracts the user-facing consent prompt for an internet-access grant.
    /// UI layer registers an implementation that pops a modal dialog. Headless
    /// scenarios (tests, native-messaging host) register a deny-all default.
    /// </summary>
    public interface IInternetConsentProvider
    {
        Task<InternetConsentDecision> RequestConsentAsync(
            InternetAccessRequest request,
            CancellationToken cancellationToken = default);
    }

    public enum InternetConsentChoice
    {
        Deny,

        /// <summary>Grant exactly this request, for the requested TTL.</summary>
        GrantOnce,

        /// <summary>Grant this and re-grant silently for the rest of the unlock session for the same FeatureId + host set.</summary>
        GrantForSession,
    }

    public sealed record InternetConsentDecision(InternetConsentChoice Choice, string? UserNote = null);

    /// <summary>
    /// Default consent provider that denies everything. Used in non-UI contexts
    /// (native messaging host, tests). The desktop App.axaml.cs replaces this
    /// with a Window-backed implementation.
    /// </summary>
    public sealed class DenyAllConsentProvider : IInternetConsentProvider
    {
        public Task<InternetConsentDecision> RequestConsentAsync(
            InternetAccessRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new InternetConsentDecision(InternetConsentChoice.Deny,
                "No UI consent provider registered — denying by default."));
    }
}
