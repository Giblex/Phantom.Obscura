using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using PhantomVault.Core.Services;

namespace PhantomVault.UI.Helpers
{
    internal static class PasswordStrengthEvaluator
    {
        private const int MinimumGeneratedLength = 20;
        private const int MaximumGeneratedLength = 32;
        private const string LowercaseCharacters = "abcdefghijkmnopqrstuvwxyz";
        private const string UppercaseCharacters = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        private const string DigitCharacters = "23456789";
        private const string SymbolCharacters = "!@#$%^&*()-_=+[]{}:;,.?";

        private static readonly string[] CommonTokens =
        {
            "password",
            "pass",
            "secure",
            "secret",
            "admin",
            "login",
            "welcome",
            "vault",
            "phantom",
            "qwerty",
            "letmein",
            "github",
            "discord",
            "slack"
        };

        public static PasswordStrengthAssessment Evaluate(string? password)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                return new PasswordStrengthAssessment(
                    0,
                    0,
                    "Very Weak",
                    "#F44336",
                    true,
                    true,
                    3);
            }

            bool hasLower = password.Any(char.IsLower);
            bool hasUpper = password.Any(char.IsUpper);
            bool hasDigit = password.Any(char.IsDigit);
            bool hasSpecial = password.Any(ch => !char.IsLetterOrDigit(ch));
            int charsetSize = GetCharacterSetSize(hasLower, hasUpper, hasDigit, hasSpecial);
            double shannonEntropyBits = PasswordHealthService.ComputeEntropy(password);
            double charsetEntropyBits = charsetSize > 0 ? password.Length * Math.Log2(charsetSize) : 0;
            double effectiveEntropyBits = Math.Min(shannonEntropyBits, charsetEntropyBits);
            int uniqueCharacterCount = password.Distinct().Count();
            double uniqueCharacterRatio = password.Length == 0
                ? 0
                : (double)uniqueCharacterCount / password.Length;

            int lengthScore = password.Length switch
            {
                >= 24 => 25,
                >= 20 => 22,
                >= 16 => 18,
                >= 12 => 14,
                >= 10 => 10,
                >= 8 => 6,
                _ => 0
            };

            int varietyScore = 0;
            if (hasLower) varietyScore += 5;
            if (hasUpper) varietyScore += 5;
            if (hasDigit) varietyScore += 5;
            if (hasSpecial) varietyScore += 8;
            if (hasLower && hasUpper && hasDigit && hasSpecial) varietyScore += 5;

            int entropyScore = (int)Math.Round(Math.Clamp(effectiveEntropyBits / 2.0, 0, 45));
            int unpredictabilityBonus = 0;
            if (password.Length >= 18 && uniqueCharacterRatio >= 0.85)
            {
                unpredictabilityBonus += 7;
            }
            else if (password.Length >= 14 && uniqueCharacterRatio >= 0.72)
            {
                unpredictabilityBonus += 3;
            }

            int patternPenalty = CalculatePatternPenalty(password, uniqueCharacterRatio, hasSpecial);
            int score = Math.Clamp(lengthScore + varietyScore + entropyScore + unpredictabilityBonus - patternPenalty, 0, 100);
            bool isWeak = score < 60 || effectiveEntropyBits < 45 || password.Length < 12 || patternPenalty >= 18;
            bool needsSuggestion = score < 85 || effectiveEntropyBits < 60 || patternPenalty >= 10 || password.Length < 16;

