using AvApplication = Avalonia.Application;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace PhantomVault.UI;

/// <summary>
/// Android-flavoured Avalonia <c>Application</c>. Mirrors the desktop App's
/// resource dictionaries / styles but uses <c>SingleViewApplicationLifetime</c>
/// because Android renders one fullscreen view (no MainWindow / Closing semantics).
/// We alias <c>Avalonia.Application</c> as <c>AvApplication</c> to disambiguate
/// from <c>Android.App.Application</c> which is in the implicit using set.
/// </summary>
public partial class App : AvApplication
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            // Phase 3: AppShell wraps a navigation stack so the desktop's
            // multi-window flow (Welcome → VaultUnlock → Vault → AddEdit → …)
            // can be expressed as nested UserControls under Android's
            // SingleViewApplicationLifetime.
            single.MainView = new Views.AppShell
            {
                DataContext = new ViewModels.ShellViewModel()
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
