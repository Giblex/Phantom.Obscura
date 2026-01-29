using System;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Controls authentication flow and can enforce security measures during unlock.
    /// </summary>
    public interface IAuthController
    {
        /// <summary>
        /// Adds an artificial delay to the authentication process.
        /// Used to slow down brute-force attacks.
        /// </summary>
        /// <param name="delay">Duration of delay to add.</param>
        Task AddAuthenticationDelayAsync(TimeSpan delay);

        /// <summary>
        /// Locks the vault for a temporary period, preventing any unlock attempts.
        /// </summary>
        /// <param name="lockoutDuration">How long to lock out authentication.</param>
        Task EnforceTempLockoutAsync(TimeSpan lockoutDuration);

        /// <summary>
        /// Requires physical PhantomKey/Keyfile for the next unlock attempt.
        /// Disables password-only authentication until reset.
        /// </summary>
        void RequirePhantomKeyForNextUnlock();

        /// <summary>
        /// Forces re-authentication with a reason displayed to the user.
        /// Used when security threats are detected.
        /// </summary>
        /// <param name="reason">The reason for requiring re-authentication.</param>
        void RequireReauthentication(string reason);
    }
}
