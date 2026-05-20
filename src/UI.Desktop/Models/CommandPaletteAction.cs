using System;

namespace PhantomVault.UI.Models
{
    /// <summary>
    /// A single action that can be invoked from the command palette (Ctrl+K).
    /// Actions are built fresh each time the palette is opened, so they can
    /// safely close over live ViewModel state.
    /// </summary>
    /// <remarks>
    /// Stays a UI-layer plain object: no Avalonia references, no async.
    /// Asynchronous work belongs inside the captured <see cref="Execute"/>
    /// delegate which can fire-and-forget into a ReactiveCommand.
    /// </remarks>
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

        /// <summary>Primary label shown in the palette row.</summary>
        public string Title { get; }

        /// <summary>Optional secondary description shown beneath the title.</summary>
        public string? Subtitle { get; }

        /// <summary>Category label shown as a tag (e.g. "Vault", "Navigate", "Add").</summary>
        public string Category { get; }

        /// <summary>Optional shortcut hint shown at the right (e.g. "Ctrl+L").</summary>
        public string? Shortcut { get; }

        /// <summary>Optional glyph string (emoji or short text) shown left of the title.</summary>
        public string? Glyph { get; }

        /// <summary>
        /// Extra terms — comma- or space-separated — that should match this
        /// action in fuzzy search even when the visible Title doesn't contain
        /// them. Use for synonyms ("settings, preferences, options").
        /// </summary>
        public string? SearchKeywords { get; }

        /// <summary>The work the palette runs when the user activates this row.</summary>
        public Action Execute { get; }
    }
}
