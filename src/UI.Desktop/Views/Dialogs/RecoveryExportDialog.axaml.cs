using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using PhantomVault.UI.ViewModels;
using PhantomVault.UI.Views;

namespace PhantomVault.UI.Views.Dialogs
{
    /// <summary>
    /// Mandatory post-provisioning step: requires at least one concrete recovery action
    /// (save file, save PDF, or copy codes) before setup can continue.
    /// </summary>
    public partial class RecoveryExportDialog : ThemeAwareWindow
    {
        private SetupWizardViewModel? _wizard;
        private bool _completed;

        public RecoveryExportDialog() => InitializeComponent();

        public static async Task ShowAsync(Window owner, SetupWizardViewModel wizard)
        {
            if (wizard == null || !wizard.HasStagedRecovery)
                return;

            var dialog = new RecoveryExportDialog { _wizard = wizard };
            dialog.Wire();
            await dialog.ShowDialog(owner);
        }

        private void Wire()
        {
            var codesList = this.FindControl<ItemsControl>("CodesList")!;
            var saveButton = this.FindControl<Button>("SaveFileButton")!;
            var savePdfButton = this.FindControl<Button>("SavePdfButton")!;
            var copyButton = this.FindControl<Button>("CopyCodesButton")!;
            var statusText = this.FindControl<TextBlock>("StatusText")!;

            codesList.ItemsSource = _wizard!.StagedRecoveryCodes.ToArray();

            void CompleteAndClose()
            {
                _completed = true;
                _wizard.ClearStagedRecovery();
                Close();
            }

            saveButton.Click += async (_, __) =>
            {
                var top = GetTopLevel(this);
                if (top?.StorageProvider is not { } storage)
                    return;

                var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save recovery file (keep it OFF this USB)",
                    SuggestedFileName = _wizard.SuggestedRecoveryFileName,
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("Phantom Recovery File") { Patterns = new[] { "*.precovery" } }
                    }
                });

                if (file == null)
                    return;

                var path = file.Path.LocalPath;

                if (!_wizard.IsOffUsbDestination(path))
                {
                    statusText.IsVisible = true;
                    statusText.Text = "That location is on the bound USB. Choose a different drive so the recovery file survives losing the USB.";
                    return;
                }

                if (await _wizard.SaveStagedRecoveryFileAsync(path))
                {
                    saveButton.Content = "Saved ✓";
                    CompleteAndClose();
                }
                else
                {
                    statusText.IsVisible = true;
                    statusText.Text = "Failed to save the recovery file. Please try a different location.";
                }
            };

            copyButton.Click += async (_, __) =>
            {
                var top = GetTopLevel(this);
                if (top?.Clipboard is not { } clipboard)
                    return;

                await clipboard.SetTextAsync(string.Join(Environment.NewLine, _wizard.StagedRecoveryCodes));
                copyButton.Content = "Copied ✓";
                CompleteAndClose();
            };

            savePdfButton.Click += async (_, __) =>
            {
                var top = GetTopLevel(this);
                if (top?.StorageProvider is not { } storage)
                    return;

                var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save recovery codes as a printable PDF",
                    SuggestedFileName = $"{_wizard.EffectiveVaultName} Recovery Kit.pdf",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("PDF Document") { Patterns = new[] { "*.pdf" } }
                    }
                });

                if (file == null)
                    return;

                var path = file.Path.LocalPath;

                if (!_wizard.IsOffUsbDestination(path))
                {
                    statusText.IsVisible = true;
                    statusText.Text = "That location is on the bound USB. Choose a different drive so the printed kit survives losing the USB.";
                    return;
                }

                try
                {
                    PhantomVault.UI.Services.RecoveryPdfWriter.Write(
                        path, _wizard.EffectiveVaultName, _wizard.StagedRecoveryCodes);
                    savePdfButton.Content = "PDF saved ✓";
                    CompleteAndClose();
                }
                catch (Exception ex)
                {
                    statusText.IsVisible = true;
                    statusText.Text = "Failed to write the PDF. Try a different location.";
                    Serilog.Log.Warning(ex, "Recovery PDF export failed");
                }
            };

            Closing += (_, e) =>
            {
                if (!_completed)
                    e.Cancel = true;
            };
        }
    }
}
