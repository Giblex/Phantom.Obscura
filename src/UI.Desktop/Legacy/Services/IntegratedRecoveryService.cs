using System;
using System.IO;
using System.Threading.Tasks;
using PhantomVault.Core.Services;
using PhantomVault.Core.Models;
using PhantomRecovery.Core.Recovery.Services;

// Alias to resolve ambiguity between PhantomVault and PhantomRecovery USB binding services
using VaultUsbBinding = PhantomVault.Core.Services.UsbBindingService;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Enhanced Recovery Service that integrates PhantomRecovery with PhantomVault's USB binding system
    /// </summary>
    public class IntegratedRecoveryService
    {
        private readonly RecoveryVaultService _recoveryService;
        private readonly ManifestService? _manifestService;
        private readonly VaultUsbBinding? _usbBindingService;
        private readonly EncryptionService? _encryptionService;

        public IntegratedRecoveryService(
            ManifestService? manifestService = null,
            VaultUsbBinding? usbBindingService = null,
            EncryptionService? encryptionService = null)
        {
            _recoveryService = new RecoveryVaultService();
            _manifestService = manifestService;
            _usbBindingService = usbBindingService;
            _encryptionService = encryptionService;
        }

        /// <summary>
        /// Creates a recovery vault with USB binding and manifest injection
        /// </summary>
        public async Task<bool> CreateRecoveryVaultWithBindingAsync(
            string vaultPath,
            string usbRootPath,
            string masterSecret,
            string recoveryPin,
            string? keyfilePath = null)
        {
            try
            {
                // Create the recovery vault directory structure
                if (!Directory.Exists(vaultPath))
                {
                    Directory.CreateDirectory(vaultPath);
                }

                // Initialize USB binding if USB path is provided
                if (_usbBindingService != null && !string.IsNullOrEmpty(usbRootPath))
                {
                    var bindingInfo = new UsbBindingInfo
                    {
                        UsbRootPath = usbRootPath,
                        BindingType = "PhantomRecoveryVault",
                        CreatedAt = DateTime.UtcNow
                    };

                    // Store USB binding information
                    var bindingPath = Path.Combine(vaultPath, ".usb_binding");
                    var bindingJson = System.Text.Json.JsonSerializer.Serialize(bindingInfo);
                    await File.WriteAllTextAsync(bindingPath, bindingJson);
                }

                // Create manifest for recovery vault
                if (_manifestService != null && !string.IsNullOrEmpty(usbRootPath))
                {
                    var manifestPath = Path.Combine(usbRootPath, ".phantom_manifest");
                    var manifest = new VaultManifest
                    {
                        Version = 3,
                        VaultName = "PhantomRecovery Vault",
                        ContainerPath = Path.Combine(usbRootPath, "recovery_vault.pvc"),
                        UseVeraCrypt = false,
                        CreatedUtc = DateTimeOffset.UtcNow,
                        Description = "PhantomRecovery integrated vault with USB binding"
                    };

                    try
                    {
                        _manifestService.WriteManifest(manifest, manifestPath, masterSecret, keyfilePath);
                    }
                    catch (Exception ex)
                    {
                        // Log but don't fail if manifest creation fails
                        System.Diagnostics.Debug.WriteLine($"Failed to create manifest: {ex.Message}");
                    }
                }

                // Initialize recovery vault marker
                var markerPath = Path.Combine(vaultPath, ".recovery_vault");
                await File.WriteAllTextAsync(markerPath, $"Created: {DateTime.UtcNow:O}");

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create recovery vault with binding: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Verifies USB binding for recovery vault
        /// </summary>
        public bool VerifyUsbBinding(string vaultPath, string expectedUsbPath)
        {
            try
            {
                var bindingPath = Path.Combine(vaultPath, ".usb_binding");
                if (!File.Exists(bindingPath))
                {
                    return false;
                }

                var bindingJson = File.ReadAllText(bindingPath);
                var bindingInfo = System.Text.Json.JsonSerializer.Deserialize<UsbBindingInfo>(bindingJson);
                
                return bindingInfo?.UsbRootPath == expectedUsbPath;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the underlying recovery service
        /// </summary>
        public RecoveryVaultService GetRecoveryService() => _recoveryService;
    }

    /// <summary>
    /// USB binding information for recovery vault
    /// </summary>
    public class UsbBindingInfo
    {
        public string? UsbRootPath { get; set; }
        public string? BindingType { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
