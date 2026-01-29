namespace PhantomVault.UI.Mobile;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        // Set main page to VaultUnlockPage
        MainPage = new AppShell();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = base.CreateWindow(activationState);

        // Set window title
        window.Title = "PhantomVault";

        return window;
    }
}
