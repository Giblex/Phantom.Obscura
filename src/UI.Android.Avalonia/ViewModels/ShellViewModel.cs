using System.Collections.Generic;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhantomVault.UI.Views;

namespace PhantomVault.UI.ViewModels;

/// <summary>
/// Tiny in-process navigator. Maintains a stack of <see cref="UserControl"/>
/// instances and exposes the current one plus a Back command. Desktop
/// equivalent is the multi-Window flow orchestrated by App.axaml.cs; on
/// Android we fold that into a stack because <c>SingleViewApplicationLifetime</c>
/// only hosts one Control.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly Stack<(string Title, UserControl View)> _stack = new();

    [ObservableProperty] private UserControl? _currentView;
    [ObservableProperty] private string _title = "Phantom Obscura";
    [ObservableProperty] private bool _canGoBack;

    /// <summary>Process-wide singleton so any view's code-behind can request navigation.</summary>
    public static ShellViewModel? Current { get; private set; }

    public ShellViewModel()
    {
        Current = this;
        // Initial route: WelcomePage.
        Navigate("Phantom Obscura", new WelcomePage
        {
            DataContext = new WelcomePageViewModel()
        });
    }

    // ── Named navigation helpers ─────────────────────────────────────────────

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

    /// <summary>Push a new view onto the stack and show it.</summary>
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
