using System.Collections.Generic;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhantomVault.UI.Views;

namespace PhantomVault.UI.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    private readonly Stack<(string Title, UserControl View)> _stack = new();

    [ObservableProperty] private UserControl? _currentView;
    [ObservableProperty] private string _title = "Phantom Obscura";
    [ObservableProperty] private bool _canGoBack;

    public static ShellViewModel? Current { get; private set; }

    public ShellViewModel()
    {
        Current = this;

        Navigate("Phantom Obscura", new WelcomePage
        {
            DataContext = new WelcomePageViewModel()
        });
    }

    public void NavigateUnlock() => Navigate("Unlocking Vault", new VaultUnlockView
    {
        DataContext = new VaultUnlockViewModel()
    });

    public void NavigateDashboard() => Navigate("Vault Dashboard", new DashboardView
    {
        DataContext = new DashboardViewModel()
    });

    public void NavigateVault() => Navigate("Credentials", new CredentialListView
    {
        DataContext = new CredentialListViewModel()
    });

    public void NavigateAddEdit() => Navigate("New credential", new AddEditCredentialView
    {
        DataContext = new AddEditCredentialViewModel()
    });

    public void NavigateCategories() => Navigate("Categories", new CategoryLandingView
    {
        DataContext = new CategoryLandingViewModel()
    });

    public void NavigateIconDownloader() => Navigate("Icon Downloader", new IconDownloaderView
    {
        DataContext = new IconDownloaderViewModel()
    });

    public void NavigateSettings() => Navigate("Settings", new SettingsView
    {
        DataContext = new SettingsViewModel()
    });

    public void NavigateSecurityDashboard() => Navigate("Security Dashboard", new SecurityDashboardView
    {
        DataContext = new SecurityDashboardViewModel()
    });

    public void NavigateThemeSettings() => Navigate("Theme", new ThemeSettingsView
    {
        DataContext = new ThemeSettingsViewModel()
    });

    public void NavigateImportExport() => Navigate("Import / Export", new ImportExportView
    {
        DataContext = new ImportExportViewModel()
    });

    public void Navigate(string title, UserControl view)
    {
        _stack.Push((title, view));
        Title = title;
        CurrentView = view;
        CanGoBack = _stack.Count > 1;
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        if (_stack.Count <= 1) return;
        _stack.Pop();
        var (title, view) = _stack.Peek();
        Title = title;
        CurrentView = view;
        CanGoBack = _stack.Count > 1;
    }
}

