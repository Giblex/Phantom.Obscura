using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PhantomVault.Core.Utils;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// The single implementation of "which keyfile opens a vault on this drive".
    ///
    /// <para>
    /// Needed because a v2 volume's header is encrypted under the keyfile, so any code path
    /// that opens a volume must know the keyfile <i>before</i> opening it. Which keyfile is
    /// not knowable up front — a vault may have been sealed with the USB keyfile alone, or
    /// with that keyfile composed with a host companion held on this machine — so callers
    /// pass the whole list to <c>ObscuraVolumeService.ResolveKeyfileAsync</c> and let the
    /// header's authentication tag pick the right one.
    /// </para>
    ///
    /// <para>
    /// Host companions do NOT live on the drive: they sit in
    /// <c>%LOCALAPPDATA%\PhantomObscura\HostKey\*.companion.key</c>, and the drive may name a
    /// specific one through a <c>.phantom\host-key\companion.locator</c> JSON file written at
    /// provisioning time by <c>SetupWizardViewModel</c>.
    /// </para>
    ///
    /// <para>
    /// This lived in two near-identical copies — here and in <c>VaultUnlockViewModel</c> —
    /// kept in sync by hand. Divergence between them does not fail loudly: a missed candidate
    /// means a composite-sealed vault silently refuses to open, which reads to the user as a
    /// corrupt vault. One implementation removes that failure mode, so callers delegate here
    /// rather than reimplementing the search.
    /// </para>
    /// </summary>
    internal static class ObscuraKeyfileLocator
    {
        private const string CompanionLocatorRelativePath = @".phantom\host-key\companion.locator";
        private const string CompanionFilePattern = "*.companion.key";

        private static IEnumerable<string> KeyfileSearchPaths(string driveRoot)
        {
            yield return Path.Combine(driveRoot, ".phantom", "vaults");
            yield return Path.Combine(driveRoot, ".phantom");
            yield return driveRoot;
            yield return Path.Combine(driveRoot, "keys");
        }

        /// <summary>
        /// Every keyfile form worth trying against a volume on this drive, most likely first.
        /// Empty when the drive holds no keyfile at all.
        /// </summary>
        public static IReadOnlyList<string> BuildCandidates(string? driveRoot)
        {
            if (string.IsNullOrWhiteSpace(driveRoot)) return Array.Empty<string>();

            var usbKeyfilePath = FindUsbKeyfile(driveRoot!);
            return string.IsNullOrWhiteSpace(usbKeyfilePath)
                ? Array.Empty<string>()
                : BuildCandidates(driveRoot!, usbKeyfilePath!);
        }

        /// <summary>
        /// Candidates for a primary keyfile the caller has already located. Accepts a composite
        /// path and reduces it, so a caller holding the output of <see cref="ComposeWithCompanion"/>
        /// does not have to unwrap it first.
        /// </summary>
        public static IReadOnlyList<string> BuildCandidates(string driveRoot, string primaryKeyfilePath)
        {
            var candidates = new List<string>();
            if (string.IsNullOrWhiteSpace(driveRoot) || string.IsNullOrWhiteSpace(primaryKeyfilePath))
                return candidates;

            var usbKeyfilePath = CompositeKeyfilePath.GetPrimaryPath(primaryKeyfilePath) ?? primaryKeyfilePath;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string? value)
            {
                if (!string.IsNullOrWhiteSpace(value) && seen.Add(value!))
                    candidates.Add(value!);
            }

            // The companion this drive explicitly names, if any — the most likely match.
            var locatorCompanion = TryResolveHostCompanion(driveRoot);
            if (!string.IsNullOrWhiteSpace(locatorCompanion))
                Add(CompositeKeyfilePath.Compose(usbKeyfilePath, locatorCompanion!));

            // Then every companion registered on this machine.
            foreach (var companion in HostCompanionKeyfiles())
                Add(CompositeKeyfilePath.Compose(usbKeyfilePath, companion));

            // Finally the USB keyfile on its own, for vaults sealed without a companion.
            Add(usbKeyfilePath);

            return candidates;
        }

        /// <summary>
        /// The bare USB keyfile on this drive, or null when the drive holds none.
        /// </summary>
        public static string? FindUsbKeyfile(string driveRoot)
        {
            if (string.IsNullOrWhiteSpace(driveRoot)) return null;

            foreach (var searchPath in KeyfileSearchPaths(driveRoot))
            {
                try
                {
                    if (!Directory.Exists(searchPath)) continue;
                    var keyFiles = Directory.GetFiles(searchPath, "*.key", SearchOption.TopDirectoryOnly);
                    if (keyFiles.Length > 0) return keyFiles[0];
                }
                catch (Exception)
                {
                    // An unreadable directory is not fatal — the keyfile may be in the next one.
                }
            }
            return null;
        }

        /// <summary>
        /// The drive's keyfile, composed with the companion the drive names when there is one.
        /// This is the single "best guess" form; <see cref="BuildCandidates(string)"/> is the
        /// form to prefer when the caller can try several.
        /// </summary>
        public static string? ComposeWithCompanion(string driveRoot)
        {
            var usbKeyfilePath = FindUsbKeyfile(driveRoot);
            if (string.IsNullOrWhiteSpace(usbKeyfilePath)) return null;

            var companion = TryResolveHostCompanion(driveRoot);
            return string.IsNullOrWhiteSpace(companion)
                ? usbKeyfilePath
                : CompositeKeyfilePath.Compose(usbKeyfilePath!, companion!);
        }

        /// <summary>
        /// The companion keyfile this drive names through its locator file, when that file
        /// exists and points at a companion that is actually present on this machine.
        /// </summary>
        public static string? TryResolveHostCompanion(string driveRoot)
        {
            try
            {
                var locatorPath = Path.Combine(driveRoot, CompanionLocatorRelativePath);
                if (!File.Exists(locatorPath)) return null;

                var locator = JsonSerializer.Deserialize<HostCompanionLocator>(File.ReadAllText(locatorPath));
                if (locator == null || string.IsNullOrWhiteSpace(locator.HostCompanionKeyfilePath)) return null;

                return File.Exists(locator.HostCompanionKeyfilePath) ? locator.HostCompanionKeyfilePath : null;
            }
            catch
            {
                return null;
            }
        }

        private static IReadOnlyList<string> HostCompanionKeyfiles()
        {
            try
            {
                var hostKeyDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PhantomObscura",
                    "HostKey");

                return Directory.Exists(hostKeyDir)
                    ? Directory.GetFiles(hostKeyDir, CompanionFilePattern, SearchOption.TopDirectoryOnly)
                    : Array.Empty<string>();
            }
            catch (Exception)
            {
                // Without a companion the USB-only candidate still gets tried.
                return Array.Empty<string>();
            }
        }
    }

    /// <summary>
    /// The on-drive pointer to a host companion keyfile. Written by the setup wizard when a
    /// vault is provisioned with a composite key, read on every unlock — one declaration so
    /// the writer and the readers cannot drift apart on the property name.
    /// </summary>
    internal sealed class HostCompanionLocator
    {
        public string HostCompanionKeyfilePath { get; init; } = string.Empty;
    }
}
