using PhantomVault.UI.Services;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels.Settings;

public class SyncSettingsViewModel : ReactiveObject
{
    private bool _syncEnabled;
    private bool _syncTheme;
    private bool _syncTotp;
    private bool _syncPasskeys;
    private bool _syncPhantomKey;
    private readonly bool _baselineSyncEnabled;
    private readonly bool _baselineSyncTheme;
    private readonly bool _baselineSyncTotp;
    private readonly bool _baselineSyncPasskeys;
    private readonly bool _baselineSyncPhantomKey;
    private readonly SettingsDraftTracker _draft;

    public SyncSettingsViewModel() : this(null) { }

    public SyncSettingsViewModel(SettingsDraftTracker? draftTracker)
    {

        _draft = draftTracker
            ?? ((Avalonia.Application.Current as App)?.Services?.GetService(typeof(SettingsDraftTracker)) as SettingsDraftTracker)
            ?? new SettingsDraftTracker();

        var settings = SettingsService.Load();
        _syncEnabled = settings.SyncEnabled;
        _syncTheme = settings.SyncTheme;
        _syncTotp = settings.SyncTotp;
        _syncPasskeys = settings.SyncPasskeys;
        _syncPhantomKey = settings.SyncPhantomKey;
        _baselineSyncEnabled = _syncEnabled;
        _baselineSyncTheme = _syncTheme;
        _baselineSyncTotp = _syncTotp;
        _baselineSyncPasskeys = _syncPasskeys;
        _baselineSyncPhantomKey = _syncPhantomKey;
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

    public bool SyncTotp
    {
        get => _syncTotp;
        set
        {
            if (!this.RaiseAndSetIfChanged(ref _syncTotp, value).Equals(value)) return;
            StageBool("Sync.Totp", value, _baselineSyncTotp,
                commitValue => { var s = SettingsService.Load(); s.SyncTotp = commitValue; SettingsService.Save(s); },
                () => { _syncTotp = _baselineSyncTotp; this.RaisePropertyChanged(nameof(SyncTotp)); });
        }
    }

    public bool SyncPasskeys
    {
        get => _syncPasskeys;
        set
        {
            if (!this.RaiseAndSetIfChanged(ref _syncPasskeys, value).Equals(value)) return;
            StageBool("Sync.Passkeys", value, _baselineSyncPasskeys,
                commitValue => { var s = SettingsService.Load(); s.SyncPasskeys = commitValue; SettingsService.Save(s); },
                () => { _syncPasskeys = _baselineSyncPasskeys; this.RaisePropertyChanged(nameof(SyncPasskeys)); });
        }
    }

    public bool SyncPhantomKey
    {
        get => _syncPhantomKey;
        set
        {
            if (!this.RaiseAndSetIfChanged(ref _syncPhantomKey, value).Equals(value)) return;
            StageBool("Sync.PhantomKey", value, _baselineSyncPhantomKey,
                commitValue => { var s = SettingsService.Load(); s.SyncPhantomKey = commitValue; SettingsService.Save(s); },
                () => { _syncPhantomKey = _baselineSyncPhantomKey; this.RaisePropertyChanged(nameof(SyncPhantomKey)); });
        }
    }

    private void StageBool(string key, bool value, bool baseline, Action<bool> commit, Action discard)
    {
        if (value == baseline)
        {
            _draft.ClearKey(key);
            return;
        }
        _draft.Stage(key, commit: () => commit(value), discard: discard);
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

