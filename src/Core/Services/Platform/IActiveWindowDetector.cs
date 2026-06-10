using System.Threading.Tasks;
using PhantomVault.Core.Models.AutoInject;

namespace PhantomVault.Core.Services.Platform
{

    public interface IActiveWindowDetector
    {

        AutoInjectContext GetCurrentContext();

        bool IsActiveBrowser();

        string? TryGetBrowserUrl();

        NativeLoginContext? DetectNativeLoginFields();

        Task<bool> TryFillNativeLoginAsync(NativeLoginContext context, string username, string password);
    }
}

