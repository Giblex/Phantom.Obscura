// Partial class — Password Strength Tester section of VaultViewModel
using System;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using PhantomVault.UI.Helpers;

namespace PhantomVault.UI.ViewModels
{
    public sealed partial class VaultViewModel
    {
        #region Password Strength Tester

        private void UpdatePasswordTestResults()
        {
            if (string.IsNullOrEmpty(_testPassword))
            {
                TestPasswordScore = 0;
                TestPasswordStrengthLabel = "";
                TestPasswordStrengthColor = "#808080";
                SuggestedPassword = "";
                SimilarityPercentage = 0;
                return;
            }

            var assessment = PasswordStrengthEvaluator.Evaluate(_testPassword);
            TestPasswordScore = assessment.Score;
            TestPasswordStrengthLabel = assessment.Label;
            TestPasswordStrengthColor = assessment.ColorHex;
            GenerateSuggestedPassword();
        }

        private int CalculatePasswordScore(string password)
        {
            return PasswordStrengthEvaluator.Evaluate(password).Score;
        }

        private void GenerateSuggestedPassword()
        {
            if (string.IsNullOrEmpty(_testPassword))
                return;

            string suggested = PasswordStrengthEvaluator.GenerateSuggestedPassword(_testPassword);
            SuggestedPassword = suggested;
            SimilarityPercentage = string.IsNullOrEmpty(suggested)
                ? 0
                : CalculateSimilarity(_testPassword, suggested);
        }

        private int CalculateSimilarity(string original, string suggested)
        {
            if (string.IsNullOrEmpty(original)) return 0;

            int matches = 0;
            for (int i = 0; i < Math.Min(original.Length, suggested.Length); i++)
            {
                if (char.ToLower(original[i]) == char.ToLower(suggested[i]))
                    matches++;
            }

            return (matches * 100) / Math.Max(original.Length, suggested.Length);
        }

        private void CopySuggestedPassword()
        {
            if (string.IsNullOrEmpty(_suggestedPassword))
                return;

            try
            {
                if (_ownerWindow?.Clipboard is IClipboard clipboard)
                {
                    Dispatcher.UIThread.Post(async () =>
                    {
                        await clipboard.SetTextAsync(_suggestedPassword);
                        StatusMessage = "✓ Suggested password copied to clipboard";
                    });
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to copy: {ex.Message}";
            }
        }

        #endregion
    }
}
