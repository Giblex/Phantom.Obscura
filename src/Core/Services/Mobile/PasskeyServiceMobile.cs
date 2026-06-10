namespace PhantomVault.Core.Services
{
    // Android substitute for the desktop PasskeyService (Windows Hello /
    // WebAuthn host support is Windows-only). Passkeys are unavailable on mobile.
    public sealed class PasskeyService
    {
        public bool IsSupported => false;

        public bool IsBiometricAvailable => false;
    }
}
