using System.Windows.Input;

namespace PhantomVault.UI.ViewModels.Settings
{
    /// <summary>
    /// Marker interface for settings ViewModels that track unsaved changes
    /// and apply changes only on Save.
    /// </summary>
    public interface IDirtyTrackingSettings
    {
        bool IsDirty { get; }
        ICommand SaveCommand { get; }
        void Save();
    }
}
