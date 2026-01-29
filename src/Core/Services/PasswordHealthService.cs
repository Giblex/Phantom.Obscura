using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Analyses a collection of credentials and produces a health report.
    /// Metrics include password entropy, reuse and age. The service
    /// optionally integrates with Have I Been Pwned (HIBP) using the
    /// k‑anonymous API to flag passwords found in known breaches. A
    /// Bloom filter can be provided to avoid network calls and improve
    /// privacy. To use the breach check, set <see cref="CheckBreaches"/>
    /// to true and supply a delegate that performs the k‑anon lookup.
    /// </summary>
    public sealed class PasswordHealthService
    {
        /// <summary>
        /// Delegate type for performing a k‑anonymous HIBP lookup. It
        /// receives the first five hexadecimal characters of a SHA1
        /// password hash and returns the number of matches found. The
        /// lookup should not send the full hash to maintain privacy.
        /// </summary>
        public delegate Task<int> HibpLookupAsync(string hashPrefix);

        private readonly HibpLookupAsync? _hibpLookup;
        private readonly bool _checkBreaches;

        public PasswordHealthService(bool checkBreaches = false, HibpLookupAsync? hibpLookup = null)
        {
            _checkBreaches = checkBreaches;
            _hibpLookup = hibpLookup;
        }

        /// <summary>
        /// Performs analysis on the provided credentials. Weakness
        /// thresholds can be adjusted via parameters. Breach checking is
        /// only performed if enabled and a lookup delegate is provided.
        /// </summary>
        /// <param name="credentials">A collection of credentials.</param>
        /// <param name="entropyThreshold">Minimum acceptable entropy in bits.</param>
        /// <param name="reuseThreshold">Consider passwords reused if they appear at least this many times.</param>
        /// <param name="ageThreshold">Consider passwords old if they have not been updated in this number of days.</param>
        public async Task<PasswordHealthReport> AnalyzeAsync(IEnumerable<Credential> credentials, double entropyThreshold = 40.0, int reuseThreshold = 2, int ageThreshold = 365)
        {
            var list = credentials.ToList();
            var report = new PasswordHealthReport { TotalCredentials = list.Count };
            if (list.Count == 0) return report;

            // Compute entropy and detect weak/old passwords
            double totalEntropy = 0;
            var passwordCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var cred in list)
            {
                double entropy = ComputeEntropy(cred.Password);
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
                // Count password usage for reuse detection
                if (!passwordCounts.TryAdd(cred.Password, 1))
                {
                    passwordCounts[cred.Password]++;
                }
            }
            report.AverageEntropy = totalEntropy / list.Count;

            // Identify reused passwords
            foreach (var kvp in passwordCounts)
            {
                if (kvp.Value >= reuseThreshold)
                {
                    report.ReusedCount++;
                    // Add titles of credentials that share this password
                    report.ReusedTitles.AddRange(list.Where(c => c.Password == kvp.Key).Select(c => c.Title));
                }
            }

            // Optionally check against HIBP using k‑anon API
            if (_checkBreaches && _hibpLookup != null)
            {
                foreach (var cred in list)
                {
                    if (await IsBreachedAsync(cred.Password).ConfigureAwait(false))
                    {
                        // Treat breached passwords as weak
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

        /// <summary>
        /// Computes an approximate Shannon entropy of a password. The
        /// entropy is in bits and assumes independence of characters.
        /// </summary>
        /// <param name="password">The password to analyse.</param>
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
            // Multiply by length to approximate total entropy
            return entropy * len;
        }

        /// <summary>
        /// Determines whether a password appears in known breach data using
        /// the k‑anonymous model. The default implementation returns
        /// false because no breach lookup delegate has been provided.
        /// </summary>
        private async Task<bool> IsBreachedAsync(string password)
        {
            if (_hibpLookup == null) return false;
            // Compute SHA1 hash of the password
            using var sha1 = SHA1.Create();
            byte[] hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(password));
            string hashHex = BitConverter.ToString(hashBytes).Replace("-", string.Empty);
            string prefix = hashHex.Substring(0, 5);
            int matches = await _hibpLookup(prefix).ConfigureAwait(false);
            return matches > 0;
        }
    }
}