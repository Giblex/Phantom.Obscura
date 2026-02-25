using System;
using System.Collections.Generic;

namespace PhantomVault.UI.Helpers
{
    /// <summary>
    /// Migrates legacy icon paths (old "Coloured Icons" folder, emojis, etc.)
    /// to the new Cat Icons library paths.  Every code-path that loads an icon
    /// from saved data should run the value through <see cref="Migrate"/> first.
    /// </summary>
    public static class IconPathMigrator
    {
        // Default fallback icon (General / Bookmark)
        public const string DefaultIcon = "/Assets/Visuals/Cat Icons/Bookmark/3914133 (1)_semi_dark_pastel_blue.png";

        // Well-known category icon paths
        public const string LoginsIcon   = "/Assets/Visuals/Cat Icons/Key/Key_golden_pastel_yellow.png";
        public const string PaymentIcon  = "/Assets/Visuals/Cat Icons/Credit cards/Credit cards_teal.png";
        public const string NotesIcon    = "/Assets/Visuals/Cat Icons/Notes/3917361_pink_red_peach.png";
        public const string BankingIcon  = "/Assets/Visuals/Cat Icons/Wallet/wallet_semi_dark_pastel_blue.png";
        public const string PersonalIcon = "/Assets/Visuals/Cat Icons/Profile/Profile_purple.png";
        public const string TrashIcon    = "/Assets/Visuals/Cat Icons/rubbish/rubbish_charcoal.png";
        public const string GeneralIcon  = DefaultIcon;
        public const string WiFiIcon     = "/Assets/Visuals/Cat Icons/Nodes/3917447 (1)_semi_dark_pastel_blue.png";
        public const string IdIcon       = "/Assets/Visuals/Cat Icons/ID card/3914525 (1)_electric_blue.png";
        public const string CustomIcon   = "/Assets/Visuals/Cat Icons/Tags/Tags_purple.png";

        /// <summary>Emoji → Cat-Icon mapping.</summary>
        private static readonly Dictionary<string, string> EmojiMap = new(StringComparer.Ordinal)
        {
            { "🔑", LoginsIcon },
            { "💳", PaymentIcon },
            { "📝", NotesIcon },
            { "🏦", BankingIcon },
            { "👤", PersonalIcon },
            { "🗑️", TrashIcon },
            { "🗑",  TrashIcon },   // variant without VS16
            { "📁", DefaultIcon },
        };

        /// <summary>
        /// Returns a valid Cat Icons path.  If the input is already a Cat Icons
        /// path it is returned unchanged.  Old "Coloured Icons" paths and emoji
        /// strings are mapped to the new library.
        /// </summary>
        public static string Migrate(string? icon)
        {
            if (string.IsNullOrWhiteSpace(icon))
                return DefaultIcon;

            // Already a Cat Icons path – nothing to do.
            if (icon.Contains("/Cat Icons/", StringComparison.OrdinalIgnoreCase))
                return icon;

            // Emoji lookup
            if (EmojiMap.TryGetValue(icon, out var mapped))
                return mapped;

            // Legacy "Coloured Icons" path – can't map 1-to-1; use default.
            if (icon.Contains("Coloured Icons", StringComparison.OrdinalIgnoreCase) ||
                icon.Contains("Icons/Logos", StringComparison.OrdinalIgnoreCase))
                return DefaultIcon;

            // Any other non-path string (stale emoji, random text) – default.
            if (!icon.StartsWith("/Assets/", StringComparison.OrdinalIgnoreCase) &&
                !icon.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return DefaultIcon;

            // Unrecognised asset path – return as-is (might be a custom icon).
            return icon;
        }
    }
}
