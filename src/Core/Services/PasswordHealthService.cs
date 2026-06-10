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

        public delegate Task<int> HibpLookupAsync(string hashPrefix);

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
                foreach (var cred in list)
                {
                    if (await IsBreachedAsync(cred.Password).ConfigureAwait(false))
                    {

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

        private async Task<bool> IsBreachedAsync(string password)
        {
            if (_hibpLookup == null) return false;

            using var sha1 = SHA1.Create();
            byte[] hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(password));
            string hashHex = BitConverter.ToString(hashBytes).Replace("-", string.Empty);
            string prefix = hashHex.Substring(0, 5);
            int matches = await _hibpLookup(prefix).ConfigureAwait(false);
            return matches > 0;
        }
    }
}

