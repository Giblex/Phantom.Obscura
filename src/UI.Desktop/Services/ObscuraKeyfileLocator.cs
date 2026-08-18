using System;
using System.Collections.Generic;
using System.IO;
using PhantomVault.Core.Utils;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Finds the keyfile candidates present on a drive.
    ///
    /// <para>
    /// Needed because a v2 volume's header is encrypted under the keyfile, so any code path
    /// that opens a volume must know the keyfile <i>before</i> it opens it. Which keyfile is
    /// not knowable up front — the vault may have been sealed with the USB keyfile alone or
    /// with that keyfile composed with a host companion — so callers hand the whole candidate
    /// list to <c>ObscuraVolumeService.ResolveKeyfileAsync</c> and let the header's
    /// authentication tag decide.
    /// </para>
    ///
    /// <para>
    /// <b>Known duplication.</b> <c>VaultUnlockViewModel</c> has its own richer copy of this
    /// search, used for manifest authentication, which additionally consults the
    /// BlackSecure raw-selection check. Consolidating the two means editing the live unlock
    /// path, which is deliberately not being done in the same change that alters the on-disk
    /// format; this locator exists so the second caller (MainViewModel) does not become a
    /// third independent copy in the meantime.
    /// </para>
    /// </summary>
    internal static class ObscuraKeyfileLocator
    {
        /// <summary>Directories a keyfile is expected to live in, in priority order.</summary>
        private static IEnumerable<string> SearchPaths(string driveRoot)
        {
            yield return Path.Combine(driveRoot, ".phantom", "vaults");
            yield return Path.Combine(driveRoot, ".phantom");
            yield return driveRoot;
            yield return Path.Combine(driveRoot, "keys");
        }

        /// <summary>
        /// Every keyfile form worth trying against a volume on this drive, most likely first:
        /// each discovered keyfile composed with each host companion, then each on its own.
        /// Returns an empty list when the drive holds no keyfile at all.
        /// </summary>
        public static IReadOnlyList<string> BuildCandidates(string? driveRoot)
        {
            var candidates = new List<string>();
            if (string.IsNullOrWhiteSpace(driveRoot)) return candidates;

            void Add(string? candidate)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && !candidates.Contains(candidate))
                    candidates.Add(candidate!);
            }

            var keyfiles = new List<string>();
            foreach (var searchPath in SearchPaths(driveRoot))
            {
                try
                {
                    if (!Directory.Exists(searchPath)) continue;
                    keyfiles.AddRange(Directory.GetFiles(searchPath, "*.key", SearchOption.TopDirectoryOnly));
                }
                catch (Exception)
                {
                    // An unreadable directory is not a failure — the keyfile may be elsewhere.
                }
            }

            var companions = HostCompanionKeyfiles(driveRoot);

            // Composites first: a vault sealed with a composite cannot be opened by the USB
            // keyfile alone, whereas trying a composite against a USB-only vault simply fails
            // its tag and costs one cheap check.
            foreach (var keyfile in keyfiles)
                foreach (var companion in companions)
                    Add(CompositeKeyfilePath.Compose(keyfile, companion));

            foreach (var keyfile in keyfiles)
                Add(keyfile);

            return candidates;
        }

        private static IReadOnlyList<string> HostCompanionKeyfiles(string driveRoot)
        {
            var companions = new List<string>();
            try
            {
                foreach (var searchPath in SearchPaths(driveRoot))
                {
                    if (!Directory.Exists(searchPath)) continue;
                    foreach (var locator in Directory.GetFiles(searchPath, "*.hostkey", SearchOption.TopDirectoryOnly))
                    {
                        if (File.Exists(locator)) companions.Add(locator);
                    }
                }
            }
            catch (Exception)
            {
                // Best effort: without a companion the USB-only candidates still get tried.
            }
            return companions;
        }
    }
}
