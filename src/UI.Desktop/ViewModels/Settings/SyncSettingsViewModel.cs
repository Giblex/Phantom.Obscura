using PhantomVault.UI.Services;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels.Settings;

public class SyncSettingsViewModel : ReactiveObject
{
    private bool _syncEnabled;
    private bool _syncTheme;

    public SyncSettingsViewModel()
    {
        var settings = SettingsService.Load();
        _syncEnabled = settings.SyncEnabled;
        _syncTheme = settings.SyncTheme;
    }

    public bool SyncEnabled
    {
        get => _syncEnabled;
        set
        {
            if (this.RaiseAndSetIfChanged(ref _syncEnabled, value) != value) return;
            var s = SettingsService.Load();
            s.SyncEnabled = value;
            SettingsService.Save(s);
        }
    }

    public bool SyncTheme
    {
        get => _syncTheme;
        set
        {
            if (this.RaiseAndSetIfChanged(ref _syncTheme, value) != value) return;
            var s = SettingsService.Load();
            s.SyncTheme = value;
            SettingsService.Save(s);
        }
    }
}
