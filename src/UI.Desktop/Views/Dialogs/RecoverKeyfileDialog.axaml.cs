using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.DomainKeys;
using Serilog;

namespace PhantomVault.UI.Views.Dialogs
{
    /// <summary>
    /// Pre-vault, standalone recovery screen (no unlock required). The user selects the off-USB
    /// recovery file exported at setup and enters one recovery code; the keyfile material is rebuilt
    /// and written back onto an inserted USB drive (the only mandatory credential for this USB-bound
    /// vault). Because the original USB also held the encrypted vault containers, the recovered keyfile
    /// must be paired with the user's encrypted-vault backup to restore the DATA — this is made
    /// explicit in the UI and reiterated on success.
    /// </summary>
    public partial class RecoverKeyfileDialog : Window
    {
        private const string DefaultKeyfileName = "vault.key";

        private readonly KeyfileRecoveryBundleService? _bundleService;
        private readonly IUsbDetector? _usbDetector;

        private byte[]? _recoveryFileBytes;

        public RecoverKeyfileDialog()
        {
            InitializeComponent();

            var services = (Avalonia.Application.Current as App)?.Services;
            _bundleService = services?.GetService<KeyfileRecoveryBundleService>();
            _usbDetector = services?.GetService<IUsbDetector>();

            Wire();
        }

        private void Wire()
        {
            var fileBox = this.FindControl<TextBox>("FileBox")!;
            var browseButton = this.FindControl<Button>("BrowseButton")!;
            var codeBox = this.FindControl<TextBox>("CodeBox")!;
            var recoverButton = this.FindControl<Button>("RecoverButton")!;
            var statusText = this.FindControl<TextBlock>("StatusText")!;
            var closeButton = this.FindControl<Button>("CloseButton")!;

            void RefreshRecoverEnabled()
                => recoverButton.IsEnabled = _recoveryFileBytes is { Length: > 0 }
                    && !string.IsNullOrWhiteSpace(codeBox.Text)
                    && HasUsb();

            RefreshUsbGate();

            if (_usbDetector != null)
            {
                _usbDetector.RemovableDriveInserted += _ => Dispatch(() => { RefreshUsbGate(); RefreshRecoverEnabled(); });
                _usbDetector.RemovableDriveRemoved += _ => Dispatch(() => { RefreshUsbGate(); RefreshRecoverEnabled(); });
            }

            browseButton.Click += async (_, __) =>
            {
                var top = GetTopLevel(this);
                if (top?.StorageProvider is not { } storage)
                    return;

                var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select your recovery file",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Phantom Recovery File") { Patterns = new[] { "*.precovery" } },
                        new FilePickerFileType("All files") { Patterns = new[] { "*" } }
                    }
                });

                var picked = files.FirstOrDefault();
                if (picked == null)
                    return;

                try
                {
                    var path = picked.Path.LocalPath;
                    var bytes = await File.ReadAllBytesAsync(path);

                    if (_bundleService == null || !_bundleService.IsRecoveryFile(bytes))
                    {
                        statusText.Text = "That file is not a valid Phantom recovery file.";
                        _recoveryFileBytes = null;
                        fileBox.Text = string.Empty;
                    }
                    else
                    {
                        ZeroAndClear(ref _recoveryFileBytes);
                        _recoveryFileBytes = bytes;
                        fileBox.Text = path;
                        statusText.Text = string.Empty;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to read recovery file");
                    statusText.Text = "Could not read that file. Please try again.";
                }

                RefreshRecoverEnabled();
            };

            codeBox.TextChanged += (_, __) => RefreshRecoverEnabled();

            recoverButton.Click += async (_, __) =>
            {
                if (_recoveryFileBytes is not { Length: > 0 } || _bundleService == null)
                    return;

                if (!HasUsb())
                {
                    RefreshUsbGate();
                    return;
                }

                recoverButton.IsEnabled = false;
                statusText.Text = "Recovering keyfile…";

                byte[]? keyfileBytes = null;
                try
                {
                    var code = (codeBox.Text ?? string.Empty).Trim();
                    keyfileBytes = await Task.Run(() => _bundleService.RestoreKeyfile(_recoveryFileBytes, code));

                    var top = GetTopLevel(this);
                    if (top?.StorageProvider is not { } storage)
                    {
                        statusText.Text = "Unable to access storage to write the keyfile.";
                        return;
                    }

                    var dest = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
                    {
                        Title = "Write the recovered keyfile onto your USB drive",
                        SuggestedFileName = DefaultKeyfileName,
                        DefaultExtension = "key",
                        FileTypeChoices = new[]
                        {
                            new FilePickerFileType("Keyfile") { Patterns = new[] { "*.key" } }
                        }
                    });

                    if (dest == null)
                    {
                        statusText.Text = "Keyfile not written. Choose a USB destination to finish recovery.";
                        return;
                    }

                    var destPath = dest.Path.LocalPath;

                    if (!IsOnRemovableDrive(destPath))
                    {
                        statusText.Text = "That location is not a USB drive. This vault is USB-bound — write the keyfile onto a removable drive.";
                        return;
                    }

                    await File.WriteAllBytesAsync(destPath, keyfileBytes);

                    statusText.Text =
                        $"Keyfile restored to: {destPath}\n\n" +
                        "Next: this restores your CREDENTIAL only. To restore your DATA, pair this keyfile with your " +
                        "encrypted vault backup (restore the backup onto the same drive), then open the vault from the " +
                        "Welcome screen.";

                    recoverButton.Content = "Recovered ✓";
                }
                catch (UnauthorizedAccessException)
                {
                    statusText.Text = "That recovery code did not match this file. Check the code and try again.";
                    recoverButton.IsEnabled = true;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Keyfile recovery failed");
                    statusText.Text = "Recovery failed. Verify the recovery file and code, then try again.";
                    recoverButton.IsEnabled = true;
                }
                finally
                {
                    if (keyfileBytes != null)
                        CryptographicOperations.ZeroMemory(keyfileBytes);
                }
            };

            closeButton.Click += (_, __) => Close();

            Closing += (_, __) => ZeroAndClear(ref _recoveryFileBytes);
        }

        private bool HasUsb()
            => _usbDetector?.GetRemovableDrives().Any() ?? false;

        private bool IsOnRemovableDrive(string path)
        {
            if (_usbDetector == null || string.IsNullOrWhiteSpace(path))
                return false;

            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
                return false;

            return _usbDetector.IsValidRemovableDrive(root)
                || _usbDetector.GetRemovableDrives().Any(d =>
                    path.StartsWith(d, StringComparison.OrdinalIgnoreCase));
        }

        private void RefreshUsbGate()
        {
            var warning = this.FindControl<Border>("UsbWarning");
            if (warning != null)
                warning.IsVisible = !HasUsb();
        }

        private void Dispatch(Action action)
            => Avalonia.Threading.Dispatcher.UIThread.Post(action);

        private static void ZeroAndClear(ref byte[]? buffer)
        {
            if (buffer != null)
            {
                CryptographicOperations.ZeroMemory(buffer);
                buffer = null;
            }
        }
    }
}
