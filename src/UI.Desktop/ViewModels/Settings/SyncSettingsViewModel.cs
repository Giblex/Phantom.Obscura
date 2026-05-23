using PhantomVault.UI.Services;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels.Settings;

public class SyncSettingsViewModel : ReactiveObject
{
    private bool _syncEnabled;
    private bool _syncTheme;
    private readonly bool _baselineSyncEnabled;
    private readonly bool _baselineSyncTheme;
    private readonly SettingsDraftTracker _draft;

    public SyncSettingsViewModel() : this(null) { }

    public SyncSettingsViewModel(SettingsDraftTracker? draftTracker)
    {
        // Issue #25/Sync: resolve shared tracker so the overlay-bottom Save
        // button drives Sync settings too. Fallback for ctor calls outside
        // the DI graph keeps the parameterless ctor working.
        _draft = draftTracker
            ?? ((Avalonia.Application.Current as App)?.Services?.GetService(typeof(SettingsDraftTracker)) as SettingsDraftTracker)
            ?? new SettingsDraftTracker();

        var settings = SettingsService.Load();
        _syncEnabled = settings.SyncEnabled;
        _syncTheme = settings.SyncTheme;
        _baselineSyncEnabled = _syncEnabled;
        _baselineSyncTheme = _syncTheme;
    }

    public bool SyncEnabled
    {
        get => _syncEnabled;
        set
        {
            if (!this.RaiseAndSetIfChanged(ref _syncEnabled, value).Equals(value)) return;
            StageSyncEnabled(value);
        }
    }

    public bool SyncTheme
    {
        get => _syncTheme;
        set
        {
            if (!this.RaiseAndSetIfChanged(ref _syncTheme, value).Equals(value)) return;
            StageSyncTheme(value);
        }
    }

    private void StageSyncEnabled(bool value)
    {
        if (value == _baselineSyncEnabled)
        {
            _draft.ClearKey("Sync.Enabled");
            return;
        }
        _draft.Stage(
            key: "Sync.Enabled",
            commit: () =>
            {
                var s = SettingsService.Load();
                s.SyncEnabled = value;
                SettingsService.Save(s);
            },
            discard: () =>
            {
                _syncEnabled = _baselineSyncEnabled;
                this.RaisePropertyChanged(nameof(SyncEnabled));
            });
    }

    private void StageSyncTheme(bool value)
    {
        if (value == _baselineSyncTheme)
        {
            _draft.ClearKey("Sync.Theme");
            return;
        }
        _draft.Stage(
            key: "Sync.Theme",
            commit: () =>
            {
                var s = SettingsService.Load();
                s.SyncTheme = value;
                SettingsService.Save(s);
            },
            discard: () =>
            {
                _syncTheme = _baselineSyncTheme;
                this.RaisePropertyChanged(nameof(SyncTheme));
            });
    }
}
