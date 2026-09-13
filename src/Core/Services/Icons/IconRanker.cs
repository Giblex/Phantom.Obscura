using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PhantomVault.Core.Services.Icons
{
    /// <summary>What the icon picker was opened for; drives which icons rank first.</summary>
    /// <summary>What the icon picker is choosing for, used to rank its suggestions.</summary>
    /// <param name="Purpose">Browsing, a category icon, or an entry icon.</param>
    /// <param name="Name">The category name or the entry's title.</param>
    /// <param name="Url">The entry's website, when it has one.</param>
    /// <param name="Kind">The entry type (e.g. "CreditCard", "BankAccount"), so the picker can
    /// offer every logo of that kind (all banks for a card) rather than only title matches.</param>
    public sealed record IconPickContext(IconPickPurpose Purpose, string? Name = null, string? Url = null, string? Kind = null)
    {
        public static IconPickContext General { get; } = new(IconPickPurpose.General);
        public static IconPickContext ForCategory(string? name) => new(IconPickPurpose.Category, name);
        public static IconPickContext ForEntry(string? title, string? url, string? kind = null) => new(IconPickPurpose.Entry, title, url, kind);
    }

    public enum IconPickPurpose
    {
        /// <summary>Browsing the library; nothing is being assigned.</summary>
        General,

        /// <summary>Choosing a category icon: line icons first, matched to the category name.</summary>
        Category,

        /// <summary>Choosing an entry icon: logos first, matched to the entry's title and site.</summary>
        Entry
    }

    /// <summary>
    /// Closest-match ranking for the icon picker. Scores an icon name against a set of query terms:
    /// exact and prefix matches first, then containment, shared words, and near-misses by edit
    /// distance. Entry queries come from the title and the site's domain; category queries from
    /// the name, its singular form and a small synonym table ("Banking" also tries bank, money...).
    /// </summary>
    public static class IconRanker
    {
        private static readonly HashSet<string> CommonTlds = new(StringComparer.OrdinalIgnoreCase)
        {
            "com", "net", "org", "io", "co", "uk", "au", "nz", "ca", "de", "gov", "edu", "app", "dev", "ai", "me", "tv", "www"
        };

        private static readonly Dictionary<string, string[]> CategorySynonyms = new(StringComparer.Ordinal)
        {
            ["login"] = new[] { "key", "lock", "password" },
            ["password"] = new[] { "key", "lock" },
            ["bank"] = new[] { "money", "wallet", "card", "dollar" },
            ["banking"] = new[] { "bank", "money", "wallet", "card" },
            ["finance"] = new[] { "bank", "money", "wallet", "chart" },
            ["creditcard"] = new[] { "card", "wallet" },
            ["card"] = new[] { "wallet" },
            ["securenote"] = new[] { "note", "notepad", "book", "pen" },
            ["note"] = new[] { "notepad", "book", "pen", "document" },
            ["personal"] = new[] { "user", "person", "profile", "home" },
            ["wifi"] = new[] { "network", "internet", "router", "signal" },
            ["network"] = new[] { "wifi", "internet", "router" },
            ["id"] = new[] { "identity", "badge", "passport", "user" },
            ["identity"] = new[] { "id", "badge", "passport", "user" },
            ["email"] = new[] { "mail", "at", "envelope" },
            ["mail"] = new[] { "email", "at", "envelope" },
            ["social"] = new[] { "people", "chat", "users" },
            ["shopping"] = new[] { "basket", "cart", "bag", "shop" },
            ["work"] = new[] { "business", "briefcase", "office" },
            ["business"] = new[] { "briefcase", "office", "work" },
            ["travel"] = new[] { "plane", "car", "map", "luggage" },
            ["gaming"] = new[] { "game", "controller" },
            ["game"] = new[] { "gaming", "controller" },
            ["entertainment"] = new[] { "movie", "music", "tv", "play" },
            ["streaming"] = new[] { "movie", "tv", "play", "video" },
            ["crypto"] = new[] { "bitcoin", "coin", "wallet" },
            ["health"] = new[] { "heart", "medical" },
            ["education"] = new[] { "book", "school", "graduation" },
            ["school"] = new[] { "book", "education", "graduation" },
            ["development"] = new[] { "code", "server", "terminal" },
            ["server"] = new[] { "code", "terminal", "database" },
            ["pincode"] = new[] { "pin", "lock", "number" },
            ["apikey"] = new[] { "key", "code" },
            ["home"] = new[] { "house" },
            ["government"] = new[] { "building" },
            ["utility"] = new[] { "tool", "settings" },
        };

        /// <summary>Lower-case letters and digits only ("@" becomes "at"), for comparing names.</summary>
        public static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var sb = new StringBuilder(value.Length);
            foreach (var c in value.Replace("@", "at"))
            {
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>Words of a name, splitting on punctuation and camelCase ("GoogleDrive" → google, drive).</summary>
        public static IReadOnlyList<string> Tokens(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return Array.Empty<string>();
            var spaced = Regex.Replace(value.Replace("@", " at "), "([a-z0-9])([A-Z])", "$1 $2");
            return Regex.Split(spaced, "[^A-Za-z0-9]+")
                .Select(t => t.ToLowerInvariant())
                .Where(t => t.Length >= 2)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        public static IReadOnlyList<string> QueriesForEntry(string? title, string? url)
        {
            var queries = new List<string>();
            AddQuery(queries, title);
            foreach (var token in Tokens(title)) AddQuery(queries, token);

            var host = HostOf(url);
            if (host != null)
            {
                var labels = host.Split('.', StringSplitOptions.RemoveEmptyEntries)
                    .Where(l => !CommonTlds.Contains(l))
                    .ToList();
                // "mail.google.com": "google" (the registrable name) matters most, then "mail".
                if (labels.Count > 0) AddQuery(queries, labels[^1]);
                foreach (var label in labels) AddQuery(queries, label);
            }

            return queries;
        }

        public static IReadOnlyList<string> QueriesForCategory(string? name)
        {
            var queries = new List<string>();
            AddQuery(queries, name);

            foreach (var key in new[] { Normalize(name) }.Concat(Tokens(name)))
            {
                var singular = Singular(key);
                AddQuery(queries, singular);
                if (CategorySynonyms.TryGetValue(key, out var synonyms) ||
                    CategorySynonyms.TryGetValue(singular, out synonyms))
                {
                    foreach (var s in synonyms) AddQuery(queries, s);
                }
            }

            return queries;
        }

        /// <summary>Best score (0..1) of <paramref name="item"/> against any query.</summary>
        public static double Score(IconItem item, IReadOnlyList<string> queries)
        {
            if (queries.Count == 0 || item.MatchKey.Length == 0) return 0;

            var itemTokens = Tokens(item.Name);
            double best = 0;
            for (int i = 0; i < queries.Count; i++)
            {
                // Earlier queries (the name itself) outrank later ones (synonyms, sub-labels) slightly.
                double weight = 1.0 - Math.Min(i, 6) * 0.02;
                best = Math.Max(best, ScoreOne(item.MatchKey, itemTokens, queries[i]) * weight);
            }
            return best;
        }

        // Logos that belong to an entry type. "bank" matches a whole word or the end of one
        // (Chase Bank, CommBank, Citibank) but not the start of an unrelated word (Bankstown).
        // Brand names catch banks and card networks whose file names never say "bank".
        private static readonly string[] BankSuffixWords = { "bank", "banking" };

        private static readonly string[] BankBrands =
        {
            "anz", "nab", "westpac", "commbank", "commonwealth", "bankwest", "stgeorge", "macquarie",
            "suncorp", "bendigo", "ubank", "ing", "amp", "hsbc", "barclays", "lloyds", "natwest",
            "santander", "chase", "citi", "citibank", "wellsfargo", "capitalone", "revolut", "monzo",
            "starling", "wise", "creditunion", "heritage", "greatsouthern", "boq", "mebank", "rabobank",
            "tdbank", "usbank", "pnc", "schwab", "ally", "discover", "kiwibank", "asb", "bnz"
        };

        private static readonly string[] CardNetworks =
        {
            "visa", "mastercard", "amex", "americanexpress", "discover", "dinersclub", "jcb",
            "unionpay", "maestro", "eftpos", "paypal", "afterpay", "zip", "klarna"
        };

        /// <summary>
        /// Every icon that fits an entry type: all bank logos (plus card networks for a card).
        /// Empty for types without a known family, so the picker behaves as before.
        /// </summary>
        public static IReadOnlyList<IconItem> MatchKind(IEnumerable<IconItem> items, string? kind)
        {
            var k = (kind ?? string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
            bool card = k is "creditcard" or "card";
            bool bank = card || k is "bankaccount" or "bank";
            if (!bank) return Array.Empty<IconItem>();

            var brands = card ? BankBrands.Concat(CardNetworks).ToArray() : BankBrands;

            return items
                .Where(item =>
                {
                    var words = WordsOf(item.Name);
                    if (words.Count == 0) return false;
                    var joined = string.Concat(words);

                    if (words.Any(w => BankSuffixWords.Any(s => w == s || w.EndsWith(s, StringComparison.Ordinal))))
                        return true;

                    foreach (var brand in brands)
                    {
                        // Short brands (anz, nab, ing, amp) only as a whole word, never inside one.
                        if (brand.Length <= 3)
                        {
                            if (words.Contains(brand)) return true;
                        }
                        else if (words.Any(w => w.StartsWith(brand, StringComparison.Ordinal)) ||
                                 joined.StartsWith(brand, StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }
                    return false;
                })
                .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Lower-case letter runs of a file name: "0025_Suncorp-Bank-Logo" → suncorp, bank, logo.</summary>
        private static List<string> WordsOf(string? name)
        {
            var words = new List<string>();
            if (string.IsNullOrEmpty(name)) return words;
            var current = new System.Text.StringBuilder();
            foreach (var ch in name)
            {
                if (char.IsLetter(ch))
                {
                    current.Append(char.ToLowerInvariant(ch));
                }
                else if (current.Length > 0)
                {
                    words.Add(current.ToString());
                    current.Clear();
                }
            }
            if (current.Length > 0) words.Add(current.ToString());
            return words;
        }

        public static IReadOnlyList<IconItem> Rank(IEnumerable<IconItem> items, IReadOnlyList<string> queries, double minimumScore, int take)
        {
            if (queries.Count == 0) return Array.Empty<IconItem>();
            return items
                .Select(i => (Item: i, Score: Score(i, queries)))
                .Where(x => x.Score >= minimumScore)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Item.Name, StringComparer.OrdinalIgnoreCase)
                .Take(take)
                .Select(x => x.Item)
                .ToList();
        }

        private static double ScoreOne(string key, IReadOnlyList<string> itemTokens, string rawQuery)
        {
            var q = Normalize(rawQuery);
            if (q.Length == 0) return 0;
            if (key == q) return 1.0;

            int shorter = Math.Min(key.Length, q.Length);
            int longer = Math.Max(key.Length, q.Length);
            double lengthRatio = (double)shorter / longer;
            double score = 0;

            if (shorter >= 3 && (key.StartsWith(q, StringComparison.Ordinal) || q.StartsWith(key, StringComparison.Ordinal)))
                score = Math.Max(score, 0.8 + 0.1 * lengthRatio);
            else if (shorter >= 3 && (key.Contains(q, StringComparison.Ordinal) || q.Contains(key, StringComparison.Ordinal)))
                score = Math.Max(score, 0.62 + 0.15 * lengthRatio);

            var queryTokens = Tokens(rawQuery);
            if (queryTokens.Count > 0 && itemTokens.Count > 0)
            {
                int shared = queryTokens.Count(t => itemTokens.Contains(t));
                if (shared > 0)
                    score = Math.Max(score, 0.6 * shared / Math.Max(queryTokens.Count, itemTokens.Count) + 0.1);
            }

            if (shorter >= 4)
            {
                double similarity = 1.0 - (double)Levenshtein(key, q) / longer;
                if (similarity >= 0.75) score = Math.Max(score, similarity * 0.7);
            }

            return score;
        }

        private static void AddQuery(List<string> queries, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var trimmed = value.Trim();
            if (Normalize(trimmed).Length == 0) return;
            if (!queries.Any(q => string.Equals(Normalize(q), Normalize(trimmed), StringComparison.Ordinal)))
                queries.Add(trimmed);
        }

        private static string Singular(string word)
        {
            if (word.Length > 4 && word.EndsWith("ies", StringComparison.Ordinal)) return word[..^3] + "y";
            if (word.Length > 3 && word.EndsWith("s", StringComparison.Ordinal) && !word.EndsWith("ss", StringComparison.Ordinal)) return word[..^1];
            return word;
        }

        private static string? HostOf(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var candidate = url.Contains("://", StringComparison.Ordinal) ? url : "https://" + url;
            return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
                ? uri.Host.ToLowerInvariant()
                : null;
        }

        private static int Levenshtein(string a, string b)
        {
            var previous = new int[b.Length + 1];
            var current = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) previous[j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                current[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                }
                (previous, current) = (current, previous);
            }
            return previous[b.Length];
        }
    }
}
