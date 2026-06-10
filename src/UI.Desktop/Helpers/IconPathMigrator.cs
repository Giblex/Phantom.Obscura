using System;
using System.Collections.Generic;

namespace PhantomVault.UI.Helpers
{

    public static class IconPathMigrator
    {

        public const string DefaultIcon = "/Assets/Visuals/Cat Icons/Bookmark/3914133 (1)_semi_dark_pastel_blue.png";

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

        private static readonly Dictionary<string, string> EmojiMap = new(StringComparer.Ordinal)
        {
            { "🔑", LoginsIcon },
            { "💳", PaymentIcon },
            { "📝", NotesIcon },
            { "🏦", BankingIcon },
            { "👤", PersonalIcon },
            { "🗑️", TrashIcon },
            { "🗑",  TrashIcon },
            { "📁", DefaultIcon },
        };

        public static string Migrate(string? icon)
        {
            if (string.IsNullOrWhiteSpace(icon))
                return DefaultIcon;

            if (icon.Contains("/Cat Icons/", StringComparison.OrdinalIgnoreCase))
                return icon;

            if (EmojiMap.TryGetValue(icon, out var mapped))
                return mapped;

            if (icon.Contains("Coloured Icons", StringComparison.OrdinalIgnoreCase) ||
                icon.Contains("Icons/Logos", StringComparison.OrdinalIgnoreCase))
                return DefaultIcon;

            if (!icon.StartsWith("/Assets/", StringComparison.OrdinalIgnoreCase) &&
                !icon.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return DefaultIcon;

            return icon;
        }
    }
}

