using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.BootRom;
using PhantomVault.Core.Utils;
using ReactiveUI;
using Serilog;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// Turning Boot ROM binding on and off for the open vault.
    ///
    /// Both directions re-wrap the manifest, because the ROM's contribution is part of the key
    /// that encrypts it. That is the one genuinely dangerous operation in this feature: a
    /// half-finished switch would leave a vault nobody can open. Every path here therefore
    /// verifies the result by reading the manifest back, and undoes its own work if anything
    /// fails.
    /// </summary>
    public sealed partial class VaultViewModel
    {
        /// <summary>The drive this vault was opened from, where Boot ROM artefacts live.</summary>
        private string? BootRomDriveRoot =>
            !string.IsNullOrEmpty(_usbRootPath) ? _usbRootPath :
            !string.IsNullOrEmpty(_mountPath) ? _mountPath : null;

        /// <summary>True when the open vault's device carries a Boot ROM marker.</summary>
        public bool IsBootRomBound =>
            BootRomDriveRoot is { Length: > 0 } root && BootRomService.IsBound(root);

        /// <summary>Digests the ROM is provisioned against. Must match what unlock supplies.</summary>
        private static (byte[] Integrity, byte[] Binding) ComputeBootRomDigests(string driveRoot) =>
            BootRomDigests.For(driveRoot);

        /// <summary>
        /// Provisions a Boot ROM for this vault and re-wraps the manifest so its key now includes
        /// the ROM's contribution. Returns the recovery code to show the user once, or null if
        /// nothing was changed.
        /// </summary>
        public async Task<string?> EnableBootRomBindingAsync()
        {
            if (BootRomDriveRoot is not { Length: > 0 } driveRoot)
            {
                await _dialogService.ShowErrorAsync("Boot ROM unavailable",
                    "This vault is not open from a device that can carry a Boot ROM.", _ownerWindow);
                return null;
            }
            if (string.IsNullOrWhiteSpace(_manifestPath) || _cachedRuntimeManifest is null)
            {
                await _dialogService.ShowErrorAsync("Boot ROM unavailable",
                    "The vault manifest is not loaded, so Boot ROM protection cannot be enabled right now.", _ownerWindow);
                return null;
            }
            if (string.IsNullOrWhiteSpace(_vaultKeyfilePath))
            {
                await _dialogService.ShowErrorAsync("Keyfile required",
                    "Boot ROM protection seals the ROM to this vault's keyfile material, so a keyfile is required.", _ownerWindow);
                return null;
            }
            if (BootRomService.IsBound(driveRoot))
            {
                await _dialogService.ShowInfoAsync("Already protected",
                    "This vault already has Boot ROM protection enabled.", _ownerWindow);
                return null;
            }

            // _manifestPath is guaranteed non-empty by the guard above.
            string manifestPath = _manifestPath;

            // Container-backed vaults cannot be re-keyed in place yet.
            //
            // Writing a manifest that lives inside a .pvault container first authenticates and
            // reads the existing container. Registering the ROM contribution beforehand changes
            // the derived key, so that read fails against a container written without it — and the
            // rollback write fails the same way. It is safe (nothing is modified; the vault still
            // opens) but it cannot succeed, so it is refused here rather than attempted.
            //
            // Boot ROM applied at vault CREATION is unaffected: the container is written bound
            // from the start and never re-read under a changed key.
            if (_manifestPath.EndsWith(".pvault", StringComparison.OrdinalIgnoreCase))
            {
                Log.Information("[BootRom] Enable refused: {Manifest} is a container vault and cannot be re-keyed in place",
                    System.IO.Path.GetFileName(_manifestPath));

                await _dialogService.ShowInfoAsync(
                    "Not available for this vault",
                    "Boot ROM protection cannot be added to a vault that is already set up in this format.\n\n"
                    + "It can be enabled when a vault is created. Your vault has not been changed.",
                    _ownerWindow);
                return null;
            }

            var (integrity, binding) = ComputeBootRomDigests(driveRoot);
            BootRomProvisionResult? provisioned = null;

            try
            {
                IsBusy = true;
                StatusMessage = "Creating this vault's Boot ROM…";

                provisioned = await Task.Run(() =>
                    new BootRomProvisioner().Provision(driveRoot, _vaultKeyfilePath, integrity, binding)).ConfigureAwait(true);

                // From here the manifest key changes. Register the contribution first so the
                // write below derives with it, exactly as the next unlock will.
                BootRomSession.Set(manifestPath, provisioned.Contribution);

                // Pin the marker inside the encrypted manifest, so a marker swapped on the device
                // is detectable once the vault opens.
                _cachedRuntimeManifest.BootRomMarkerHashBase64 =
                    Convert.ToBase64String(SHA256.HashData(provisioned.Marker.CanonicalBytes()));

                StatusMessage = "Re-protecting the vault…";
                await Task.Run(() =>
                {
                    _manifestService.WriteManifestSecure(
                        _cachedRuntimeManifest,
                        manifestPath,
                        _vaultPassword ?? SecurePassword.Empty(),
                        _vaultKeyfilePath,
                        usbSerial: null,
                        requireDualFactor: false,
                        overrideKdfParams: null);
                }).ConfigureAwait(true);

                // Prove the vault still opens before telling the user it worked.
                await Task.Run(() =>
                {
                    _manifestService.ReadManifestSecure(
                        manifestPath,
                        _vaultPassword ?? SecurePassword.Empty(),
                        _vaultKeyfilePath);
                }).ConfigureAwait(true);

                StatusMessage = "Boot ROM protection enabled";
                Log.Information("[BootRom] Binding enabled for {Manifest}", System.IO.Path.GetFileName(_manifestPath));
                this.RaisePropertyChanged(nameof(IsBootRomBound));
                return provisioned.RecoveryCode;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[BootRom] Enabling binding failed; rolling back");
                await RollBackFailedEnableAsync(driveRoot, manifestPath);
                await _dialogService.ShowErrorAsync(
                    "Boot ROM protection not enabled",
                    "The vault was left exactly as it was. " + ErrorMessageService.GetUserSafeMessage(ex),
                    _ownerWindow);
                return null;
            }
            finally
            {
                if (provisioned is not null)
                    CryptographicOperations.ZeroMemory(provisioned.Contribution);
                IsBusy = false;
            }
        }

        /// <summary>
        /// Checks the device's Boot ROM marker against the hash pinned in the manifest. Returns
        /// false only when both exist and disagree — evidence the marker was replaced. The
        /// binding itself is already enforced by the key, so this is reported, not acted on.
        /// </summary>
        public bool VerifyBootRomMarkerPin()
        {
            if (_cachedRuntimeManifest?.BootRomMarkerHashBase64 is not { Length: > 0 } pinned)
                return true;
            if (BootRomDriveRoot is not { Length: > 0 } driveRoot)
                return true;

            var marker = BootRomMarker.TryLoad(driveRoot);
            if (marker is null)
                return true;

            string actual = Convert.ToBase64String(SHA256.HashData(marker.CanonicalBytes()));
            if (string.Equals(actual, pinned, StringComparison.Ordinal))
                return true;

            Log.Warning("[BootRom] Marker on the device does not match the hash pinned in the manifest");
            return false;
        }

        /// <summary>
        /// Puts the vault back the way it was after a failed enable: manifest written without the
        /// contribution, and the half-provisioned artefacts removed.
        /// </summary>
        private async Task RollBackFailedEnableAsync(string driveRoot, string manifestPath)
        {
            BootRomSession.Clear(manifestPath);
            if (_cachedRuntimeManifest is not null)
                _cachedRuntimeManifest.BootRomMarkerHashBase64 = null;
            try
            {
                if (_cachedRuntimeManifest is not null && !string.IsNullOrWhiteSpace(_vaultKeyfilePath))
                {
                    await Task.Run(() =>
                    {
                        _manifestService.WriteManifestSecure(
                            _cachedRuntimeManifest,
                            manifestPath,
                            _vaultPassword ?? SecurePassword.Empty(),
                            _vaultKeyfilePath,
                            usbSerial: null,
                            requireDualFactor: false,
                            overrideKdfParams: null);
                    }).ConfigureAwait(true);
                }
            }
            catch (Exception ex)
            {
                // The manifest may already be unbound; the ROM artefacts are removed either way,
                // and the recovery code still opens the escrow if the write did land.
                Log.Error(ex, "[BootRom] Rollback write failed");
            }

            BootRomProvisioner.Remove(driveRoot);
            this.RaisePropertyChanged(nameof(IsBootRomBound));
        }

        /// <summary>
        /// Removes Boot ROM binding: re-wraps the manifest without the contribution, then deletes
        /// the ROM, marker and escrow from the device.
        /// </summary>
        public async Task<bool> DisableBootRomBindingAsync()
        {
            if (BootRomDriveRoot is not { Length: > 0 } driveRoot || !BootRomService.IsBound(driveRoot))
                return false;
            if (string.IsNullOrWhiteSpace(_manifestPath) || _cachedRuntimeManifest is null || string.IsNullOrWhiteSpace(_vaultKeyfilePath))
            {
                await _dialogService.ShowErrorAsync("Cannot remove protection",
                    "The vault manifest is not loaded, so Boot ROM protection cannot be removed right now.", _ownerWindow);
                return false;
            }

            string manifestPath = _manifestPath;
            byte[]? contribution = BootRomSession.Peek(manifestPath)?.AsSpan().ToArray();

            try
            {
                IsBusy = true;
                StatusMessage = "Removing Boot ROM protection…";

                // Drop the contribution so the manifest is rewritten under the plain key.
                BootRomSession.Clear(manifestPath);
                _cachedRuntimeManifest.BootRomMarkerHashBase64 = null;

                await Task.Run(() =>
                {
                    _manifestService.WriteManifestSecure(
                        _cachedRuntimeManifest,
                        manifestPath,
                        _vaultPassword ?? SecurePassword.Empty(),
                        _vaultKeyfilePath,
                        usbSerial: null,
                        requireDualFactor: false,
                        overrideKdfParams: null);
                }).ConfigureAwait(true);

                await Task.Run(() =>
                {
                    _manifestService.ReadManifestSecure(
                        manifestPath,
                        _vaultPassword ?? SecurePassword.Empty(),
                        _vaultKeyfilePath);
                }).ConfigureAwait(true);

                BootRomProvisioner.Remove(driveRoot);
                StatusMessage = "Boot ROM protection removed";
                Log.Information("[BootRom] Binding removed for {Manifest}", System.IO.Path.GetFileName(_manifestPath));
                this.RaisePropertyChanged(nameof(IsBootRomBound));
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[BootRom] Removing binding failed; restoring the previous state");

                // Put the contribution back so the vault keeps opening as it did.
                if (contribution is { Length: 32 })
                    BootRomSession.Set(manifestPath, contribution);

                await _dialogService.ShowErrorAsync(
                    "Boot ROM protection not removed",
                    "The vault was left protected. " + ErrorMessageService.GetUserSafeMessage(ex),
                    _ownerWindow);
                return false;
            }
            finally
            {
                if (contribution is not null)
                    CryptographicOperations.ZeroMemory(contribution);
                IsBusy = false;
            }
        }
    }
}
