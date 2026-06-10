using System;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services.Security
{

    public interface IAuthController
    {

        bool IsLockedOut { get; }

        DateTimeOffset? LockoutEndUtc { get; }

        Task AddAuthenticationDelayAsync(TimeSpan delay);

        Task EnforceTempLockoutAsync(TimeSpan lockoutDuration);

        void RequirePhantomKeyForNextUnlock();

        bool CheckAndResetPhantomKeyRequirement();

        void RequireReauthentication(string reason);

        event EventHandler<string>? ReauthenticationRequired;
    }
}

