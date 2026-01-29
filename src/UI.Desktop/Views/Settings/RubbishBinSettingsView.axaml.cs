using System;
using System.IO;
using Avalonia.Controls;
using PhantomVault.UI;
using PhantomVault.UI.Services;
using PhantomVault.UI.ViewModels.Settings;

namespace PhantomVault.UI.Views.Settings
{
    public partial class RubbishBinSettingsView : UserControl
    {
        public RubbishBinSettingsView()
        {
            InitializeComponent();
            var app = Avalonia.Application.Current as App;
            var services = app?.Services;
            var dialogService = services?.GetService(typeof(DialogService)) as DialogService;
            var shadowTrash = services?.GetService(typeof(ShadowTrashService)) as ShadowTrashService;
            var secureTrash = services?.GetService(typeof(SecureTrashService)) as SecureTrashService;

            // File-based trash location (if used by the app); provide real paths for shadow snapshots.
            string trashFilesRoot = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "PhantomVault", "SecureTrashFiles");
            string[] TrashFilesProvider() =>
                System.IO.Directory.Exists(trashFilesRoot)
                    ? System.IO.Directory.GetFiles(trashFilesRoot, "*", System.IO.SearchOption.AllDirectories)
                    : System.Array.Empty<string>();

            DataContext = new RubbishBinSettingsViewModel(dialogService, shadowTrash, secureTrash, null, TrashFilesProvider);
        }
    }
}
