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
        // Initial route: WelcomePage, bound to the Phase 3a stub VM.
        Navigate("Phantom Obscura", new WelcomePage
        {
            DataContext = new WelcomePageViewModel()
        });
    }

    // ── Named navigation helpers (Phase 3 placeholders for each P0 surface) ──

    public void NavigateUnlock() => Navigate("Unlocking Vault", new VaultUnlockView
    {
        DataContext = new VaultUnlockViewModel()
    });

    public void NavigateDashboard() => Navigate("Vault Dashboard", PlaceholderView.Create(
        title:        "Dashboard",
        sourceView:   "Views/DashboardView.axaml (1287 LOC)",
        description:  "Phantom Obscura's main hub — sidebar with categories, credential grid, quick actions, and account summary.",
        portStatus:   "Verbatim port queued for Phase 3d. The desktop AXAML references custom controls (PhantomVault.UI.Desktop.Controls) which need linking before this view can render."));

    public void NavigateVault() => Navigate("Vault", PlaceholderView.Create(
        title:        "Credential Vault",
        sourceView:   "Views/VaultWindow.axaml (2032 LOC)",
        description:  "The full credential editor / list view with category sidebar, type-specific detail panels, and quick-copy.",
        portStatus:   "Largest desktop view — split into CredentialListView + Details/* sub-views during port (Phase 3d/3e)."));

    public void NavigateAddEdit() => Navigate("Add Credential", PlaceholderView.Create(
        title:        "Add / Edit Credential",
        sourceView:   "Views/AddEditCredentialWindow.axaml (845 LOC)",
        description:  "Type-aware credential editor: password, card, identity, API key, Wi-Fi, contact, bank, PIN, etc. — each with a tailored form.",
        portStatus:   "Phase 3e: each EditForm/*.axaml under Views/EditForms/ ports as its own DataTemplate."));

    // Real ported views (Phase 3f/3g landed this turn) — replace earlier placeholders.
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

    public void NavigateImportExport() => Navigate("Import / Export", PlaceholderView.Create(
        title:        "Import / Export",
        sourceView:   "Views/ImportExportDialog.axaml",
        description:  "Import credentials from KeePass / Bitwarden / CSV; export the vault to encrypted backup.",
        portStatus:   "Phase 3g. File picker needs to use the StorageProvider API instead of OpenFileDialog."));

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
