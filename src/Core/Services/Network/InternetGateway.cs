using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services.Network
{
    /// <summary>
    /// Default <see cref="IInternetGateway"/> implementation. Sealed, single-instance.
    /// Every outbound HTTP call funnels through here.
    /// </summary>
    public sealed class InternetGateway : IInternetGateway, IDisposable
    {
        private readonly IInternetConsentProvider _consent;
        private readonly InternetAccessAuditLog _audit;
        private readonly ConcurrentDictionary<Guid, InternetAccessGrant> _grants = new();
        private readonly Timer _sweepTimer;
        private readonly object _eventLock = new();
        private bool _disposed;
        private bool _offlineMode;

        public InternetGateway(IInternetConsentProvider consent, InternetAccessAuditLog audit)
        {
            _consent = consent ?? throw new ArgumentNullException(nameof(consent));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _sweepTimer = new Timer(SweepExpired, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
        }

        public bool HasActiveGrant => !_offlineMode && _grants.Values.Any(g => g.IsLive);

        public bool OfflineMode
        {
            get => _offlineMode;
            set
            {
                if (_offlineMode == value) return;
                _offlineMode = value;
                if (value)
                {
                    _audit.Append(new InternetAccessAuditEntry(
                        DateTimeOffset.UtcNow, InternetAccessAuditKind.Denied,
                        FeatureId: "OfflineMode", UserVisibleReason: "Offline mode enabled",
                        Host: null, Detail: "Master kill-switch engaged; revoking all grants."));
                    RevokeAll("Offline mode enabled");
                }
                else
                {
                    _audit.Append(new InternetAccessAuditEntry(
                        DateTimeOffset.UtcNow, InternetAccessAuditKind.Granted,
                        FeatureId: "OfflineMode", UserVisibleReason: "Offline mode disabled",
                        Host: null, Detail: "Master kill-switch released; grants must be re-requested."));
                }
            }
        }

        public IReadOnlyList<InternetAccessGrantInfo> ActiveGrants =>
            new ReadOnlyCollection<InternetAccessGrantInfo>(
                _grants.Values.Where(g => g.IsLive).Select(g => g.ToInfo()).ToList());

        public event EventHandler<InternetAccessGrantChangedEventArgs>? GrantChanged;

        public async Task<InternetAccessGrant?> RequestAccessAsync(
            InternetAccessRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));
            request.Validate();

            if (_offlineMode)
            {
                _audit.Append(new InternetAccessAuditEntry(
                    DateTimeOffset.UtcNow, InternetAccessAuditKind.Denied,
                    request.FeatureId, request.UserVisibleReason,
                    Host: null, Detail: "Offline mode is enabled; no consent prompt shown."));
                return null;
            }

            _audit.Append(new InternetAccessAuditEntry(
                DateTimeOffset.UtcNow, InternetAccessAuditKind.Requested,
                request.FeatureId, request.UserVisibleReason,
                Host: string.Join(",", request.AllowedHosts), Detail: null));

            InternetConsentDecision decision;
            try
            {
                decision = await _consent.RequestConsentAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _audit.Append(new InternetAccessAuditEntry(
                    DateTimeOffset.UtcNow, InternetAccessAuditKind.Denied,
                    request.FeatureId, request.UserVisibleReason,
                    Host: null, Detail: "Consent prompt cancelled."));
                return null;
            }

            if (decision is null || decision.Choice == InternetConsentChoice.Deny)
            {
                _audit.Append(new InternetAccessAuditEntry(
                    DateTimeOffset.UtcNow, InternetAccessAuditKind.Denied,
                    request.FeatureId, request.UserVisibleReason,
                    Host: null, Detail: decision?.UserNote));
                return null;
            }

            var ttl = decision.Choice == InternetConsentChoice.GrantForSession && request.AllowSessionGrant
                ? ClampTtl(TimeSpan.FromMinutes(60))
                : ClampTtl(request.Ttl);

            var grant = new InternetAccessGrant(
                request.FeatureId,
                request.UserVisibleReason,
                request.AllowedHosts,
                request.SpkiPinsByHost,
                ttl);

            _grants[grant.Id] = grant;

            _audit.Append(new InternetAccessAuditEntry(
                DateTimeOffset.UtcNow, InternetAccessAuditKind.Granted,
                request.FeatureId, request.UserVisibleReason,
                Host: string.Join(",", request.AllowedHosts),
                Detail: $"ttl={ttl.TotalSeconds:0}s"));

            RaiseChanged(grant.ToInfo(), InternetAccessGrantChangeKind.Granted, decision.UserNote);
            return grant;
        }

        public HttpClient CreateClient(InternetAccessGrant grant)
        {
            if (grant is null) throw new ArgumentNullException(nameof(grant));
            if (!_grants.ContainsKey(grant.Id))
                throw new InvalidOperationException("Grant was not issued by this gateway.");
            if (!grant.IsLive)
                throw new InvalidOperationException("Grant is expired or revoked.");

            var sockets = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                UseProxy = false,
                AutomaticDecompression = DecompressionMethods.None,
                ConnectTimeout = TimeSpan.FromSeconds(15),
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                SslOptions = new SslClientAuthenticationOptions
                {
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    RemoteCertificateValidationCallback = (sender, cert, chain, errors)
                        => ValidatePin(grant, sender, cert, errors),
                },
            };

            var enforcing = new GrantEnforcingHandler(this, grant, _audit) { InnerHandler = sockets };

            return new HttpClient(enforcing, disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
        }

        public void Revoke(InternetAccessGrant grant, string reason)
        {
            if (grant is null) return;
            if (!_grants.TryGetValue(grant.Id, out var existing)) return;
            if (existing.Revoked) return;

            existing.MarkRevoked();
            _audit.Append(new InternetAccessAuditEntry(
                DateTimeOffset.UtcNow, InternetAccessAuditKind.Revoked,
                existing.FeatureId, existing.UserVisibleReason,
                Host: null, Detail: reason));
            RaiseChanged(existing.ToInfo(), InternetAccessGrantChangeKind.Revoked, reason);
        }

        public void RevokeAll(string reason)
        {
            foreach (var grant in _grants.Values.ToList())
                Revoke(grant, reason);
        }

        internal void NotifyExpired(InternetAccessGrant grant)
        {
            if (grant is null) return;
            _audit.Append(new InternetAccessAuditEntry(
                DateTimeOffset.UtcNow, InternetAccessAuditKind.Expired,
                grant.FeatureId, grant.UserVisibleReason,
                Host: null, Detail: null));
            RaiseChanged(grant.ToInfo(), InternetAccessGrantChangeKind.Expired, null);
        }

        private void SweepExpired(object? _)
        {
            foreach (var kv in _grants)
            {
                var g = kv.Value;
                if (!g.Revoked && !g.IsLive)
                {
                    if (_grants.TryRemove(kv.Key, out InternetAccessGrant? _))
                        NotifyExpired(g);
                }
            }
        }

        private static TimeSpan ClampTtl(TimeSpan ttl)
        {
            if (ttl < TimeSpan.FromMinutes(1)) return TimeSpan.FromMinutes(1);
            if (ttl > TimeSpan.FromMinutes(60)) return TimeSpan.FromMinutes(60);
            return ttl;
        }

        private bool ValidatePin(InternetAccessGrant grant, object? sender, X509Certificate? cert, SslPolicyErrors errors)
        {
            if (errors != SslPolicyErrors.None)
            {
                LogPinFailure(grant, host: TryGetHost(sender), detail: $"SSL errors: {errors}");
                return false;
            }
            if (cert is not X509Certificate2 cert2)
            {
                LogPinFailure(grant, host: TryGetHost(sender), detail: "No X509Certificate2 presented.");
                return false;
            }

            var host = TryGetHost(sender);
            if (string.IsNullOrEmpty(host) ||
                !grant.SpkiPinsByHost.TryGetValue(host, out var pins) ||
                pins is null || pins.Count == 0)
            {
                LogPinFailure(grant, host, detail: "No pin set for host.");
                return false;
            }

            foreach (var pin in pins)
            {
                if (SpkiPin.Matches(cert2, pin)) return true;
            }

            LogPinFailure(grant, host, detail: "Server SPKI does not match any pin.");
            return false;
        }

        private void LogPinFailure(InternetAccessGrant grant, string? host, string detail)
        {
            _audit.Append(new InternetAccessAuditEntry(
                DateTimeOffset.UtcNow, InternetAccessAuditKind.PinFailure,
                grant.FeatureId, grant.UserVisibleReason, host, detail));
        }

        private static string? TryGetHost(object? sender)
        {
            return sender switch
            {
                SslStream ssl => ssl.TargetHostName,
                _ => null,
            };
        }

        private void RaiseChanged(InternetAccessGrantInfo info, InternetAccessGrantChangeKind change, string? reason)
        {
            EventHandler<InternetAccessGrantChangedEventArgs>? handler;
            lock (_eventLock) handler = GrantChanged;
            try
            {
                handler?.Invoke(this, new InternetAccessGrantChangedEventArgs
                {
                    Grant = info,
                    Change = change,
                    Reason = reason,
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InternetGateway] GrantChanged subscriber threw: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _sweepTimer.Dispose();
            RevokeAll("Gateway disposed.");
        }

        /// <summary>
        /// Per-grant message handler. Blocks requests once the grant is expired or
        /// revoked, and rejects requests whose host is not in the grant's allowlist.
        /// SPKI pinning is enforced one layer down by the SslOptions callback.
        /// </summary>
        private sealed class GrantEnforcingHandler : DelegatingHandler
        {
            private readonly InternetGateway _owner;
            private readonly InternetAccessGrant _grant;
            private readonly InternetAccessAuditLog _audit;

            public GrantEnforcingHandler(InternetGateway owner, InternetAccessGrant grant, InternetAccessAuditLog audit)
            {
                _owner = owner;
                _grant = grant;
                _audit = audit;
            }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                if (!_grant.IsLive)
                {
                    _audit.Append(new InternetAccessAuditEntry(
                        DateTimeOffset.UtcNow, InternetAccessAuditKind.Blocked,
                        _grant.FeatureId, _grant.UserVisibleReason,
                        Host: request.RequestUri?.Host, Detail: "Grant not live."));
                    throw new InvalidOperationException("Internet access grant is expired or revoked.");
                }

                var uri = request.RequestUri
                    ?? throw new InvalidOperationException("Request URI is required.");

                if (uri.Scheme != Uri.UriSchemeHttps)
                {
                    _audit.Append(new InternetAccessAuditEntry(
                        DateTimeOffset.UtcNow, InternetAccessAuditKind.Blocked,
                        _grant.FeatureId, _grant.UserVisibleReason,
                        Host: uri.Host, Detail: $"Non-HTTPS scheme rejected: {uri.Scheme}"));
                    throw new InvalidOperationException("Only HTTPS is permitted through the internet gateway.");
                }

                if (!_grant.AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
                {
                    _audit.Append(new InternetAccessAuditEntry(
                        DateTimeOffset.UtcNow, InternetAccessAuditKind.Blocked,
                        _grant.FeatureId, _grant.UserVisibleReason,
                        Host: uri.Host, Detail: "Host not in grant allowlist."));
                    throw new InvalidOperationException(
                        $"Host '{uri.Host}' is not authorised by this internet-access grant.");
                }

                _grant.RecordRequest();
                _audit.Append(new InternetAccessAuditEntry(
                    DateTimeOffset.UtcNow, InternetAccessAuditKind.Used,
                    _grant.FeatureId, _grant.UserVisibleReason,
                    Host: uri.Host, Detail: $"{request.Method} {uri.AbsolutePath}"));

                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
