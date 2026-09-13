using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace PhantomVault.Core.Services.Icons
{
    public enum IconKind
    {
        /// <summary>Single-colour category glyph; the picker recolours it.</summary>
        LineIcon,

        /// <summary>Brand logo; keeps its own colours.</summary>
        Logo,

        /// <summary>Uploaded by the user.</summary>
        Upload,

        /// <summary>Downloaded (Flaticon / favicon) by the user.</summary>
        Download
    }

    /// <summary>One pickable icon after de-duplication.</summary>
    public sealed record IconItem(string Name, string FilePath, IconKind Kind)
    {
        /// <summary>Line icons are single-colour glyphs and can be recoloured; logos keep their colours.</summary>
        public bool IsTintable => Kind == IconKind.LineIcon;

        /// <summary>Normalised name used for de-duplication and matching.</summary>
        public string MatchKey { get; } = IconRanker.Normalize(Name);
    }

    /// <summary>Where the library reads icons from. Separate from the defaults so tests can use temp folders.</summary>
    public sealed record IconLibrarySources(
        string VisualsRoot,
        string UserIconsDirectory,
        IReadOnlyList<string> DownloadDirectories);

    /// <summary>
    /// The organised, de-duplicated icon library.
    ///
    /// The shipped Assets/Visuals tree holds ~13k files, most of them repeats: every Cat Icons
    /// folder is one glyph saved in ten colours, the logo set is present twice, and thousands of
    /// files are byte-identical. This index collapses all of that to one entry per icon:
    /// one file per Cat Icons folder (the picker recolours it), one logo per normalised name
    /// (best format kept), and byte-identical files anywhere reduced to a single copy.
    /// App Icons — the app's own interface glyphs — are not offered at all.
    /// </summary>
    public sealed class IconLibraryIndex
    {
        public const string LineIconsFolder = "Cat Icons";
        public const string LogosFolder = "Entry Logos";

        // Logos for other password managers, used by the import screen; not entry icons.
        private const string ImportLogosFolder = "Import logos";

        // Each Cat Icons folder holds the same glyph in ten colours. Any one is enough because the
        // picker recolours it; charcoal is preferred as the most neutral source.
        private const string PreferredLineVariantSuffix = "_charcoal";

        private static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg", ".svg", ".ico", ".webp" };

        // Card-network marks stay out of the logo picker, as they were before. The old filter also
        // matched "logo", "brand", "bank", "card" and "pay", which silently hid the entire
        // Entry Logos folder and useful glyphs such as "Card"; that is no longer the case.
        private static readonly string[] WithheldLogoKeys =
            { "visa", "mastercard", "amex", "americanexpress", "maestro", "cirrus", "westernunion" };

        public IReadOnlyList<IconItem> LineIcons { get; }
        public IReadOnlyList<IconItem> Logos { get; }
        public IReadOnlyList<IconItem> Uploads { get; }
        public IReadOnlyList<IconItem> Downloads { get; }

        /// <summary>How many files were dropped as repeats (colour copies are not counted).</summary>
        public int DuplicatesRemoved { get; }

        private IconLibraryIndex(
            IReadOnlyList<IconItem> lineIcons,
            IReadOnlyList<IconItem> logos,
            IReadOnlyList<IconItem> uploads,
            IReadOnlyList<IconItem> downloads,
            int duplicatesRemoved)
        {
            LineIcons = lineIcons;
            Logos = logos;
            Uploads = uploads;
            Downloads = downloads;
            DuplicatesRemoved = duplicatesRemoved;
        }

        private static string AppDataRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhantomVault");

        /// <summary>
        /// Per-user folder for uploaded icons. Uploads used to be copied into the app's install
        /// folder, which an update can replace and which may be read-only.
        /// </summary>
        public static string UserIconsDirectory => Path.Combine(AppDataRoot, "MyIcons");

        /// <summary>Where recoloured copies are written; kept out of the "My icons" listing.</summary>
        public static string RecolouredDirectory => Path.Combine(UserIconsDirectory, "Recoloured");

        /// <summary>Where the icon downloader stores what it fetches.</summary>
        public static string DownloadsDirectory => Path.Combine(AppDataRoot, "IconCache");

        public static IconLibrarySources DefaultSources(string visualsRoot) => new(
            visualsRoot,
            UserIconsDirectory,
            new[]
            {
                DownloadsDirectory,
                // Entry-specific downloads from IconDownloaderViewModel land here.
                Path.Combine(AppContext.BaseDirectory, "Assets", "Icons")
            });

        public static IconLibraryIndex Build(IconLibrarySources sources)
        {
            ArgumentNullException.ThrowIfNull(sources);

            int removed = 0;

            var lineIcons = LoadLineIcons(Path.Combine(sources.VisualsRoot, LineIconsFolder), ref removed);
            var logos = LoadLogos(Path.Combine(sources.VisualsRoot, LogosFolder), ref removed);
            var uploads = LoadUploads(sources, ref removed);
            var downloads = LoadDownloads(sources.DownloadDirectories, ref removed);

            // Byte-identical files across every list collapse to one copy. The user's own icons
            // win over shipped ones, so an uploaded copy of a logo stays under "My icons".
            var keep = RemoveContentDuplicates(uploads.Concat(downloads).Concat(lineIcons).Concat(logos), ref removed);

            return new IconLibraryIndex(
                lineIcons.Where(keep.Contains).ToList(),
                logos.Where(keep.Contains).ToList(),
                uploads.Where(keep.Contains).ToList(),
                downloads.Where(keep.Contains).ToList(),
                removed);
        }

        private static List<IconItem> LoadLineIcons(string root, ref int removed)
        {
            var items = new List<IconItem>();
            if (!Directory.Exists(root)) return items;

            foreach (var dir in SafeDirectories(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var files = SupportedFiles(dir, SearchOption.TopDirectoryOnly)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (files.Count == 0) continue;

                var source = files.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
                                 .EndsWith(PreferredLineVariantSuffix, StringComparison.OrdinalIgnoreCase))
                             ?? files[0];
                items.Add(new IconItem(Path.GetFileName(dir), source, IconKind.LineIcon));
            }

            foreach (var file in SupportedFiles(root, SearchOption.TopDirectoryOnly))
                items.Add(new IconItem(CleanName(file), file, IconKind.LineIcon));

            return KeepFirstPerName(items, ref removed);
        }

        private static List<IconItem> LoadLogos(string root, ref int removed)
        {
            if (!Directory.Exists(root)) return new List<IconItem>();

            var importLogos = Path.Combine(root, ImportLogosFolder);
            var candidates = SupportedFiles(root, SearchOption.AllDirectories)
                .Where(f => !IsUnder(f, importLogos))
                .Select(f => new IconItem(CleanName(f), f, IconKind.Logo))
                .Where(i => i.MatchKey.Length > 0 && !WithheldLogoKeys.Any(k => i.MatchKey.Contains(k, StringComparison.Ordinal)))
                .ToList();

            // The logo set exists twice over and in several formats; keep the best file per name.
            var best = candidates
                .GroupBy(i => i.MatchKey, StringComparer.Ordinal)
                .Select(g => g.OrderBy(i => FormatRank(i.FilePath)).ThenByDescending(i => SafeLength(i.FilePath)).First())
                .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            removed += candidates.Count - best.Count;
            return best;
        }

        private static List<IconItem> LoadUploads(IconLibrarySources sources, ref int removed)
        {
            var files = new List<string>();
            if (Directory.Exists(sources.UserIconsDirectory))
                files.AddRange(SupportedFiles(sources.UserIconsDirectory, SearchOption.TopDirectoryOnly));

            // Older builds imported into the root of Assets/Visuals; those are the user's uploads too.
            if (Directory.Exists(sources.VisualsRoot))
                files.AddRange(SupportedFiles(sources.VisualsRoot, SearchOption.TopDirectoryOnly));

            return files
                .OrderByDescending(SafeLastWrite)
                .Select(f => new IconItem(CleanName(f), f, IconKind.Upload))
                .ToList();
        }

        private static List<IconItem> LoadDownloads(IEnumerable<string> directories, ref int removed)
        {
            var files = directories
                .Where(Directory.Exists)
                .SelectMany(d => SupportedFiles(d, SearchOption.AllDirectories))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(SafeLastWrite);

            return files.Select(f => new IconItem(CleanName(f), f, IconKind.Download)).ToList();
        }

        private static List<IconItem> KeepFirstPerName(List<IconItem> items, ref int removed)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var kept = new List<IconItem>();
            foreach (var item in items)
            {
                // Glyphs such as "@" normalise to a key of their own ("at"); an empty key is kept as-is.
                if (item.MatchKey.Length == 0 || seen.Add(item.MatchKey))
                    kept.Add(item);
                else
                    removed++;
            }
            return kept;
        }

        /// <summary>
        /// Returns the items to keep, dropping later byte-identical copies. Only files that share a
        /// size are hashed, so the cost is proportional to the real collisions, not the library.
        /// </summary>
        private static HashSet<IconItem> RemoveContentDuplicates(IEnumerable<IconItem> itemsInPriorityOrder, ref int removed)
        {
            var ordered = itemsInPriorityOrder.ToList();
            var keep = new HashSet<IconItem>(ordered);

            foreach (var sizeGroup in ordered.GroupBy(i => SafeLength(i.FilePath)).Where(g => g.Key > 0 && g.Count() > 1))
            {
                var seenHashes = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in sizeGroup)
                {
                    var hash = TryHash(item.FilePath);
                    if (hash == null) continue;
                    if (!seenHashes.Add(hash))
                    {
                        keep.Remove(item);
                        removed++;
                    }
                }
            }

            return keep;
        }

        /// <summary>
        /// Display name from a file name: drops copy markers ("twitter (5)"), stock-site suffixes
        /// ("AccorSeeklogo"), "img_" prefixes and trailing "-icon"/"-logo"/"-svg" tokens.
        /// </summary>
        public static string CleanName(string filePath)
        {
            var name = Path.GetFileNameWithoutExtension(filePath);
            name = Regex.Replace(name, @"\s*\(\d+\)$", string.Empty);
            name = Regex.Replace(name, @"(?i)^img_", string.Empty);
            name = Regex.Replace(name, @"(?i)seeklogo$", string.Empty);
            name = Regex.Replace(name, @"(?i)([-_ ](icon|logo|svg))+$", string.Empty);
            name = name.Replace('_', ' ').Trim();
            return name.Length == 0 ? Path.GetFileNameWithoutExtension(filePath) : name;
        }

        private static int FormatRank(string path) => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".svg" => 0,
            ".png" => 1,
            ".webp" => 2,
            ".jpg" or ".jpeg" => 3,
            _ => 4
        };

        private static bool IsSupported(string path) =>
            SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

        private static IEnumerable<string> SupportedFiles(string dir, SearchOption option)
        {
            try
            {
                return Directory.EnumerateFiles(dir, "*", option).Where(IsSupported).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }

        private static IEnumerable<string> SafeDirectories(string dir)
        {
            try { return Directory.EnumerateDirectories(dir).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
        }

        private static bool IsUnder(string path, string folder)
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        private static long SafeLength(string path)
        {
            try { return new FileInfo(path).Length; } catch { return 0; }
        }

        private static DateTime SafeLastWrite(string path)
        {
            try { return File.GetLastWriteTimeUtc(path); } catch { return DateTime.MinValue; }
        }

        private static string? TryHash(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                return Convert.ToHexString(SHA256.HashData(stream));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
