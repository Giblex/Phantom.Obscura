using System;

namespace PhantomVault.Core.Models.AutoInject
{
    /// <summary>
    /// Which part of a credential an AutoFill action should act on.
    ///
    /// The AutoFill entry point used to be all-or-nothing — it always typed username
    /// and password together. That made the common cases impossible: filling only the
    /// username on a two-step login, filling only the password when the browser already
    /// remembered the account, or entering a TOTP code on the second screen.
    /// </summary>
    public enum AutoFillField
    {
        /// <summary>Username, Tab, password — the standard single-form login.</summary>
        Both = 0,

        /// <summary>Username only. Two-step logins ask for it on its own first.</summary>
        UsernameOnly = 1,

        /// <summary>Password only. The site or browser already has the account.</summary>
        PasswordOnly = 2,

        /// <summary>The current time-based one-time code, for a 2FA prompt.</summary>
        TotpCode = 3
    }

    /// <summary>
    /// A point-in-time TOTP reading: the code plus how long it stays valid.
    ///
    /// Carries the remaining lifetime so the UI can show a countdown rather than a
    /// bare code the user cannot tell is about to expire.
    /// </summary>
    public sealed class TotpSnapshot
    {
        public string Code { get; init; } = string.Empty;

        /// <summary>Seconds until this code rolls over.</summary>
        public int SecondsRemaining { get; init; }

        /// <summary>Length of the full step, so the UI can render progress as a fraction.</summary>
        public int StepSeconds { get; init; } = 30;

        public double Fraction => StepSeconds <= 0
            ? 0
            : Math.Clamp(SecondsRemaining / (double)StepSeconds, 0, 1);
    }
}