            return new PasswordStrengthAssessment(
                score,
                effectiveEntropyBits,
                GetStrengthLabel(score),
                GetStrengthColor(score),
                isWeak,
                needsSuggestion,
                GetSeverity(score));
        }

        public static string GenerateSuggestedPassword(string? password)
        {
            var assessment = Evaluate(password);
            if (!assessment.NeedsSuggestion)
            {
                return string.Empty;
            }

            int requestedLength = Math.Clamp(
                Math.Max(MinimumGeneratedLength, (password?.Length ?? 0) + 6),
                MinimumGeneratedLength,
                MaximumGeneratedLength);

            return GenerateRandomPassword(requestedLength);
        }

        public static string GenerateRandomPassword(int requestedLength)
        {
            int length = Math.Clamp(requestedLength, MinimumGeneratedLength, MaximumGeneratedLength);
            var passwordCharacters = new List<char>(length)
            {
                GetRandomCharacter(LowercaseCharacters),
                GetRandomCharacter(UppercaseCharacters),
                GetRandomCharacter(DigitCharacters),
                GetRandomCharacter(SymbolCharacters)
            };

            string allCharacters = LowercaseCharacters + UppercaseCharacters + DigitCharacters + SymbolCharacters;
            while (passwordCharacters.Count < length)
            {
                passwordCharacters.Add(GetRandomCharacter(allCharacters));
            }

            Shuffle(passwordCharacters);
            return new string(passwordCharacters.ToArray());
        }

        private static int GetCharacterSetSize(bool hasLower, bool hasUpper, bool hasDigit, bool hasSpecial)
        {
            int charsetSize = 0;
            if (hasLower) charsetSize += 26;
            if (hasUpper) charsetSize += 26;
            if (hasDigit) charsetSize += 10;
            if (hasSpecial) charsetSize += 32;
            return charsetSize;
        }

        private static int CalculatePatternPenalty(string password, double uniqueCharacterRatio, bool hasSpecial)
        {
            int penalty = 0;
            string normalized = password.Trim().ToLowerInvariant();

            foreach (string token in CommonTokens)
            {
                if (normalized.Contains(token, StringComparison.Ordinal))
                {
                    penalty += token.Length >= 6 ? 18 : 12;
                    break;
                }
            }

            if (HasSequentialRun(normalized))
            {
                penalty += 12;
            }

            if (HasRepeatedRun(password))
            {
                penalty += 8;
            }

            if (uniqueCharacterRatio < 0.65)
            {
                penalty += 10;
            }
            else if (uniqueCharacterRatio < 0.8)
            {
                penalty += 4;
            }

            if (LooksLikeWordWithSuffix(normalized))
            {
                penalty += 14;
            }

            if (hasSpecial && IsTrailingSpecialOnly(password))
            {
                penalty += 6;
            }

            return penalty;
        }

        private static bool HasSequentialRun(string password)
        {
            if (password.Length < 3)
            {
                return false;
            }

            for (int index = 0; index <= password.Length - 3; index++)
            {
                int stepOne = password[index + 1] - password[index];
                int stepTwo = password[index + 2] - password[index + 1];
                if ((stepOne == 1 && stepTwo == 1) || (stepOne == -1 && stepTwo == -1))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasRepeatedRun(string password)
        {
            if (password.Length < 3)
            {
                return false;
            }

            int runLength = 1;
            for (int index = 1; index < password.Length; index++)
            {
                if (password[index] == password[index - 1])
                {
                    runLength++;
                    if (runLength >= 3)
                    {
                        return true;
                    }
                }
                else
                {
                    runLength = 1;
                }
            }

            return false;
        }

        private static bool LooksLikeWordWithSuffix(string normalizedPassword)
        {
            if (normalizedPassword.Length < 8)
            {
                return false;
            }

            int splitIndex = normalizedPassword.TakeWhile(char.IsLetter).Count();
            if (splitIndex < 5 || splitIndex >= normalizedPassword.Length)
            {
                return false;
            }

            string suffix = normalizedPassword.Substring(splitIndex);
            return suffix.All(ch => char.IsDigit(ch) || !char.IsLetterOrDigit(ch));
        }

        private static bool IsTrailingSpecialOnly(string password)
        {
            int specialIndex = password.IndexOfAny(SymbolCharacters.ToCharArray());
            return specialIndex >= 0 && specialIndex == password.Length - 1;
        }

        private static char GetRandomCharacter(string characters)
        {
            return characters[RandomNumberGenerator.GetInt32(characters.Length)];
        }

        private static void Shuffle(IList<char> characters)
        {
            for (int index = characters.Count - 1; index > 0; index--)
            {
                int swapIndex = RandomNumberGenerator.GetInt32(index + 1);
                (characters[index], characters[swapIndex]) = (characters[swapIndex], characters[index]);
            }
        }

        private static string GetStrengthLabel(int score) => score switch
        {
            >= 90 => "Very Strong",
            >= 75 => "Strong",
            >= 55 => "Fair",
            >= 35 => "Weak",
            _ => "Very Weak"
        };

        private static string GetStrengthColor(int score) => score switch
        {
            >= 90 => "#22C55E",
            >= 75 => "#84CC16",
            >= 55 => "#EAB308",
            >= 35 => "#F97316",
            _ => "#EF4444"
        };

        private static int GetSeverity(int score) => score switch
        {
            < 35 => 3,
            < 55 => 2,
            < 75 => 1,
            _ => 0
        };
    }

    internal sealed record PasswordStrengthAssessment(
        int Score,
        double EffectiveEntropyBits,
        string Label,
        string ColorHex,
        bool IsWeak,
        bool NeedsSuggestion,
        int Severity);
}
