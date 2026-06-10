using System;
using System.Threading.Tasks;
using PhantomVault.Core.Models.AutoInject;

namespace PhantomVault.Core.Services.Platform.Android
{

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

