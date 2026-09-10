using System;
using Avalonia.Media;

namespace PhantomVault.UI.ViewModels;

public class ListItemWrapper
{
    /// <summary>
    /// Colour used when a category carries none of its own — a neutral light grey, which at
    /// the header's tint opacity reads as a plain uncoloured row rather than an odd hue.
    /// </summary>
    private static readonly Color DefaultCategoryColor = Color.FromRgb(0xE5, 0xE7, 0xEB);

    private static readonly IBrush LightText = Brushes.White;

    public bool IsCategoryHeader { get; }
    public string? CategoryName { get; }
    public int CategoryItemCount { get; }
    public CredentialViewModel? Credential { get; }

    /// <summary>
    /// Header background: a low-opacity tint of the category colour.
    ///
    /// Two things were wrong before. The markup bound a colour STRING straight at
    /// SolidColorBrush.Color, which is an Avalonia Color — the binding resolved, so
    /// FallbackValue never applied, but the string never coerced either, leaving the brush
    /// transparent and the header showing bare page background. Handing the view a ready-made
    /// brush removes that conversion entirely.
    ///
    /// The tint is then deliberately faint rather than a solid pastel fill. The sidebar
    /// renders the same categories as muted glass pills with a bright accent bar, and a
    /// saturated block in the list read as a different design language sitting next to them.
    /// </summary>
    public IBrush CategoryBrush { get; }

    /// <summary>
    /// The category's colour at full strength, for the accent bar — the sidebar's 8x22 bar is
    /// what actually carries the colour identity there, so the header uses it the same way.
    /// </summary>
    public IBrush CategoryAccentBrush { get; }

    /// <summary>
    /// Header text brush — light, matching the sidebar labels rather than the dark-on-pastel
    /// the old solid-fill header used.
    /// </summary>
    public IBrush CategoryForegroundBrush { get; }

    private ListItemWrapper(bool isCategoryHeader, string? categoryName, string? categoryColor, int categoryItemCount, CredentialViewModel? credential)
    {
        IsCategoryHeader = isCategoryHeader;
        CategoryName = categoryName;
        CategoryItemCount = categoryItemCount;
        Credential = credential;

        var color = ParseOrDefault(categoryColor);

        // 0.16 keeps the hue readable against the dark page without becoming a block of
        // colour; it is the same weight the sidebar pills carry.
        CategoryBrush = new SolidColorBrush(color, 0.16);
        CategoryAccentBrush = new SolidColorBrush(color);

        // A 0.16 tint over the dark page composites to a dark background whatever the hue, so
        // the theme's light text is always the readable choice here. This is not the same
        // decision as the tiles, which fill with the colour at full strength and therefore do
        // need the luminance test in CategoryNameToForegroundBrushConverter.
        CategoryForegroundBrush = LightText;
    }

    private static Color ParseOrDefault(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return DefaultCategoryColor;
        return Color.TryParse(hex, out var parsed) ? parsed : DefaultCategoryColor;
    }

    public static ListItemWrapper CreateCategoryHeader(string categoryName, string? categoryColor, int itemCount)
    {
        return new ListItemWrapper(true, categoryName, categoryColor, itemCount, null);
    }

    public static ListItemWrapper CreateCredential(CredentialViewModel credential)
    {
        return new ListItemWrapper(false, null, null, 0, credential);
    }
}
