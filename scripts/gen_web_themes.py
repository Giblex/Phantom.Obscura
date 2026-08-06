#!/usr/bin/env python3
"""
Generate the six website-demo palettes as full Phantom Obscura theme dictionaries.

This mirrors UI.Desktop/Services/CustomThemeGenerator.GenerateXaml so the produced
files carry the exact same (complete) resource-key set the app already proves valid
for runtime themes — only the base colours differ. Runtime themes are merged as an
override on top of PhantomTheme.Dark, so the glass/dashboard keys fall back to base.

Palettes come straight from the website demo (products/vault.css + vault.js).
"""
import os

OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "src", "UI.Desktop", "Assets", "Themes")


def strip_hash(h):
    return h[1:] if h.startswith("#") else h


def parse_rgb(h):
    h = strip_hash(h)
    if len(h) == 8:
        h = h[2:]
    return int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16)


def clamp(v):
    return max(0, min(255, int(v)))


def to_hex(r, g, b):
    return f"#{clamp(r):02X}{clamp(g):02X}{clamp(b):02X}"


def lighten(h, f):
    r, g, b = parse_rgb(h)
    return to_hex(r + (255 - r) * f, g + (255 - g) * f, b + (255 - b) * f)


def darken(h, f):
    r, g, b = parse_rgb(h)
    return to_hex(r * f, g * f, b * f)


def mix(h1, h2, f):
    r1, g1, b1 = parse_rgb(h1)
    r2, g2, b2 = parse_rgb(h2)
    return to_hex(r1 * (1 - f) + r2 * f, g1 * (1 - f) + g2 * f, b1 * (1 - f) + b2 * f)


def is_dark(h):
    r, g, b = parse_rgb(h)
    return (0.299 * r + 0.587 * g + 0.114 * b) / 255.0 < 0.5


