using Avalonia;
using Avalonia.iOS;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using Foundation;

namespace PhantomVault.UI.iOS;

// The single UIApplicationDelegate Avalonia hooks into. Mirrors MainActivity on the
// Android head: it customizes the AppBuilder and hands control to the shared App.
[Register(nameof(AppDelegate))]
public partial class AppDelegate : AvaloniaAppDelegate<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        => base.CustomizeAppBuilder(builder)
            .With(new FontManagerOptions
            {
                // Menlo is the iOS system monospace face; keep it as the default fallback so
                // the vault's mono runs render without shipping a custom font.
                DefaultFamilyName = "Menlo"
            })
            .UseReactiveUI();
}
