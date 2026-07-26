using UIKit;

namespace PhantomVault.UI.iOS;

// Managed entry point for the iOS app. UIApplicationMain spins up UIKit and resolves the
// principal delegate (AppDelegate), which in turn boots Avalonia via AvaloniaAppDelegate<App>.
public static class Application
{
    public static void Main(string[] args)
    {
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
