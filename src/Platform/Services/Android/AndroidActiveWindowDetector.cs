using System;
using System.Threading.Tasks;
using PhantomVault.Core.Models.AutoInject;
using PhantomVault.Platform.Services;

namespace PhantomVault.Core.Services.Platform.Android
{
    /// <summary>
    /// Android implementation of <see cref="IActiveWindowDetector"/>.
    /// Win32-style foreground-window inspection is not applicable on Android,
    /// so this implementation deliberately returns an empty
    /// <see cref="AutoInjectContext"/> and reports no browser / no native login.
    /// The Android autofill pipeline relies on the system AutofillService
    /// for window context instead of polling.
    /// </summary>
    public sealed class AndroidActiveWindowDetector : IActiveWindowDetector
    {
        public AutoInjectContext GetCurrentContext() => new AutoInjectContext();

        public bool IsActiveBrowser() => false;

        public string? TryGetBrowserUrl() => null;

        public NativeLoginContext? DetectNativeLoginFields() => null;

        public Task<bool> TryFillNativeLoginAsync(NativeLoginContext context, string username, string password)
            => Task.FromResult(false);
    }
}
