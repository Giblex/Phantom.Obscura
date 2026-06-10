using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services.Network
{
    /// <summary>
    /// Gateway for ALL outbound HTTP in Phantom Obscura. Off by default.
    ///
    /// Design contract:
    ///   • No HTTP is allowed without an active <see cref="InternetAccessGrant"/>.
    ///   • A grant is obtained via <see cref="RequestAccessAsync"/>, which always
    ///     consults an <see cref="IInternetConsentProvider"/> (UI prompt).
    ///   • A grant is time-scoped (TTL) and host-allowlisted.
    ///   • SPKI pinning is enforced at the TLS layer for every request.
    ///   • Every grant / request / revoke is audit-logged with privacy redaction.
    ///   • Grants can be revoked by the user or by audit triggers at any time.
    ///
    /// Goal: outbound traffic is a *deliberate, observable, revocable* action,
    /// not an ambient default.
    /// </summary>
    public interface IInternetGateway
    {
        /// <summary>True iff at least one active (non-expired, non-revoked) grant exists.</summary>
        bool HasActiveGrant { get; }

        /// <summary>
        /// Master kill-switch. When true, all currently-active grants are revoked
        /// and any further <see cref="RequestAccessAsync"/> calls deny immediately
        /// without surfacing a consent prompt. Bound to the user's privacy-mode
        /// toggle (P-1: single "go offline" switch).
        /// </summary>
        bool OfflineMode { get; set; }

        /// <summary>Snapshot of currently-active grants. For UI display only.</summary>
        IReadOnlyList<InternetAccessGrantInfo> ActiveGrants { get; }

        /// <summary>Raised whenever a grant is created, expired, or revoked.</summary>
        event EventHandler<InternetAccessGrantChangedEventArgs>? GrantChanged;

        /// <summary>
        /// Request an internet-access grant. Always prompts the user via the consent
        /// provider. Returns <c>null</c> if the user denies or the request is invalid.
        /// </summary>
        /// <param name="request">The access request describing why, where, and for how long.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task<InternetAccessGrant?> RequestAccessAsync(
            InternetAccessRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Get an <see cref="HttpClient"/> bound to an active grant. The client will:
        ///   • Block any request whose host is not in the grant's allowlist.
        ///   • Reject any TLS connection whose server SPKI does not match a pin.
        ///   • Fail-fast once the grant expires or is revoked.
        /// </summary>
        /// <param name="grant">An active grant from <see cref="RequestAccessAsync"/>.</param>
        HttpClient CreateClient(InternetAccessGrant grant);

        /// <summary>
        /// Revoke a grant immediately. Idempotent. Logged.
        /// </summary>
        void Revoke(InternetAccessGrant grant, string reason);

        /// <summary>Revoke EVERY active grant. Used by panic-lock, USB-removal, etc.</summary>
        void RevokeAll(string reason);
    }

    /// <summary>Read-only view of a grant for UI listing.</summary>
    public sealed record InternetAccessGrantInfo(
        Guid Id,
        string Reason,
        IReadOnlyList<string> AllowedHosts,
        DateTimeOffset GrantedAtUtc,
        DateTimeOffset ExpiresAtUtc,
        bool Revoked,
        int RequestCount);

    public enum InternetAccessGrantChangeKind
    {
        Granted,
        Expired,
        Revoked,
    }

    public sealed class InternetAccessGrantChangedEventArgs : EventArgs
    {
        public required InternetAccessGrantInfo Grant { get; init; }
        public required InternetAccessGrantChangeKind Change { get; init; }
        public string? Reason { get; init; }
    }
}
