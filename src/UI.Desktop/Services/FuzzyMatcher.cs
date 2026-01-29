using System;
using System.Collections.Generic;
using System.Linq;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Provides fuzzy string matching for search functionality
    /// </summary>
    public static class FuzzyMatcher
    {
        /// <summary>
        /// Calculates a fuzzy match score between pattern and text (0-100)
        /// Higher score indicates better match
        /// </summary>
        public static int CalculateScore(string pattern, string text)
        {
            if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(text))
                return 0;

            pattern = pattern.ToLowerInvariant();
            text = text.ToLowerInvariant();

            // Exact match gets perfect score
            if (text == pattern)
                return 100;

            // Contains gets high score
            if (text.Contains(pattern))
                return 90;

            // Calculate fuzzy match score
            int score = 0;
            int patternIndex = 0;
            int consecutiveMatches = 0;
            bool previousMatch = false;

            for (int i = 0; i < text.Length && patternIndex < pattern.Length; i++)
            {
                if (text[i] == pattern[patternIndex])
                {
                    score += previousMatch ? 5 : 3; // Bonus for consecutive matches
                    
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

            // All characters matched
            if (patternIndex == pattern.Length)
            {
                // Bonus for matching at start
                if (text.StartsWith(pattern[0].ToString()))
                    score += 10;

                // Bonus for length similarity
                int lengthDiff = Math.Abs(text.Length - pattern.Length);
                score += Math.Max(0, 20 - lengthDiff * 2);

                return Math.Min(85, score);
            }

            return 0; // Not all pattern characters found
        }

        /// <summary>
        /// Checks if text matches pattern with fuzzy logic
        /// </summary>
        public static bool IsMatch(string pattern, string text, int threshold = 30)
        {
            return CalculateScore(pattern, text) >= threshold;
        }

        /// <summary>
        /// Gets character positions that match the pattern (for highlighting)
        /// </summary>
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

    /// <summary>
    /// Search result with score and matched indices for highlighting
    /// </summary>
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
