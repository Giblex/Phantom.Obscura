using AvApplication = Avalonia.Application;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace PhantomVault.UI;

public partial class App : AvApplication
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {

            single.MainView = new Views.AppShell
            {
                DataContext = new ViewModels.ShellViewModel()
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}

