using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Controls authentication flow and enforces security measures
    /// (lockouts, key requirements, forced re-auth) on behalf of the DefenceEngine.
    /// </summary>
    public sealed class AuthController : IAuthController
    {
        private readonly ILogger<AuthController>? _logger;
        private readonly object _lock = new();
        private bool _requirePhantomKeyNextUnlock;
        private DateTimeOffset? _lockoutEndUtc;

        public AuthController(ILogger<AuthController>? logger = null)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        public bool IsLockedOut
        {
            get
            {
                lock (_lock)
                {
                    return _lockoutEndUtc.HasValue && DateTimeOffset.UtcNow < _lockoutEndUtc.Value;
                }
            }
        }

        /// <inheritdoc />
        public DateTimeOffset? LockoutEndUtc
        {
            get
            {
                lock (_lock)
                {
                    return _lockoutEndUtc;
                }
            }
        }

        /// <inheritdoc />
        public event EventHandler<string>? ReauthenticationRequired;

        public async Task AddAuthenticationDelayAsync(TimeSpan delay)
        {
            _logger?.LogInformation("Adding authentication delay: {Delay}", delay);
            await Task.Delay(delay);
        }

        public Task EnforceTempLockoutAsync(TimeSpan lockoutDuration)
        {
            lock (_lock)
            {
                _lockoutEndUtc = DateTimeOffset.UtcNow.Add(lockoutDuration);
            }
            _logger?.LogWarning("Temporary lockout enforced until {LockoutEnd}", _lockoutEndUtc);
            return Task.CompletedTask;
        }

        public void RequirePhantomKeyForNextUnlock()
        {
            _logger?.LogWarning("PhantomKey will be required for next unlock");
            _requirePhantomKeyNextUnlock = true;
        }

        /// <inheritdoc />
        public bool CheckAndResetPhantomKeyRequirement()
        {
            var wasRequired = _requirePhantomKeyNextUnlock;
            _requirePhantomKeyNextUnlock = false;
            return wasRequired;
        }

        public void RequireReauthentication(string reason)
        {
            _logger?.LogWarning("Re-authentication required: {Reason}", reason);
            ReauthenticationRequired?.Invoke(this, reason);
        }
    }
}
