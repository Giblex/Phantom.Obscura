using System;

namespace PhantomVault.Core.Services.Autofill
{

    public static class AutofillProviderFactory
    {

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

            return null;
        }
    }
}