def esc(s):
    return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def generate(c):
    P, S, Surf = c["PrimaryBackground"], c["SecondaryBackground"], c["SurfaceBackground"]
    Accent, AccentHover = c["Accent"], c["AccentHover"]
    TextPrimary, TextMuted, Border = c["TextPrimary"], c["TextMuted"], c["Border"]
    Success, Warning, Error = c["Success"], c["Warning"], c["Error"]
    dark = is_dark(P)

    control_fg = P if dark else "#FFFFFF"
    popup_bg = lighten(TextPrimary, 0.95) if dark else "#FFFFFF"
    popup_text = darken(P, 0.3) if dark else P
    header_bg = darken(P, 0.7) if dark else "#FFFFFF"
    window_bg = S
    card_bg = Surf
    disabled_text = darken(TextMuted, 0.7) if dark else lighten(TextMuted, 0.6)
    secondary_text = lighten(TextMuted, 0.4) if dark else darken(TextMuted, 0.4)
    input_bg = darken(P, 0.8) if dark else "#FFFFFF"
    textbox_bg = mix(Surf, Border, 0.5) if dark else "#FFFFFF"
    textbox_bg_hover = Border if dark else lighten(P, 0.97)
    shadow_color = darken(P, 0.3) if dark else "#A0A0A0"
    shadow_opacity = "0.4" if dark else "0.12"
    overlay_bg = f"#B0{strip_hash(P)}"
    separator_alpha = f"#30{strip_hash(Accent)}"
    selection_alpha = f"#50{strip_hash(Accent)}"
    accent_dim = darken(Accent, 0.7) if dark else lighten(Accent, 0.6)
    dialog_bg = mix(P, S, 0.4) if dark else "#FFFFFF"
    info = "#60A5FA"
    status_bg = mix(Surf, P, 0.5) if dark else "#FAFAFA"
    box_shadow_opacity = "#80000000" if dark else "#25000000"

    L = []
    a = L.append
    a('<ResourceDictionary xmlns="https://github.com/avaloniaui"')
    a('                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">')
    a(f'    <!-- Phantom Obscura website palette: {esc(c["Name"])} -->')
    a('')
    a(f'    <Color x:Key="Color.Navy900">{P}</Color>')
    a(f'    <Color x:Key="Color.Navy800">{mix(P, S, 0.5)}</Color>')
    a(f'    <Color x:Key="Color.Navy700">{S}</Color>')
    a(f'    <Color x:Key="Color.Navy600">{Surf}</Color>')
    a(f'    <Color x:Key="Color.Navy500">{mix(Surf, Border, 0.5)}</Color>')
    a(f'    <Color x:Key="Color.Navy400">{Border}</Color>')
    a('')
    a(f'    <Color x:Key="Color.TextPrimary">{TextPrimary}</Color>')
    a(f'    <Color x:Key="Color.TextMuted">{TextMuted}</Color>')
    a(f'    <Color x:Key="Color.TextOnDark">{TextPrimary}</Color>')
    a(f'    <Color x:Key="Color.TextOnLight">{P}</Color>')
    a('')
    a(f'    <Color x:Key="Color.Accent">{Accent}</Color>')
    a(f'    <Color x:Key="Color.AccentHover">{AccentHover}</Color>')
    a(f'    <Color x:Key="Color.AccentDim">{accent_dim}</Color>')
    a('')
    a(f'    <Color x:Key="Color.Success">{Success}</Color>')
    a(f'    <Color x:Key="Color.Warning">{Warning}</Color>')
    a(f'    <Color x:Key="Color.Danger">{Error}</Color>')
    a(f'    <Color x:Key="Color.Info">{info}</Color>')
    a('')
    a(f'    <SolidColorBrush x:Key="WindowBackgroundBrush" Color="{window_bg}"/>')
    a(f'    <SolidColorBrush x:Key="HeaderBackgroundBrush" Color="{header_bg}"/>')
    a(f'    <SolidColorBrush x:Key="FooterBackgroundBrush" Color="{header_bg}"/>')
    a(f'    <SolidColorBrush x:Key="ContentBackgroundBrush" Color="{card_bg}"/>')
    a(f'    <SolidColorBrush x:Key="CardBackgroundBrush" Color="{card_bg}"/>')
    a(f'    <SolidColorBrush x:Key="SettingsPanelBackgroundBrush" Color="{card_bg}"/>')
    a(f'    <SolidColorBrush x:Key="SettingsPanelBorderBrush" Color="{Border}"/>')
    a(f'    <SolidColorBrush x:Key="TileGlassBrush" Color="{card_bg}"/>')
    a(f'    <SolidColorBrush x:Key="TileGapBrush" Color="{window_bg}"/>')
    a('')
    a(f'    <SolidColorBrush x:Key="AccentBrush" Color="{Accent}"/>')
    a(f'    <SolidColorBrush x:Key="AccentHoverBrush" Color="{AccentHover}"/>')
    a(f'    <SolidColorBrush x:Key="SecondaryAccentBrush" Color="{accent_dim}"/>')
    a('')
    a(f'    <Color x:Key="PrimaryTextColor">{TextPrimary}</Color>')
    a(f'    <SolidColorBrush x:Key="PrimaryTextBrush" Color="{TextPrimary}"/>')
    a(f'    <SolidColorBrush x:Key="ControlForegroundBrush" Color="{control_fg}"/>')
    a(f'    <SolidColorBrush x:Key="HeaderTextBrush" Color="{TextPrimary}"/>')
    a(f'    <SolidColorBrush x:Key="HeaderMutedTextBrush" Color="{secondary_text}"/>')
    a(f'    <SolidColorBrush x:Key="FooterTextBrush" Color="{TextPrimary}"/>')
    a(f'    <SolidColorBrush x:Key="FooterMutedTextBrush" Color="{secondary_text}"/>')
    a(f'    <SolidColorBrush x:Key="SecondaryTextBrush" Color="{secondary_text}"/>')
    a(f'    <SolidColorBrush x:Key="MutedTextBrush" Color="{TextMuted}"/>')
    a(f'    <SolidColorBrush x:Key="DisabledTextBrush" Color="{disabled_text}"/>')
    a('')
    a(f'    <SolidColorBrush x:Key="HeaderButtonBackgroundBrush" Color="{card_bg}"/>')
    a(f'    <SolidColorBrush x:Key="HeaderButtonHoverBrush" Color="{mix(card_bg, Border, 0.5)}"/>')
    a(f'    <SolidColorBrush x:Key="HeaderButtonPressedBrush" Color="{window_bg}"/>')
    a(f'    <SolidColorBrush x:Key="HeaderButtonForegroundBrush" Color="{TextPrimary}"/>')
    a(f'    <SolidColorBrush x:Key="HeaderButtonBorderBrush">#55{strip_hash(card_bg)}</SolidColorBrush>')
    a('')
    a(f'    <SolidColorBrush x:Key="ControlBackgroundBrush" Color="{input_bg}"/>')
    a(f'    <SolidColorBrush x:Key="ControlHoverBrush" Color="{mix(input_bg, S, 0.5)}"/>')
    a(f'    <SolidColorBrush x:Key="InputBackgroundBrush" Color="{input_bg}"/>')
    a(f'    <SolidColorBrush x:Key="InputForegroundBrush" Color="{TextPrimary}"/>')
    a(f'    <SolidColorBrush x:Key="TextSelectionBrush" Color="{selection_alpha}"/>')
    a(f'    <SolidColorBrush x:Key="TextCaretBrush" Color="{Accent}"/>')
    a(f'    <SolidColorBrush x:Key="ControlBorderBrush" Color="{Border}"/>')
    a(f'    <SolidColorBrush x:Key="OverlayBackgroundBrush" Color="{overlay_bg}"/>')
    a(f'    <SolidColorBrush x:Key="ReadOnlyFieldBackgroundBrush" Color="{card_bg}"/>')
    a(f'    <SolidColorBrush x:Key="ReadOnlyFieldForegroundBrush" Color="{secondary_text}"/>')
    a(f'    <SolidColorBrush x:Key="CategoryPanelBackgroundBrush" Color="{window_bg}"/>')
    a('    <SolidColorBrush x:Key="HighContrastBorderBrush" Color="#00000000"/>')
    a('')
    a(f'    <SolidColorBrush x:Key="TextBoxBackgroundBrush" Color="{textbox_bg}"/>')
    a(f'    <SolidColorBrush x:Key="TextBoxBackgroundHoverBrush" Color="{textbox_bg_hover}"/>')
    a(f'    <SolidColorBrush x:Key="TextBoxBackgroundFocusedBrush" Color="{textbox_bg_hover}"/>')
    a(f'    <SolidColorBrush x:Key="TextBoxBackgroundDisabledBrush" Color="{card_bg}"/>')
    a(f'    <SolidColorBrush x:Key="TextBoxBorderBrush" Color="{Border}"/>')
    a(f'    <SolidColorBrush x:Key="TextBoxBorderHoverBrush" Color="{Accent}"/>')
    a(f'    <SolidColorBrush x:Key="TextBoxBorderFocusedBrush" Color="{Accent}"/>')
    a(f'    <SolidColorBrush x:Key="TextBrush" Color="{popup_text}"/>')
    a('')
    a(f'    <SolidColorBrush x:Key="DialogBackgroundBrush" Color="{dialog_bg}"/>')
    a(f'    <SolidColorBrush x:Key="DialogBorderBrush" Color="{Border}"/>')
    a('')
    a('    <SolidColorBrush x:Key="ButtonForegroundBrush" Color="#FFFFFF"/>')
    a(f'    <SolidColorBrush x:Key="ButtonAccentForegroundBrush" Color="{"#FFFFFF" if dark else P}"/>')
    a(f'    <SolidColorBrush x:Key="ButtonSecondaryForegroundBrush" Color="{TextPrimary}"/>')
    a(f'    <SolidColorBrush x:Key="ButtonDisabledForegroundBrush" Color="{disabled_text}"/>')
    a('')
    a(f'    <SolidColorBrush x:Key="ContentTextBrush" Color="{TextPrimary}"/>')
    a(f'    <SolidColorBrush x:Key="ContentSecondaryTextBrush" Color="{secondary_text}"/>')
    a(f'    <SolidColorBrush x:Key="ContentMutedTextBrush" Color="{TextMuted}"/>')
    a(f'    <SolidColorBrush x:Key="CardTextBrush" Color="{TextPrimary}"/>')
    a(f'    <SolidColorBrush x:Key="CardSecondaryTextBrush" Color="{secondary_text}"/>')
    a('')
    a(f'    <SolidColorBrush x:Key="InputTextBrush" Color="{popup_text}"/>')
    a(f'    <SolidColorBrush x:Key="InputPlaceholderBrush" Color="{disabled_text}"/>')
    a(f'    <SolidColorBrush x:Key="LabelTextBrush" Color="{TextPrimary}"/>')
    a('')
    a(f'    <SolidColorBrush x:Key="FlyoutPresenterBackground" Color="{popup_bg}"/>')
    a(f'    <SolidColorBrush x:Key="MenuFlyoutPresenterBackground" Color="{popup_bg}"/>')
    a(f'    <SolidColorBrush x:Key="ComboBoxDropDownBackground" Color="{popup_bg}"/>')
    a(f'    <SolidColorBrush x:Key="PopupBackgroundBrush" Color="{popup_bg}"/>')
    a(f'    <SolidColorBrush x:Key="SystemControlBackgroundChromeMediumLowBrush" Color="{popup_bg}"/>')
    a(f'    <SolidColorBrush x:Key="SystemControlForegroundBaseHighBrush" Color="{popup_text}"/>')
    a(f'    <SolidColorBrush x:Key="ComboBoxItemForeground" Color="{popup_text}"/>')
    a(f'    <SolidColorBrush x:Key="ComboBoxItemForegroundPointerOver" Color="{popup_text}"/>')
    a(f'    <SolidColorBrush x:Key="ComboBoxItemForegroundSelected" Color="{popup_text}"/>')
    a('')
    a(f'    <SolidColorBrush x:Key="ListItemTextBrush" Color="{TextPrimary}"/>')
    a(f'    <SolidColorBrush x:Key="ListItemSecondaryTextBrush" Color="{TextMuted}"/>')
    a('    <SolidColorBrush x:Key="ListItemSelectedTextBrush" Color="#FFFFFF"/>')
    a('')
    a('    <SolidColorBrush x:Key="ButtonWhiteBackgroundBrush">#FFFFFF</SolidColorBrush>')
    a(f'    <SolidColorBrush x:Key="SeparatorBrush">{separator_alpha}</SolidColorBrush>')
    a('    <SolidColorBrush x:Key="CardSeparatorBrush" Color="#00000000"/>')
    a('')
    a('    <SolidColorBrush x:Key="QuickFilterGlassBrush">#00FFFFFF</SolidColorBrush>')
    a(f'    <SolidColorBrush x:Key="QuickFilterGlassHoverBrush">#18{strip_hash(Accent)}</SolidColorBrush>')
    a(f'    <SolidColorBrush x:Key="QuickFilterGlassActiveBrush">#30{strip_hash(Accent)}</SolidColorBrush>')
    a(f'    <SolidColorBrush x:Key="QuickFilterGlassActiveHoverBrush">#40{strip_hash(Accent)}</SolidColorBrush>')
    a(f'    <SolidColorBrush x:Key="QuickFilterGlassBorderBrush">#30{strip_hash(Accent)}</SolidColorBrush>')
    a(f'    <SolidColorBrush x:Key="QuickFilterGlassActiveBorderBrush">#50{strip_hash(Accent)}</SolidColorBrush>')
    a('')
    a(f'    <SolidColorBrush x:Key="SuccessBrush" Color="{Success}"/>')
    a(f'    <SolidColorBrush x:Key="WarningBrush" Color="{Warning}"/>')
    a(f'    <SolidColorBrush x:Key="ErrorBrush" Color="{Error}"/>')
    a(f'    <SolidColorBrush x:Key="SuccessBackgroundBrush" Color="{status_bg}"/>')
    a(f'    <SolidColorBrush x:Key="WarningBackgroundBrush" Color="{status_bg}"/>')
    a(f'    <SolidColorBrush x:Key="ErrorBackgroundBrush" Color="{status_bg}"/>')
    a(f'    <SolidColorBrush x:Key="InfoBackgroundBrush" Color="{status_bg}"/>')
    a('')
    a('    <FontFamily x:Key="MainFontFamily">Segoe UI</FontFamily>')
    a('')
    a(f'    <Color x:Key="GlobalShadowColor">{shadow_color}</Color>')
    a(f'    <Color x:Key="HeaderAccentColor">{card_bg}</Color>')
    a('')
    a(f'    <DropShadowEffect x:Key="DarkSubtleDropShadow" Color="{shadow_color}" Opacity="{shadow_opacity}" BlurRadius="22"/>')
    a(f'    <DropShadowEffect x:Key="SubtleDropShadow" Color="{shadow_color}" Opacity="{shadow_opacity}" BlurRadius="22"/>')
    a(f'    <DropShadowEffect x:Key="DarkButtonShadow" Color="{shadow_color}" Opacity="{shadow_opacity}" BlurRadius="16"/>')
    a(f'    <DropShadowEffect x:Key="DarkButtonShadowHover" Color="{Accent}" Opacity="0.12" BlurRadius="20"/>')
    a(f'    <DropShadowEffect x:Key="DarkButtonShadowPressed" Color="{shadow_color}" Opacity="0.2" BlurRadius="10"/>')
    a(f'    <DropShadowEffect x:Key="HoverMenuDropShadow" Color="{shadow_color}" Opacity="{shadow_opacity}" BlurRadius="14"/>')
    a(f'    <DropShadowEffect x:Key="TileActiveShadow" Color="{Accent}" Opacity="0.1" BlurRadius="32"/>')
    a(f'    <DropShadowEffect x:Key="TileActiveShadowPressed" Color="{shadow_color}" Opacity="0.2" BlurRadius="16"/>')
    a(f'    <DropShadowEffect x:Key="FrontPageTileBaseShadow" Color="{shadow_color}" Opacity="{shadow_opacity}" BlurRadius="20"/>')
    a(f'    <DropShadowEffect x:Key="FooterGroundedShadow" Color="{shadow_color}" Opacity="{shadow_opacity}" BlurRadius="10"/>')
    a('')
    a(f'    <BoxShadows x:Key="SlideOutPanelShadow">-20 0 40 -5 {box_shadow_opacity}</BoxShadows>')
    a(f'    <BoxShadows x:Key="FlaggedPanelShadow">-16 0 32 -4 {box_shadow_opacity}</BoxShadows>')
    a('')
    a('    <LinearGradientBrush x:Key="TileOverlayBrush" StartPoint="0,0" EndPoint="1,1">')
    a(f'        <GradientStop Color="#18{strip_hash(Accent)}" Offset="0"/>')
    a('        <GradientStop Color="#00000000" Offset="1"/>')
    a('    </LinearGradientBrush>')
    a('')
    a('</ResourceDictionary>')
    a('')
    return "\n".join(L)


