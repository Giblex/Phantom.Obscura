using System;
using System.Linq;
using Avalonia.Media;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Centralizes password strength bucketing so generator and editors stay consistent.
    /// Evaluates length, character diversity, and common patterns.
    /// </summary>
    internal static class PasswordStrengthHelper
    {
        public const string PasswordFlagFieldKey = "pv_password_flag";

        internal static PasswordStrengthInfo Evaluate(string? password)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                return PasswordStrengthInfo.Empty;
            }

            int length = password.Length;

            // Calculate character class diversity (0-4 points)
            int diversity = 0;
            if (password.Any(char.IsUpper)) diversity++;
            if (password.Any(char.IsLower)) diversity++;
            if (password.Any(char.IsDigit)) diversity++;
            if (password.Any(c => !char.IsLetterOrDigit(c))) diversity++;

            // Check for common weak patterns
            bool hasCommonPattern = HasCommonPattern(password);

            // Calculate effective score (length * diversity factor, penalized for patterns)
            double diversityMultiplier = diversity switch
            {
                4 => 1.0,
                3 => 0.85,
                2 => 0.65,
                1 => 0.4,
                _ => 0.2
            };

            double effectiveScore = length * diversityMultiplier;
            if (hasCommonPattern)
            {
                effectiveScore *= 0.5; // 50% penalty for common patterns
            }

            // effectiveScore < 6 = Weak
            if (effectiveScore < 6)
            {
                return PasswordStrengthInfo.Create(
                    label: "Weak",
                    progress: 10,
                    colorHex: "#FFD64550",
                    shouldShowFlag: true);
            }

            // effectiveScore 6-12 = OK
            if (effectiveScore < 12)
            {
                return PasswordStrengthInfo.Create(
                    label: "OK",
                    progress: 30,
                    colorHex: "#FFFFB74D",
                    shouldShowFlag: true);
            }

            // effectiveScore 12-20 = Good
            if (effectiveScore < 20)
            {
                return PasswordStrengthInfo.Create(
                    label: "Good",
                    progress: 55,
                    colorHex: "#FF4EC9B0");
            }

            // effectiveScore 20-32 = Great
            if (effectiveScore < 32)
            {
                return PasswordStrengthInfo.Create(
                    label: "Great",
                    progress: 80,
                    colorHex: "#FF6B8CAE");
            }

            // effectiveScore 32+ = Phantom Strength
            return PasswordStrengthInfo.Create(
                label: "Phantom Strength",
                progress: 100,
                colorHex: "#FF8E44AD");
        }

        /// <summary>
        /// Detects common weak patterns like keyboard sequences, repeated chars, etc.
        /// </summary>
        private static bool HasCommonPattern(string password)
        {
            var lower = password.ToLowerInvariant();

            // Check for common keyboard patterns
            string[] keyboardPatterns = { "qwerty", "asdfgh", "zxcvbn", "123456", "654321", "abcdef", "password", "letmein", "admin", "welcome" };
            foreach (var pattern in keyboardPatterns)
            {
                if (lower.Contains(pattern))
                    return true;
            }

            // Check for 4+ consecutive repeated characters
            for (int i = 0; i < password.Length - 3; i++)
            {
                if (password[i] == password[i + 1] && 
                    password[i] == password[i + 2] && 
                    password[i] == password[i + 3])
                {
                    return true;
                }
            }

            // Check for sequential characters (e.g., "abcd" or "1234")
            for (int i = 0; i < password.Length - 3; i++)
            {
                char c1 = password[i], c2 = password[i + 1], c3 = password[i + 2], c4 = password[i + 3];
                if ((c2 - c1 == 1 && c3 - c2 == 1 && c4 - c3 == 1) ||
                    (c1 - c2 == 1 && c2 - c3 == 1 && c3 - c4 == 1))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Attempts to create a <see cref="PasswordStrengthInfo"/> for a stored flag label so UI elements
        /// can render consistent colors without recomputing strength.
        /// </summary>
        internal static bool TryGetInfoForFlag(string? flagValue, out PasswordStrengthInfo info)
        {
            if (string.IsNullOrWhiteSpace(flagValue))
            {
                info = PasswordStrengthInfo.Empty;
                return false;
            }

            switch (flagValue.Trim())
            {
                case "Weak":
                    info = PasswordStrengthInfo.Create("Weak", 10, "#FFD64550", shouldShowFlag: true);
                    return true;
                case "OK":
                    info = PasswordStrengthInfo.Create("OK", 30, "#FFFFB74D", shouldShowFlag: true);
                    return true;
                case "Good":
                    info = PasswordStrengthInfo.Create("Good", 55, "#FF4EC9B0");
                    return true;
                case "Great":
                    info = PasswordStrengthInfo.Create("Great", 80, "#FF6B8CAE");
                    return true;
                case "Phantom Strength":
                    info = PasswordStrengthInfo.Create("Phantom Strength", 100, "#FF8E44AD");
                    return true;
                default:
                    info = PasswordStrengthInfo.Empty;
                    return false;
            }
        }
    }

    internal readonly record struct PasswordStrengthInfo(
        string Label,
        int Progress,
        string ColorHex,
        bool ShouldShowFlag,
        string FlagText)
    {
        public static PasswordStrengthInfo Empty { get; } = new(
            Label: string.Empty,
            Progress: 0,
            ColorHex: "#FF6B8CAE",
            ShouldShowFlag: false,
            FlagText: string.Empty);

        public bool HasValue => !string.IsNullOrEmpty(Label);

        public static PasswordStrengthInfo Create(
            string label,
            int progress,
            string colorHex,
            bool shouldShowFlag = false) => new(
                label,
                progress,
                colorHex,
                shouldShowFlag,
                label);

        public ISolidColorBrush CreateBrush() => new SolidColorBrush(Color.Parse(ColorHex));

        public ISolidColorBrush CreateBadgeBrush()
        {
            var color = Color.Parse(ColorHex);
            var alpha = (byte)Math.Clamp((int)(color.A * 0.55), 40, 255);
            var adjusted = Color.FromArgb(alpha, color.R, color.G, color.B);
            return new SolidColorBrush(adjusted);
        }
    }
}
