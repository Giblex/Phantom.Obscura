using System.Windows.Input;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels.Settings
{
    public class GeneralSettingsViewModel : ReactiveObject, IDirtyTrackingSettings
    {
        private int _selectedDefaultView;
        private bool _isDashboardEnabled = true;
        private bool _isDirty;

        public int SelectedDefaultView
        {
            get => _selectedDefaultView;
            set { this.RaiseAndSetIfChanged(ref _selectedDefaultView, value); IsDirty = true; }
        }

        public bool IsDashboardEnabled
        {
            get => _isDashboardEnabled;
            set { this.RaiseAndSetIfChanged(ref _isDashboardEnabled, value); IsDirty = true; }
        }

        public bool IsDirty
        {
            get => _isDirty;
            set => this.RaiseAndSetIfChanged(ref _isDirty, value);
        }

        public ICommand SaveCommand { get; }

        public GeneralSettingsViewModel()
        {
            SaveCommand = ReactiveCommand.Create(Save);
        }

        public void Save()
        {
            // Persist general settings (extend as wiring grows)
            IsDirty = false;
        }
    }
}