PALETTES = [
    ("WebDefaultDark", "Default Dark", dict(
        Name="Default Dark", PrimaryBackground="#0B1118", SecondaryBackground="#111923",
        SurfaceBackground="#16212D", Accent="#7FC8DC", AccentHover="#9AD8E8",
        TextPrimary="#E9EDF2", TextMuted="#93A1B0", Border="#283645",
        Success="#6FD3A3", Warning="#E0B15A", Error="#E0796A")),
    ("WebMidnightBlue", "Midnight Blue", dict(
        Name="Midnight Blue", PrimaryBackground="#06080D", SecondaryBackground="#0B1019",
        SurfaceBackground="#10182A", Accent="#6EA8FF", AccentHover="#93BEFF",
        TextPrimary="#E9EDF2", TextMuted="#8AA0B4", Border="#1E2A40",
        Success="#58D68D", Warning="#E0B15A", Error="#E0796A")),
    ("WebEmber", "Ember", dict(
        Name="Ember", PrimaryBackground="#120B08", SecondaryBackground="#1A1210",
        SurfaceBackground="#261A14", Accent="#E8A054", AccentHover="#F0B878",
        TextPrimary="#F2E9E2", TextMuted="#B89A86", Border="#3A2A1E",
        Success="#7BC88A", Warning="#E0B15A", Error="#E0796A")),
    ("WebArctic", "Arctic", dict(
        Name="Arctic", PrimaryBackground="#080E14", SecondaryBackground="#0E1620",
        SurfaceBackground="#14202E", Accent="#88E0EE", AccentHover="#AEEAF4",
        TextPrimary="#E9F2F5", TextMuted="#8DA8B4", Border="#243646",
        Success="#A2E8C0", Warning="#E0B15A", Error="#E0796A")),
    ("WebPhantomViolet", "Phantom Violet", dict(
        Name="Phantom Violet", PrimaryBackground="#0C0816", SecondaryBackground="#120E20",
        SurfaceBackground="#1A142E", Accent="#B48EF0", AccentHover="#C9AEF5",
        TextPrimary="#ECE6F5", TextMuted="#9D8FB4", Border="#2A2242",
        Success="#7CE8B0", Warning="#E0B15A", Error="#E0796A")),
    ("WebHighContrast", "High Contrast", dict(
        Name="High Contrast", PrimaryBackground="#000000", SecondaryBackground="#111111",
        SurfaceBackground="#1A1A1A", Accent="#00E5FF", AccentHover="#5CEEFF",
        TextPrimary="#FFFFFF", TextMuted="#CCCCCC", Border="#333333",
        Success="#00E5FF", Warning="#FFD600", Error="#FF5C5C")),
]


def main():
    out = os.path.abspath(OUT_DIR)
    for file_id, _display, colors in PALETTES:
        path = os.path.join(out, f"Theme.{file_id}.axaml")
        with open(path, "w", encoding="utf-8") as f:
            f.write(generate(colors))
        print(f"wrote {path}")


if __name__ == "__main__":
    main()
