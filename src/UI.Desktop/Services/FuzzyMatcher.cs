using System;
using System.Collections.Generic;
using System.Linq;

namespace PhantomVault.UI.Services
{

    public static class FuzzyMatcher
    {

        public static int CalculateScore(string pattern, string text)
        {
            if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(text))
                return 0;

            pattern = pattern.ToLowerInvariant();
            text = text.ToLowerInvariant();

            if (text == pattern)
                return 100;

            if (text.Contains(pattern))
                return 90;

            int score = 0;
            int patternIndex = 0;
            int consecutiveMatches = 0;
            bool previousMatch = false;

            for (int i = 0; i < text.Length && patternIndex < pattern.Length; i++)
            {
                if (text[i] == pattern[patternIndex])
                {
                    score += previousMatch ? 5 : 3;

                    if (previousMatch)
                        consecutiveMatches++;

                    patternIndex++;
                    previousMatch = true;
                }
                else
                {
                    previousMatch = false;
                    consecutiveMatches = 0;
                }
            }

            if (patternIndex == pattern.Length)
            {

                if (text.StartsWith(pattern[0].ToString()))
                    score += 10;

                int lengthDiff = Math.Abs(text.Length - pattern.Length);
                score += Math.Max(0, 20 - lengthDiff * 2);

                return Math.Min(85, score);
            }

            return 0;
        }

        public static bool IsMatch(string pattern, string text, int threshold = 30)
        {
            return CalculateScore(pattern, text) >= threshold;
        }

        public static List<int> GetMatchIndices(string pattern, string text)
        {
            var indices = new List<int>();

            if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(text))
                return indices;

            pattern = pattern.ToLowerInvariant();
            text = text.ToLowerInvariant();

            int patternIndex = 0;

            for (int i = 0; i < text.Length && patternIndex < pattern.Length; i++)
            {
                if (text[i] == pattern[patternIndex])
                {
                    indices.Add(i);
                    patternIndex++;
                }
            }

            return indices;
        }
    }

    public class SearchResult<T>
    {
        public T Item { get; set; }
        public int Score { get; set; }
        public List<int> MatchedIndices { get; set; } = new();
        public string MatchedField { get; set; } = string.Empty;

        public SearchResult(T item)
        {
            Item = item ?? throw new ArgumentNullException(nameof(item));
        }
    }
}

