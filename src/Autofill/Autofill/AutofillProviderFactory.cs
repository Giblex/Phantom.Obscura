using System;

namespace PhantomVault.Core.Services.Autofill
{
    /// <summary>
    /// Creates the appropriate <see cref="IAutofillProvider"/> for the current platform.
    /// </summary>
    public static class AutofillProviderFactory
    {
        /// <summary>
        /// Returns the platform-native autofill provider.
        /// Returns <c>null</c> if no provider is available (e.g., unsupported platform).
        /// </summary>
        public static IAutofillProvider? Create(
            ICredentialRepository credentialRepository,
            INativeMessagingHost? nativeMessagingHost = null)
        {
            if (OperatingSystem.IsWindows())
                return new WindowsAutofillService(credentialRepository, nativeMessagingHost);

            if (OperatingSystem.IsMacOS())
                return new MacOsAutofillService(credentialRepository, nativeMessagingHost);

            if (OperatingSystem.IsLinux())
                return new LinuxAutofillService(credentialRepository, nativeMessagingHost);

            // Android autofill is registered as a system service via AndroidAutofillService
            // (Android.Service.Autofill.AutofillService) — no factory construction needed.
            return null;
        }
    }
}
