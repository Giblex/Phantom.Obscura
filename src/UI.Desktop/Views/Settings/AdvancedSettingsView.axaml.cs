using Avalonia.Controls;
using PhantomVault.UI.ViewModels.Settings;

namespace PhantomVault.UI.Views.Settings
{
    public partial class AdvancedSettingsView : UserControl
    {
        public AdvancedSettingsView()
        {
            InitializeComponent();
            DataContext = new AdvancedSettingsViewModel();
        }

        protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);

            // A fresh AdvancedSettingsViewModel is created every time this tab opens,
            // and its Privacy/Update sub-VMs subscribe to gateway/update-service
            // events. Dispose them on detach so the handlers don't accumulate.
            if (DataContext is AdvancedSettingsViewModel vm)
            {
                vm.Privacy.Dispose();
                vm.Update.Dispose();
            }
        }
    }
}

