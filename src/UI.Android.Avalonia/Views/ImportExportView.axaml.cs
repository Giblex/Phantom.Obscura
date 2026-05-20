using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views;

public partial class ImportExportView : UserControl
{
    public ImportExportView() => InitializeComponent();
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Mobile pickers must originate from the TopLevel's StorageProvider —
        // hand it to the VM as soon as the view is materialized.
        if (DataContext is ImportExportViewModel vm)
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is not null)
            {
                vm.ConfigureStorageProvider(top.StorageProvider);
            }
        }
    }
}
