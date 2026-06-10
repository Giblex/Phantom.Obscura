using System.Windows.Input;

namespace PhantomVault.UI.ViewModels.Settings
{

    public interface IDirtyTrackingSettings
    {
        bool IsDirty { get; }
        ICommand SaveCommand { get; }
        void Save();
    }
}

