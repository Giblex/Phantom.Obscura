using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services
{

    public sealed class PasswordHealthService
    {

        /// <summary>
        /// Looks up a password hash against Have I Been Pwned using k-anonymity.
        /// The caller passes the first 5 hex chars of the SHA-1 hash (sent to the API)
        /// and the remaining 35-char suffix (compared locally). The implementation must
        /// fetch the range for <paramref name="hashPrefix"/> and return the breach count
        /// for the line whose suffix equals <paramref name="hashSuffix"/>, or 0 if absent.
        /// Only the 5-char prefix is ever transmitted.
        /// </summary>
        public delegate Task<int> HibpLookupAsync(string hashPrefix, string hashSuffix);

        private static readonly string[] CommonWeakTokens =
        {
            "password", "passw", "secret", "admin", "login", "welcome",
            "qwerty", "letmein", "iloveyou", "monkey", "dragon", "vault", "phantom"
        };

        private readonly HibpLookupAsync? _hibpLookup;
        private readonly bool _checkBreaches;

        public PasswordHealthService(bool checkBreaches = false, HibpLookupAsync? hibpLookup = null)
        {
            _checkBreaches = checkBreaches;
            _hibpLookup = hibpLookup;
        }

        public async Task<PasswordHealthReport> AnalyzeAsync(IEnumerable<Credential> credentials, double entropyThreshold = 40.0, int reuseThreshold = 2, int ageThreshold = 365)
        {
            var list = credentials.ToList();
            var report = new PasswordHealthReport { TotalCredentials = list.Count };
            if (list.Count == 0) return report;

            double totalEntropy = 0;
            var passwordCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var cred in list)
            {
                // Use guess-resistance (charset entropy capped by Shannon, minus penalties
                // for dictionary words, sequences and repeats) rather than raw Shannon
                // entropy, which only measures character variety and rates "Password1!"
                // as strong.
                double entropy = EstimateStrengthBits(cred.Password);
                totalEntropy += entropy;
                if (entropy < entropyThreshold)
                {
                    report.WeakCount++;
                    report.WeakTitles.Add(cred.Title);
                }
                if ((DateTimeOffset.UtcNow - cred.LastUpdatedUtc).TotalDays > ageThreshold)
                {
                    report.OldCount++;
                    report.OldTitles.Add(cred.Title);
                }

                if (!passwordCounts.TryAdd(cred.Password, 1))
                {
                    passwordCounts[cred.Password]++;
                }
            }
            report.AverageEntropy = totalEntropy / list.Count;

            foreach (var kvp in passwordCounts)
            {
                if (kvp.Value >= reuseThreshold)
                {
                    report.ReusedCount++;

                    report.ReusedTitles.AddRange(list.Where(c => c.Password == kvp.Key).Select(c => c.Title));
                }
            }

            if (_checkBreaches && _hibpLookup != null)
            {
                report.BreachCheckPerformed = true;
                foreach (var cred in list)
                {
                    if (string.IsNullOrEmpty(cred.Password)) continue;
                    if (await IsBreachedAsync(cred.Password).ConfigureAwait(false))
                    {
                        report.BreachedCount++;
                        report.BreachedTitles.Add(cred.Title);

                        // A breached password is also weak regardless of its entropy.
                        if (!report.WeakTitles.Contains(cred.Title))
                        {
                            report.WeakTitles.Add(cred.Title);
                            report.WeakCount++;
                        }
                    }
                }
            }
            return report;
        }

        public static double ComputeEntropy(string password)
        {
            if (string.IsNullOrEmpty(password)) return 0.0;
            var freq = new Dictionary<char, double>();
            foreach (char c in password)
            {
                freq[c] = (freq.TryGetValue(c, out double count) ? count : 0) + 1;
            }
            double entropy = 0.0;
            int len = password.Length;
            foreach (double count in freq.Values)
            {
                double p = count / len;
                entropy -= p * Math.Log2(p);
            }

            return entropy * len;
        }

        /// <summary>
        /// Estimates password guess-resistance in bits. Takes the smaller of the
        /// charset-based entropy (length × log2(alphabet)) and the Shannon entropy of the
        /// characters, then subtracts penalties for dictionary tokens, sequential runs,
        /// repeated runs and low character diversity. This is a lightweight zxcvbn-style
        /// heuristic — far more honest than raw Shannon entropy for weak-password detection.
        /// </summary>
        public static double EstimateStrengthBits(string password)
        {
            if (string.IsNullOrEmpty(password)) return 0.0;

            bool hasLower = password.Any(char.IsLower);
            bool hasUpper = password.Any(char.IsUpper);
            bool hasDigit = password.Any(char.IsDigit);
            bool hasSymbol = password.Any(c => !char.IsLetterOrDigit(c));

            int charset = 0;
            if (hasLower) charset += 26;
            if (hasUpper) charset += 26;
            if (hasDigit) charset += 10;
            if (hasSymbol) charset += 32;

            double charsetBits = charset > 0 ? password.Length * Math.Log2(charset) : 0;
            double shannonBits = ComputeEntropy(password);
            double effective = Math.Min(charsetBits, shannonBits);

            double penalty = 0;
            string lower = password.ToLowerInvariant();
            foreach (var token in CommonWeakTokens)
            {
                if (lower.Contains(token, StringComparison.Ordinal))
                {
                    penalty += 16;
                    break;
                }
            }
            if (HasSequentialRun(lower)) penalty += 10;
            if (HasRepeatedRun(password)) penalty += 8;

            int unique = password.Distinct().Count();
            double uniqueRatio = (double)unique / password.Length;
            if (uniqueRatio < 0.5) penalty += 12;
            else if (uniqueRatio < 0.7) penalty += 6;

            return Math.Max(0, effective - penalty);
        }

        private static bool HasSequentialRun(string value)
        {
            if (value.Length < 3) return false;
            for (int i = 0; i <= value.Length - 3; i++)
            {
                int s1 = value[i + 1] - value[i];
                int s2 = value[i + 2] - value[i + 1];
                if ((s1 == 1 && s2 == 1) || (s1 == -1 && s2 == -1)) return true;
            }
            return false;
        }

        private static bool HasRepeatedRun(string value)
        {
            if (value.Length < 3) return false;
            int run = 1;
            for (int i = 1; i < value.Length; i++)
            {
                if (value[i] == value[i - 1])
                {
                    if (++run >= 3) return true;
                }
                else
                {
                    run = 1;
                }
            }
            return false;
        }

        private async Task<bool> IsBreachedAsync(string password)
        {
            if (_hibpLookup == null) return false;

            using var sha1 = SHA1.Create();
            byte[] hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(password));
            string hashHex = Convert.ToHexString(hashBytes); // upper-case, matches HIBP
            string prefix = hashHex.Substring(0, 5);
            string suffix = hashHex.Substring(5);

            // The lookup transmits only the prefix and compares the 35-char suffix locally,
            // returning the breach count for the exact hash (0 when not found in the range).
            int matches = await _hibpLookup(prefix, suffix).ConfigureAwait(false);
            return matches > 0;
        }
    }
}

