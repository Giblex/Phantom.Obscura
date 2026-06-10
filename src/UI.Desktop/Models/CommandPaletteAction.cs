using System;

namespace PhantomVault.UI.Models
{

    public sealed class CommandPaletteAction
    {
        public CommandPaletteAction(
            string title,
            string category,
            Action execute,
            string? subtitle = null,
            string? shortcut = null,
            string? glyph = null,
            string? searchKeywords = null)
        {
            Title = title ?? throw new ArgumentNullException(nameof(title));
            Category = category ?? throw new ArgumentNullException(nameof(category));
            Execute = execute ?? throw new ArgumentNullException(nameof(execute));
            Subtitle = subtitle;
            Shortcut = shortcut;
            Glyph = glyph;
            SearchKeywords = searchKeywords;
        }

        public string Title { get; }

        public string? Subtitle { get; }

        public string Category { get; }

        public string? Shortcut { get; }

        public string? Glyph { get; }

        public string? SearchKeywords { get; }

        public Action Execute { get; }
    }
}

