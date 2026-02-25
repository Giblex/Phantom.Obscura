using System;
using System.IO;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.ZeroKnowledge;
using Avalonia.Controls;
using System.Security.Principal;
using PhantomVault.Core.Services.Security;
using PhantomVault.Core.Models;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;

// Note: This ViewModel is constructed by the XAML designer in some files.
// To keep previews working we provide a parameterless constructor that
// creates lightweight service instances when running in design mode. In
// normal app code the parameterized constructor should be used.

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the sign-in dialog that prompts for vault passphrase and optional keyfile
    /// to unlock an existing encrypted vault container.
    /// </summary>
    public class SignInDialogViewModel : ReactiveObject
    {
        private readonly ManifestService? _manifestService;
        private readonly VaultService? _vaultService;
        private readonly IZkVaultService? _zkVaultService;
        private Window? _ownerWindow;

        public SignInDialogViewModel()
        {
            // Designer-friendly: services are not available in design mode.
            _manifestService = null;
            _vaultService = null;
            _zkVaultService = null;

            SignInCommand = ReactiveCommand.CreateFromTask(SignInAsync);
            CancelCommand = ReactiveCommand.Create(Cancel);
        }

        public SignInDialogViewModel(ManifestService manifestService, VaultService vaultService, IZkVaultService zkVaultService)
        {
            _manifestService = manifestService ?? throw new ArgumentNullException(nameof(manifestService));
            _vaultService = vaultService ?? throw new ArgumentNullException(nameof(vaultService));
            _zkVaultService = zkVaultService ?? throw new ArgumentNullException(nameof(zkVaultService));

            SignInCommand = ReactiveCommand.CreateFromTask(SignInAsync);
            CancelCommand = ReactiveCommand.Create(Cancel);
        }

        public void SetOwnerWindow(Window window) => _ownerWindow = window;

        private string _manifestPath = string.Empty;
        public string ManifestPath { get => _manifestPath; set => this.RaiseAndSetIfChanged(ref _manifestPath, value); }

        private string _keyfilePath = string.Empty;
        public string KeyfilePath { get => _keyfilePath; set => this.RaiseAndSetIfChanged(ref _keyfilePath, value); }

        private string _passphrase = string.Empty;
        public string Passphrase { get => _passphrase; set => this.RaiseAndSetIfChanged(ref _passphrase, value); }

        public ReactiveCommand<Unit, Unit> SignInCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }


        private async Task SignInAsync()
        {
            // Validate inputs
            if (string.IsNullOrEmpty(ManifestPath) || !File.Exists(ManifestPath))
            {
                await ShowDialogMessage("Manifest Missing", "Please select a valid manifest file (vault.manifest) on the USB drive.");
                return;
            }

            if (string.IsNullOrEmpty(KeyfilePath) || !File.Exists(KeyfilePath))
            {
                await ShowDialogMessage("Keyfile Missing", "Please select the keyfile embedded on the USB device. Unlock requires the keyfile.");
                return;
            }

            try
            {
                if (_manifestService == null || _vaultService == null || _zkVaultService == null)
                {
                    await ShowDialogMessage("Unavailable", "Services are not available in design mode.");
                    return;
                }

                // Attempt to read the manifest using provided credentials (non-throwing)
                if (!_manifestService.TryReadManifest(ManifestPath, string.IsNullOrEmpty(Passphrase) ? null : Passphrase, KeyfilePath, out var manifest, out var manifestError))
                {
                    await ShowDialogMessage("Manifest Error", manifestError ?? "Failed to read manifest. Please check the file and try again.");
                    return;
                }

                // Optional: check key rotation status
                var services = (Avalonia.Application.Current as App)?.Services;
                var rekeyService = services?.GetService(typeof(RekeyService)) as RekeyService;
                if (rekeyService != null && manifest != null && rekeyService.IsRotationRequired(manifest))
                {
                    var rotate = await ShowRotationPrompt(manifest);
                    if (rotate)
                    {
                        // Use legacy sync hook to avoid breaking existing flows
                        bool rekeyOk = rekeyService.RekeyVault(
                            ManifestPath,
                            Passphrase ?? string.Empty,
                            Passphrase ?? string.Empty,
                            KeyfilePath,
                            KeyfilePath);

                        if (!rekeyOk)
                        {
                            await ShowDialogMessage("Rekey Failed", "Key rotation failed. Please try again or contact support.");
                            return;
                        }
                    }
                }

                // Use container path from manifest to mount the vault
                string containerPath = manifest!.ContainerPath;
                if (string.IsNullOrEmpty(containerPath) || !File.Exists(containerPath))
                {
                    await ShowDialogMessage("Container Missing", "The vault container referenced by the manifest was not found. Verify the USB drive contents.");
                    return;
                }

                // Step 1: Mount the vault container
                string mountName = manifest.VaultName ?? Path.GetFileNameWithoutExtension(containerPath);
                string mountPath = await _vaultService.MountVaultAsync(containerPath, mountName, Passphrase ?? string.Empty, KeyfilePath);

                // Step 2: Unlock the zero-knowledge vault service (for inner encryption layer)
                // This derives the master key using Argon2id + DPAPI pepper + device binding
                bool unlocked = await _zkVaultService.UnlockMasterKeyAsync(
                    Passphrase ?? string.Empty,
                    KeyfilePath,
                    manifest.DeviceId
                );

                if (!unlocked)
                {
                    // Unlock failed - dismount the container and show error
                    await _vaultService.DismountVaultAsync(mountName);
                    await ShowDialogMessage("Unlock Failed", "Failed to unlock the encrypted vault. Please check your credentials.");
                    return;
                }

                // Success: Both layers unlocked
                // The vault.pvault file inside the mounted container can now be decrypted using ZkVaultService
                await ShowDialogMessage("Unlocked", $"Vault '{mountName}' successfully unlocked.\n\nContainer mounted at: {mountPath}\nZero-knowledge encryption layer unlocked.");

                // Close the owner window if present
                _ownerWindow?.Close();

                // NOTE: leave ManifestPath/KeyfilePath/Passphrase set so callers (e.g., VaultViewModel) can read and pass them to other windows.
            }
            catch (Exception ex)
            {
                // Ensure ZK service is locked on error
                if (_zkVaultService?.IsUnlocked == true)
                {
                    await _zkVaultService.LockAndWipeKeysAsync();
                }
                await ShowDialogMessage("Unlock Failed", ex.Message);
            }
        }


        private void Cancel()
        {
            _ownerWindow?.Close();
        }

        private async Task ShowDialogMessage(string title, string message)
        {
            // Use a simple message box (DialogService exists elsewhere; to avoid circular deps we'll use Avalonia Window here)
            if (_ownerWindow != null)
            {
                var dialog = new Window
                {
                    Title = title,
                    Width = 420,
                    Height = 160,
                    Content = new Avalonia.Controls.TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }
                };
                await dialog.ShowDialog(_ownerWindow);
            }
        }

        private async Task<bool> ShowRotationPrompt(VaultManifest manifest)
        {
            if (_ownerWindow == null) return false;

            var dialog = new Window
            {
                Title = "Key Rotation Recommended",
                Width = 480,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Thickness(16),
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"It has been {(int)(DateTimeOffset.UtcNow - manifest.LastKeyRotation).TotalDays} days since the last key rotation.\nRotate now to keep the vault protected?",
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap
                        },
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            Spacing = 8,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            Children =
                            {
                                new Button { Content = "Rotate Now", IsDefault = true, Name = "RotateBtn" },
                                new Button { Content = "Later", IsCancel = true, Name = "LaterBtn" }
                            }
                        }
                    }
                }
            };

            bool rotate = false;
            if (dialog.Content is StackPanel sp && sp.Children.Count == 2 && sp.Children[1] is StackPanel btns)
            {
                if (btns.Children[0] is Button rotateBtn)
                {
                    rotateBtn.Click += (_, __) => { rotate = true; dialog.Close(); };
                }
                if (btns.Children[1] is Button laterBtn)
                {
                    laterBtn.Click += (_, __) => { rotate = false; dialog.Close(); };
                }
            }

            await dialog.ShowDialog(_ownerWindow);
            return rotate;
        }
    }
}
