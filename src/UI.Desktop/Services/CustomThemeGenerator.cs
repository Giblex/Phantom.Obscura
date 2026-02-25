using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Generates a complete theme AXAML file from a small set of user-chosen base colors.
    /// Derives all ~150 resource keys automatically.
    /// </summary>
    public static class CustomThemeGenerator
    {
        /// <summary>
        /// Base colors the user picks in the theme editor.
        /// </summary>
        public sealed class ThemeColors
        {
            public string Name { get; set; } = "My Theme";
            public string PrimaryBackground { get; set; } = "#1A1A2E";
            public string SecondaryBackground { get; set; } = "#222240";
            public string SurfaceBackground { get; set; } = "#2A2A4A";
            public string Accent { get; set; } = "#00D9FF";
            public string AccentHover { get; set; } = "#33E5FF";
            public string TextPrimary { get; set; } = "#F0F0FF";
            public string TextMuted { get; set; } = "#8888AA";
            public string Border { get; set; } = "#404060";
            public string Success { get; set; } = "#4ADE80";
            public string Warning { get; set; } = "#FBBF24";
            public string Error { get; set; } = "#F87171";
        }

        /// <summary>
        /// Gets the custom themes directory path.
        /// </summary>
        public static string GetCustomThemesDir()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "PhantomVault", "custom-themes");
        }

        /// <summary>
        /// Generates and saves a theme AXAML file from user colors. Returns the file path.
        /// </summary>
        public static string GenerateAndSave(ThemeColors colors, string? existingPath = null)
        {
            var dir = GetCustomThemesDir();
            Directory.CreateDirectory(dir);

            var safeId = SanitizeId(colors.Name);
            var filePath = existingPath ?? Path.Combine(dir, $"Theme.Custom_{safeId}.axaml");

            var xaml = GenerateXaml(colors);
            File.WriteAllText(filePath, xaml, Encoding.UTF8);
            return filePath;
        }

        /// <summary>
        /// Generates theme AXAML content from base colors.
        /// </summary>
        public static string GenerateXaml(ThemeColors c)
        {
            // Determine if this is a light or dark theme based on primary background luminance
            bool isDark = IsColorDark(c.PrimaryBackground);
            var controlFg = isDark ? c.PrimaryBackground : "#FFFFFF";
            var popupBg = isDark ? LightenColor(c.TextPrimary, 0.95) : "#FFFFFF";
            var popupText = isDark ? DarkenColor(c.PrimaryBackground, 0.3) : c.PrimaryBackground;
            var headerBg = isDark ? DarkenColor(c.PrimaryBackground, 0.7) : "#FFFFFF";
            var windowBg = c.SecondaryBackground;
            var cardBg = c.SurfaceBackground;
            var disabledText = isDark ? DarkenColor(c.TextMuted, 0.7) : LightenColor(c.TextMuted, 0.6);
            var secondaryText = isDark ? LightenColor(c.TextMuted, 0.4) : DarkenColor(c.TextMuted, 0.4);
            var inputBg = isDark ? DarkenColor(c.PrimaryBackground, 0.8) : "#FFFFFF";
            var textBoxBg = isDark ? MixColor(c.SurfaceBackground, c.Border, 0.5) : "#FFFFFF";
            var textBoxBgHover = isDark ? c.Border : LightenColor(c.PrimaryBackground, 0.97);
            var shadowColor = isDark ? DarkenColor(c.PrimaryBackground, 0.3) : "#A0A0A0";
            var shadowOpacity = isDark ? "0.4" : "0.12";
            var overlayBg = $"#B0{StripHash(c.PrimaryBackground)}";
            var separatorAlpha = $"#30{StripHash(c.Accent)}";
            var selectionAlpha = $"#50{StripHash(c.Accent)}";
            var accentDim = isDark ? DarkenColor(c.Accent, 0.7) : LightenColor(c.Accent, 0.6);
            var dialogBg = isDark ? MixColor(c.PrimaryBackground, c.SecondaryBackground, 0.4) : "#FFFFFF";
            var info = "#60A5FA";

            var sb = new StringBuilder();
            sb.AppendLine("<ResourceDictionary xmlns=\"https://github.com/avaloniaui\"");
            sb.AppendLine("                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">");
            sb.AppendLine($"    <!-- Custom Theme: {EscapeXml(c.Name)} -->");
            sb.AppendLine();

            // Color scale
            sb.AppendLine($"    <Color x:Key=\"Color.Navy900\">{c.PrimaryBackground}</Color>");
            sb.AppendLine($"    <Color x:Key=\"Color.Navy800\">{MixColor(c.PrimaryBackground, c.SecondaryBackground, 0.5)}</Color>");
            sb.AppendLine($"    <Color x:Key=\"Color.Navy700\">{c.SecondaryBackground}</Color>");
            sb.AppendLine($"    <Color x:Key=\"Color.Navy600\">{c.SurfaceBackground}</Color>");
            sb.AppendLine($"    <Color x:Key=\"Color.Navy500\">{MixColor(c.SurfaceBackground, c.Border, 0.5)}</Color>");
            sb.AppendLine($"    <Color x:Key=\"Color.Navy400\">{c.Border}</Color>");
            sb.AppendLine();
            sb.AppendLine($"    <Color x:Key=\"Color.TextPrimary\">{c.TextPrimary}</Color>");
            sb.AppendLine($"    <Color x:Key=\"Color.TextMuted\">{c.TextMuted}</Color>");
            sb.AppendLine($"    <Color x:Key=\"Color.TextOnDark\">{c.TextPrimary}</Color>");
            sb.AppendLine($"    <Color x:Key=\"Color.TextOnLight\">{c.PrimaryBackground}</Color>");
            sb.AppendLine();
            sb.AppendLine($"    <Color x:Key=\"Color.Accent\">{c.Accent}</Color>");
            sb.AppendLine($"    <Color x:Key=\"Color.AccentHover\">{c.AccentHover}</Color>");
            sb.AppendLine($"    <Color x:Key=\"Color.AccentDim\">{accentDim}</Color>");
            sb.AppendLine();
            sb.AppendLine($"    <Color x:Key=\"Color.Success\">{c.Success}</Color>");
            sb.AppendLine($"    <Color x:Key=\"Color.Warning\">{c.Warning}</Color>");
            sb.AppendLine($"    <Color x:Key=\"Color.Danger\">{c.Error}</Color>");
            sb.AppendLine($"    <Color x:Key=\"Color.Info\">{info}</Color>");
            sb.AppendLine();

            // Brushes
            sb.AppendLine($"    <SolidColorBrush x:Key=\"WindowBackgroundBrush\" Color=\"{windowBg}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"HeaderBackgroundBrush\" Color=\"{headerBg}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"FooterBackgroundBrush\" Color=\"{headerBg}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"ContentBackgroundBrush\" Color=\"{cardBg}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"CardBackgroundBrush\" Color=\"{cardBg}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"SettingsPanelBackgroundBrush\" Color=\"{cardBg}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"SettingsPanelBorderBrush\" Color=\"{c.Border}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"TileGlassBrush\" Color=\"{cardBg}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"TileGapBrush\" Color=\"{windowBg}\"/>");
            sb.AppendLine();
            sb.AppendLine($"    <SolidColorBrush x:Key=\"AccentBrush\" Color=\"{c.Accent}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"AccentHoverBrush\" Color=\"{c.AccentHover}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"SecondaryAccentBrush\" Color=\"{accentDim}\"/>");
            sb.AppendLine();
            sb.AppendLine($"    <Color x:Key=\"PrimaryTextColor\">{c.TextPrimary}</Color>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"PrimaryTextBrush\" Color=\"{c.TextPrimary}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"ControlForegroundBrush\" Color=\"{controlFg}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"HeaderTextBrush\" Color=\"{c.TextPrimary}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"HeaderMutedTextBrush\" Color=\"{secondaryText}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"FooterTextBrush\" Color=\"{c.TextPrimary}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"FooterMutedTextBrush\" Color=\"{secondaryText}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"SecondaryTextBrush\" Color=\"{secondaryText}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"MutedTextBrush\" Color=\"{c.TextMuted}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"DisabledTextBrush\" Color=\"{disabledText}\"/>");
            sb.AppendLine();
            sb.AppendLine($"    <SolidColorBrush x:Key=\"HeaderButtonBackgroundBrush\" Color=\"{cardBg}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"HeaderButtonHoverBrush\" Color=\"{MixColor(cardBg, c.Border, 0.5)}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"HeaderButtonPressedBrush\" Color=\"{windowBg}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"HeaderButtonForegroundBrush\" Color=\"{c.TextPrimary}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"HeaderButtonBorderBrush\">#55{StripHash(cardBg)}</SolidColorBrush>");
            sb.AppendLine();
            sb.AppendLine($"    <SolidColorBrush x:Key=\"ControlBackgroundBrush\" Color=\"{inputBg}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"ControlHoverBrush\" Color=\"{MixColor(inputBg, c.SecondaryBackground, 0.5)}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"InputBackgroundBrush\" Color=\"{inputBg}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"InputForegroundBrush\" Color=\"{c.TextPrimary}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"TextSelectionBrush\" Color=\"{selectionAlpha}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"TextCaretBrush\" Color=\"{c.Accent}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"ControlBorderBrush\" Color=\"{c.Border}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"OverlayBackgroundBrush\" Color=\"{overlayBg}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"ReadOnlyFieldBackgroundBrush\" Color=\"{cardBg}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"ReadOnlyFieldForegroundBrush\" Color=\"{secondaryText}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"CategoryPanelBackgroundBrush\" Color=\"{windowBg}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"HighContrastBorderBrush\" Color=\"#00000000\"/>");
            sb.AppendLine();
            sb.AppendLine($"    <SolidColorBrush x:Key=\"TextBoxBackgroundBrush\" Color=\"{textBoxBg}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"TextBoxBackgroundHoverBrush\" Color=\"{textBoxBgHover}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"TextBoxBackgroundFocusedBrush\" Color=\"{textBoxBgHover}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"TextBoxBackgroundDisabledBrush\" Color=\"{cardBg}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"TextBoxBorderBrush\" Color=\"{c.Border}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"TextBoxBorderHoverBrush\" Color=\"{c.Accent}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"TextBoxBorderFocusedBrush\" Color=\"{c.Accent}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"TextBrush\" Color=\"{popupText}\"/>");
            sb.AppendLine();
            sb.AppendLine($"    <SolidColorBrush x:Key=\"DialogBackgroundBrush\" Color=\"{dialogBg}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"DialogBorderBrush\" Color=\"{c.Border}\"/>");
            sb.AppendLine();
            sb.AppendLine($"    <SolidColorBrush x:Key=\"ButtonForegroundBrush\" Color=\"#FFFFFF\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"ButtonAccentForegroundBrush\" Color=\"{(isDark ? "#FFFFFF" : c.PrimaryBackground)}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"ButtonSecondaryForegroundBrush\" Color=\"{c.TextPrimary}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"ButtonDisabledForegroundBrush\" Color=\"{disabledText}\"/>");
            sb.AppendLine();
            sb.AppendLine($"    <SolidColorBrush x:Key=\"ContentTextBrush\" Color=\"{c.TextPrimary}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"ContentSecondaryTextBrush\" Color=\"{secondaryText}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"ContentMutedTextBrush\" Color=\"{c.TextMuted}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"CardTextBrush\" Color=\"{c.TextPrimary}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"CardSecondaryTextBrush\" Color=\"{secondaryText}\"/>");
            sb.AppendLine();
            sb.AppendLine($"    <SolidColorBrush x:Key=\"InputTextBrush\" Color=\"{popupText}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"InputPlaceholderBrush\" Color=\"{disabledText}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"LabelTextBrush\" Color=\"{c.TextPrimary}\"/>");
            sb.AppendLine();
            sb.AppendLine($"    <SolidColorBrush x:Key=\"FlyoutPresenterBackground\" Color=\"{popupBg}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"MenuFlyoutPresenterBackground\" Color=\"{popupBg}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"ComboBoxDropDownBackground\" Color=\"{popupBg}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"PopupBackgroundBrush\" Color=\"{popupBg}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"SystemControlBackgroundChromeMediumLowBrush\" Color=\"{popupBg}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"SystemControlForegroundBaseHighBrush\" Color=\"{popupText}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"ComboBoxItemForeground\" Color=\"{popupText}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"ComboBoxItemForegroundPointerOver\" Color=\"{popupText}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"ComboBoxItemForegroundSelected\" Color=\"{popupText}\"/>");
            sb.AppendLine();
            sb.AppendLine($"    <SolidColorBrush x:Key=\"ListItemTextBrush\" Color=\"{c.TextPrimary}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"ListItemSecondaryTextBrush\" Color=\"{c.TextMuted}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"ListItemSelectedTextBrush\" Color=\"#FFFFFF\"/>");
            sb.AppendLine();
            sb.AppendLine($"    <SolidColorBrush x:Key=\"ButtonWhiteBackgroundBrush\">#FFFFFF</SolidColorBrush>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"SeparatorBrush\">{separatorAlpha}</SolidColorBrush>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"CardSeparatorBrush\" Color=\"#00000000\"/>");
            sb.AppendLine();
            sb.AppendLine($"    <SolidColorBrush x:Key=\"QuickFilterGlassBrush\">#00FFFFFF</SolidColorBrush>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"QuickFilterGlassHoverBrush\">#18{StripHash(c.Accent)}</SolidColorBrush>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"QuickFilterGlassActiveBrush\">#30{StripHash(c.Accent)}</SolidColorBrush>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"QuickFilterGlassActiveHoverBrush\">#40{StripHash(c.Accent)}</SolidColorBrush>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"QuickFilterGlassBorderBrush\">#30{StripHash(c.Accent)}</SolidColorBrush>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"QuickFilterGlassActiveBorderBrush\">#50{StripHash(c.Accent)}</SolidColorBrush>");
            sb.AppendLine();
            sb.AppendLine($"    <SolidColorBrush x:Key=\"SuccessBrush\" Color=\"{c.Success}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"WarningBrush\" Color=\"{c.Warning}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"ErrorBrush\" Color=\"{c.Error}\"/>");
            var statusBg = isDark ? MixColor(c.SurfaceBackground, c.PrimaryBackground, 0.5) : "#FAFAFA";
            sb.AppendLine($"    <SolidColorBrush x:Key=\"SuccessBackgroundBrush\" Color=\"{statusBg}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"WarningBackgroundBrush\" Color=\"{statusBg}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"ErrorBackgroundBrush\" Color=\"{statusBg}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"InfoBackgroundBrush\" Color=\"{statusBg}\"/>");
            sb.AppendLine();
            sb.AppendLine($"    <FontFamily x:Key=\"MainFontFamily\">Segoe UI</FontFamily>");
            sb.AppendLine();
            sb.AppendLine($"    <Color x:Key=\"GlobalShadowColor\">{shadowColor}</Color>");
            sb.AppendLine($"    <Color x:Key=\"HeaderAccentColor\">{cardBg}</Color>");
            sb.AppendLine();
            sb.AppendLine($"    <DropShadowEffect x:Key=\"DarkSubtleDropShadow\" Color=\"{shadowColor}\" Opacity=\"{shadowOpacity}\" BlurRadius=\"22\"/>");
            sb.AppendLine($"    <DropShadowEffect x:Key=\"SubtleDropShadow\" Color=\"{shadowColor}\" Opacity=\"{shadowOpacity}\" BlurRadius=\"22\"/>");
            sb.AppendLine($"    <DropShadowEffect x:Key=\"DarkButtonShadow\" Color=\"{shadowColor}\" Opacity=\"{shadowOpacity}\" BlurRadius=\"16\"/>");
            sb.AppendLine($"    <DropShadowEffect x:Key=\"DarkButtonShadowHover\" Color=\"{c.Accent}\" Opacity=\"0.12\" BlurRadius=\"20\"/>");
            sb.AppendLine($"    <DropShadowEffect x:Key=\"DarkButtonShadowPressed\" Color=\"{shadowColor}\" Opacity=\"0.2\" BlurRadius=\"10\"/>");
            sb.AppendLine($"    <DropShadowEffect x:Key=\"HoverMenuDropShadow\" Color=\"{shadowColor}\" Opacity=\"{shadowOpacity}\" BlurRadius=\"14\"/>");
            sb.AppendLine($"    <DropShadowEffect x:Key=\"TileActiveShadow\" Color=\"{c.Accent}\" Opacity=\"0.1\" BlurRadius=\"32\"/>");
            sb.AppendLine($"    <DropShadowEffect x:Key=\"TileActiveShadowPressed\" Color=\"{shadowColor}\" Opacity=\"0.2\" BlurRadius=\"16\"/>");
            sb.AppendLine($"    <DropShadowEffect x:Key=\"FrontPageTileBaseShadow\" Color=\"{shadowColor}\" Opacity=\"{shadowOpacity}\" BlurRadius=\"20\"/>");
            sb.AppendLine($"    <DropShadowEffect x:Key=\"FooterGroundedShadow\" Color=\"{shadowColor}\" Opacity=\"{shadowOpacity}\" BlurRadius=\"10\"/>");
            sb.AppendLine();
            var boxShadowOpacity = isDark ? "#80000000" : "#25000000";
            sb.AppendLine($"    <BoxShadows x:Key=\"SlideOutPanelShadow\">-20 0 40 -5 {boxShadowOpacity}</BoxShadows>");
            sb.AppendLine($"    <BoxShadows x:Key=\"FlaggedPanelShadow\">-16 0 32 -4 {boxShadowOpacity}</BoxShadows>");
            sb.AppendLine();
            sb.AppendLine($"    <LinearGradientBrush x:Key=\"TileOverlayBrush\" StartPoint=\"0,0\" EndPoint=\"1,1\">");
            sb.AppendLine($"        <GradientStop Color=\"#18{StripHash(c.Accent)}\" Offset=\"0\"/>");
            sb.AppendLine($"        <GradientStop Color=\"#00000000\" Offset=\"1\"/>");
            sb.AppendLine($"    </LinearGradientBrush>");
            sb.AppendLine();
            sb.AppendLine("</ResourceDictionary>");

            return sb.ToString();
        }

        /// <summary>
        /// Generates theme AXAML with Attestor-specific brush keys appended.
        /// </summary>
        public static string GenerateAttestorXaml(ThemeColors c)
        {
            var baseXaml = GenerateXaml(c);
            // Insert Attestor section before closing </ResourceDictionary>
            var insertPoint = baseXaml.LastIndexOf("</ResourceDictionary>", StringComparison.Ordinal);
            if (insertPoint < 0) return baseXaml;

            bool isDark = IsColorDark(c.PrimaryBackground);
            var accentDim = isDark ? DarkenColor(c.Accent, 0.7) : LightenColor(c.Accent, 0.6);
            var accentMuted = isDark ? DarkenColor(c.Accent, 0.5) : LightenColor(c.Accent, 0.4);
            var secondaryText = isDark ? LightenColor(c.TextMuted, 0.4) : DarkenColor(c.TextMuted, 0.4);
            var disabledText = isDark ? DarkenColor(c.TextMuted, 0.7) : LightenColor(c.TextMuted, 0.6);
            var borderHover = isDark ? LightenColor(c.Border, 0.3) : DarkenColor(c.Border, 0.3);
            var overlayBg = $"#B0{StripHash(c.PrimaryBackground)}";

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("    <!-- ═══════════════════════ ATTESTOR BRUSH KEYS ═══════════════════════ -->");
            sb.AppendLine();
            sb.AppendLine($"    <Color x:Key=\"PrimaryBackground\">{c.PrimaryBackground}</Color>");
            sb.AppendLine($"    <Color x:Key=\"SecondaryBackground\">{c.SecondaryBackground}</Color>");
            sb.AppendLine($"    <Color x:Key=\"TertiaryBackground\">{MixColor(c.SecondaryBackground, c.SurfaceBackground, 0.5)}</Color>");
            sb.AppendLine($"    <Color x:Key=\"SurfaceBackground\">{c.SurfaceBackground}</Color>");
            sb.AppendLine();
            sb.AppendLine($"    <Color x:Key=\"AccentPrimary\">{c.Accent}</Color>");
            sb.AppendLine($"    <Color x:Key=\"AccentSecondary\">{c.AccentHover}</Color>");
            sb.AppendLine($"    <Color x:Key=\"AccentTertiary\">{accentDim}</Color>");
            sb.AppendLine($"    <Color x:Key=\"AccentMuted\">{accentMuted}</Color>");
            sb.AppendLine();
            sb.AppendLine($"    <Color x:Key=\"SuccessColor\">{c.Success}</Color>");
            sb.AppendLine($"    <Color x:Key=\"WarningColor\">{c.Warning}</Color>");
            sb.AppendLine($"    <Color x:Key=\"ErrorColor\">{c.Error}</Color>");
            sb.AppendLine($"    <Color x:Key=\"InfoColor\">#60A5FA</Color>");
            sb.AppendLine($"    <Color x:Key=\"UrgentColor\">#FB923C</Color>");
            sb.AppendLine();
            sb.AppendLine($"    <Color x:Key=\"TextPrimary\">{c.TextPrimary}</Color>");
            sb.AppendLine($"    <Color x:Key=\"TextSecondary\">{secondaryText}</Color>");
            sb.AppendLine($"    <Color x:Key=\"TextMuted\">{c.TextMuted}</Color>");
            sb.AppendLine($"    <Color x:Key=\"TextDisabled\">{disabledText}</Color>");
            sb.AppendLine();
            sb.AppendLine($"    <Color x:Key=\"BorderDefault\">{c.Border}</Color>");
            sb.AppendLine($"    <Color x:Key=\"BorderHover\">{borderHover}</Color>");
            sb.AppendLine($"    <Color x:Key=\"BorderFocus\">{c.Accent}</Color>");
            sb.AppendLine();
            sb.AppendLine($"    <Color x:Key=\"ShadowLight\">#20000000</Color>");
            sb.AppendLine($"    <Color x:Key=\"ShadowMedium\">#40000000</Color>");
            sb.AppendLine($"    <Color x:Key=\"ShadowDark\">#60000000</Color>");
            sb.AppendLine();
            sb.AppendLine($"    <Color x:Key=\"OverlayBackground\">{overlayBg}</Color>");
            sb.AppendLine();
            sb.AppendLine($"    <SolidColorBrush x:Key=\"PrimaryBackgroundBrush\" Color=\"{c.PrimaryBackground}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"SecondaryBackgroundBrush\" Color=\"{c.SecondaryBackground}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"TertiaryBackgroundBrush\" Color=\"{MixColor(c.SecondaryBackground, c.SurfaceBackground, 0.5)}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"SurfaceBackgroundBrush\" Color=\"{c.SurfaceBackground}\"/>");
            sb.AppendLine();
            sb.AppendLine($"    <SolidColorBrush x:Key=\"AccentPrimaryBrush\" Color=\"{c.Accent}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"AccentSecondaryBrush\" Color=\"{c.AccentHover}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"AccentTertiaryBrush\" Color=\"{accentDim}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"AccentMutedBrush\" Color=\"{accentMuted}\"/>");
            sb.AppendLine();
            sb.AppendLine($"    <SolidColorBrush x:Key=\"SuccessBrush\" Color=\"{c.Success}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"WarningBrush\" Color=\"{c.Warning}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"ErrorBrush\" Color=\"{c.Error}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"InfoBrush\" Color=\"#60A5FA\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"UrgentBrush\" Color=\"#FB923C\"/>");
            sb.AppendLine();
            sb.AppendLine($"    <SolidColorBrush x:Key=\"TextPrimaryBrush\" Color=\"{c.TextPrimary}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"TextSecondaryBrush\" Color=\"{secondaryText}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"TextMutedBrush\" Color=\"{c.TextMuted}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"TextDisabledBrush\" Color=\"{disabledText}\"/>");
            sb.AppendLine();
            sb.AppendLine($"    <SolidColorBrush x:Key=\"BorderDefaultBrush\" Color=\"{c.Border}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"BorderHoverBrush\" Color=\"{borderHover}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"BorderFocusBrush\" Color=\"{c.Accent}\"/>");
            sb.AppendLine();
            sb.AppendLine($"    <SolidColorBrush x:Key=\"OverlayBackgroundBrush\" Color=\"{overlayBg}\"/>");

            var result = baseXaml.Insert(insertPoint, sb.ToString());
            return result;
        }

        // ──── Color helpers ────

        private static string SanitizeId(string name) =>
            new string(name.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());

        private static string EscapeXml(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        private static string StripHash(string hex) =>
            hex.StartsWith("#") ? hex.Substring(1) : hex;

        private static bool IsColorDark(string hex)
        {
            ParseRgb(hex, out int r, out int g, out int b);
            double luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
            return luminance < 0.5;
        }

        private static void ParseRgb(string hex, out int r, out int g, out int b)
        {
            hex = StripHash(hex);
            // Skip alpha if 8-char hex
            if (hex.Length == 8) hex = hex.Substring(2);
            r = Convert.ToInt32(hex.Substring(0, 2), 16);
            g = Convert.ToInt32(hex.Substring(2, 2), 16);
            b = Convert.ToInt32(hex.Substring(4, 2), 16);
        }

        private static string ToHex(int r, int g, int b) =>
            $"#{Clamp(r):X2}{Clamp(g):X2}{Clamp(b):X2}";

        private static int Clamp(int v) => Math.Max(0, Math.Min(255, v));

        private static string LightenColor(string hex, double factor)
        {
            ParseRgb(hex, out int r, out int g, out int b);
            r = (int)(r + (255 - r) * factor);
            g = (int)(g + (255 - g) * factor);
            b = (int)(b + (255 - b) * factor);
            return ToHex(r, g, b);
        }

        private static string DarkenColor(string hex, double factor)
        {
            ParseRgb(hex, out int r, out int g, out int b);
            r = (int)(r * factor);
            g = (int)(g * factor);
            b = (int)(b * factor);
            return ToHex(r, g, b);
        }

        private static string MixColor(string hex1, string hex2, double factor)
        {
            ParseRgb(hex1, out int r1, out int g1, out int b1);
            ParseRgb(hex2, out int r2, out int g2, out int b2);
            int r = (int)(r1 * (1 - factor) + r2 * factor);
            int g = (int)(g1 * (1 - factor) + g2 * factor);
            int b = (int)(b1 * (1 - factor) + b2 * factor);
            return ToHex(r, g, b);
        }

        /// <summary>
        /// Lists all custom theme files from the custom themes directory.
        /// Returns a list of (themeId, displayName, filePath) tuples.
        /// </summary>
        public static List<(string Id, string DisplayName, string FilePath)> DiscoverCustomThemes()
        {
            var results = new List<(string, string, string)>();
            var dir = GetCustomThemesDir();
            if (!Directory.Exists(dir)) return results;

            foreach (var file in Directory.GetFiles(dir, "Theme.Custom_*.axaml"))
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                // Parse "Theme.Custom_MyThemeName" -> id="Custom_MyThemeName", display="MyThemeName"
                var id = fileName.Replace("Theme.", "");
                var displayName = id.Replace("Custom_", "").Replace("_", " ");
                results.Add((id, displayName, file));
            }

            return results;
        }

        /// <summary>
        /// Deletes a custom theme file.
        /// </summary>
        public static bool DeleteCustomTheme(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    return true;
                }
            }
            catch { /* ignore */ }
            return false;
        }
    }
}
