using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services.Security
{

    public sealed class DecoyCredentialGenerator
    {

        // CSPRNG by default so generated decoys are not predictable from process
        // start time. Only when an explicit seed is supplied (deterministic test
        // scenarios) does it switch to a small seeded SplitMix64 stream. That keeps
        // tests reproducible without System.Random, which the security hard-rules
        // forbid anywhere in src.
        private sealed class SecureRng
        {
            private readonly bool _seeded;
            private ulong _state;

            public SecureRng(int? seed)
            {
                if (seed.HasValue)
                {
                    _seeded = true;
                    _state = unchecked((ulong)seed.Value * 0x9E3779B97F4A7C15UL + 1UL);
                }
            }

            public int Next(int maxExclusive)
                => _seeded ? SeededRange(0, maxExclusive) : RandomNumberGenerator.GetInt32(maxExclusive);

            public int Next(int minInclusive, int maxExclusive)
                => _seeded ? SeededRange(minInclusive, maxExclusive) : RandomNumberGenerator.GetInt32(minInclusive, maxExclusive);

            // SplitMix64 (Steele, Lea & Flood): a well-distributed, non-cryptographic
            // stream, used only for the seeded test path.
            private ulong NextUInt64()
            {
                unchecked
                {
                    _state += 0x9E3779B97F4A7C15UL;
                    var z = _state;
                    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                    z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                    return z ^ (z >> 31);
                }
            }

            // Uniform in [min, max) by rejection sampling, so no value is favoured by
            // modulo bias. An empty range returns min, as System.Random.Next did.
            private int SeededRange(int minInclusive, int maxExclusive)
            {
                if (maxExclusive < minInclusive)
                    throw new ArgumentOutOfRangeException(nameof(maxExclusive));
                if (maxExclusive == minInclusive)
                    return minInclusive;

                var range = (ulong)((long)maxExclusive - minInclusive);
                var limit = ulong.MaxValue - (ulong.MaxValue % range);
                ulong value;
                do
                {
                    value = NextUInt64();
                } while (value >= limit);

                return (int)(minInclusive + (long)(value % range));
            }
        }

        private readonly SecureRng _rng;

        private static readonly string[] PopularSites = new[]
        {
            "google.com", "facebook.com", "amazon.com", "microsoft.com", "apple.com",
            "twitter.com", "instagram.com", "linkedin.com", "github.com", "reddit.com",
            "netflix.com", "spotify.com", "dropbox.com", "paypal.com", "ebay.com",
            "bankofamerica.com", "chase.com", "wellsfargo.com", "citibank.com",
            "gmail.com", "outlook.com", "yahoo.com", "protonmail.com"
        };

        private static readonly string[] EmailProviders = new[]
        {
            "gmail.com", "outlook.com", "yahoo.com", "hotmail.com",
            "protonmail.com", "icloud.com", "aol.com"
        };

        private static readonly string[] FirstNames = new[]
        {
            "james", "john", "robert", "michael", "william", "david", "richard", "joseph",
            "mary", "patricia", "jennifer", "linda", "elizabeth", "barbara", "susan", "jessica",
            "alex", "chris", "sam", "jordan", "taylor", "morgan", "casey", "riley"
        };

        private static readonly string[] LastNames = new[]
        {
            "smith", "johnson", "williams", "brown", "jones", "garcia", "miller", "davis",
            "rodriguez", "martinez", "hernandez", "lopez", "gonzalez", "wilson", "anderson",
            "thomas", "taylor", "moore", "jackson", "martin", "lee", "thompson", "white"
        };

        private static readonly string[] WifiPrefixes = new[]
        {
            "HOME", "NETGEAR", "LINKSYS", "TP-LINK", "Fios", "Xfinity",
            "ATT", "Spectrum", "CenturyLink", "MyNetwork"
        };

        public DecoyCredentialGenerator(int? seed = null)
        {
            _rng = new SecureRng(seed);
        }

        public List<Credential> GenerateDecoyCredentials(int? count = null)
        {
            int credentialCount = count ?? _rng.Next(15, 31);
            var credentials = new List<Credential>();

            int passwordCount = (int)(credentialCount * 0.6);
            int wifiCount = _rng.Next(2, 5);
            int creditCardCount = _rng.Next(1, 3);
            int apiKeyCount = _rng.Next(2, 4);
            int identityCount = _rng.Next(1, 2);

            for (int i = 0; i < passwordCount; i++)
            {
                credentials.Add(GeneratePasswordEntry());
            }

            for (int i = 0; i < wifiCount; i++)
            {
                credentials.Add(GenerateWiFiEntry());
            }

            for (int i = 0; i < creditCardCount; i++)
            {
                credentials.Add(GenerateCreditCardEntry());
            }

            for (int i = 0; i < apiKeyCount; i++)
            {
                credentials.Add(GenerateApiKeyEntry());
            }

            for (int i = 0; i < identityCount; i++)
            {
                credentials.Add(GenerateIdentityEntry());
            }

            return credentials;
        }

        private Credential GeneratePasswordEntry()
        {
            string site = PopularSites[_rng.Next(PopularSites.Length)];
            string firstName = FirstNames[_rng.Next(FirstNames.Length)];
            string lastName = LastNames[_rng.Next(LastNames.Length)];

            string username = GenerateRealisticUsername(firstName, lastName);

            string password = GenerateRealisticPassword();

            return new Credential
            {
                EntryType = EntryType.Password,
                Title = $"{site} Account",
                Username = username,
                Password = password,
                Url = $"https://www.{site}",
                Notes = GenerateRealisticNotes(),
                Group = PickRandomCategory(),
                CreatedUtc = GeneratePastDate(365, 30),
                LastUpdatedUtc = GeneratePastDate(90, 1)
            };
        }

        private Credential GenerateWiFiEntry()
        {
            string prefix = WifiPrefixes[NextInt(0, WifiPrefixes.Length)];
            string suffix = NextInt(1000, 9999).ToString();
            string networkName = $"{prefix}-{suffix}";

            return new Credential
            {
                EntryType = EntryType.WiFi,
                Title = networkName,
                WiFiSSID = networkName,
                WiFiPassword = GenerateRealisticWiFiPassword(),
                Notes = $"Home WiFi network\n5GHz band",
                Group = "Network",
                CreatedUtc = GeneratePastDate(730, 180),
                LastUpdatedUtc = GeneratePastDate(180, 30)
            };
        }

        private Credential GenerateCreditCardEntry()
        {
            string firstName = FirstNames[_rng.Next(FirstNames.Length)];
            string lastName = LastNames[_rng.Next(LastNames.Length)];
            string cardholderName = $"{CapitalizeFirst(firstName)} {CapitalizeFirst(lastName)}";

            string cardNumber = GenerateFakeCreditCardNumber();

            DateTime expiry = DateTime.Now.AddMonths(_rng.Next(12, 37));
            string expiryDate = expiry.ToString("MM/yy");

            string cvv = _rng.Next(100, 1000).ToString();

            return new Credential
            {
                EntryType = EntryType.CreditCard,
                Title = $"{GetRandomCardType()} {cardNumber.Substring(cardNumber.Length - 4)}",
                CardholderName = cardholderName,
                CardNumber = cardNumber,
                CardCVV = cvv,
                CardExpiryMonth = expiry.Month.ToString("D2"),
                CardExpiryYear = expiry.Year.ToString(),
                CardType = GetRandomCardType(),
                Notes = $"Exp: {expiryDate}\nCardholder: {cardholderName}",
                Group = "Financial",
                CreatedUtc = GeneratePastDate(730, 180),
                LastUpdatedUtc = GeneratePastDate(90, 10)
            };
        }

        private Credential GenerateApiKeyEntry()
        {
            string[] services = { "AWS", "Azure", "Google Cloud", "GitHub", "Stripe", "SendGrid", "Twilio" };
            string service = services[_rng.Next(services.Length)];

            string apiKey = GenerateFakeApiKey();

            return new Credential
            {
                EntryType = EntryType.ApiKey,
                Title = $"{service} API Key",
                Username = $"api-user-{_rng.Next(10000, 99999)}",
                ApiKeyValue = apiKey,
                ApiEnvironment = "Production",
                Notes = $"Production API key\nCreated: {DateTime.Now.AddDays(-_rng.Next(30, 365)):yyyy-MM-dd}",
                Group = "Development",
                CreatedUtc = GeneratePastDate(365, 90),
                LastUpdatedUtc = GeneratePastDate(60, 5)
            };
        }

        private Credential GenerateIdentityEntry()
        {
            string firstName = FirstNames[_rng.Next(FirstNames.Length)];
            string lastName = LastNames[_rng.Next(LastNames.Length)];
            string fullName = $"{CapitalizeFirst(firstName)} {CapitalizeFirst(lastName)}";

            string dlNumber = $"{(char)_rng.Next('A', 'Z')}{_rng.Next(10000000, 99999999)}";
            string ssn = $"{_rng.Next(100, 999)}-{_rng.Next(10, 99)}-{_rng.Next(1000, 9999)}";

            return new Credential
            {
                EntryType = EntryType.Identity,
                Title = $"{fullName} - Driver's License",
                IdDocumentType = "Driver's License",
                IdNumber = dlNumber,
                IdIssuingState = GetRandomState(),
                Notes = $"SSN: {ssn}\nDOB: {GenerateFakeBirthdate():MM/dd/yyyy}\nState: {GetRandomState()}",
                Group = "Personal",
                CreatedUtc = GeneratePastDate(1825, 365),
                LastUpdatedUtc = GeneratePastDate(180, 30)
            };
        }

        private string GenerateRealisticUsername(string firstName, string lastName)
        {
            int pattern = _rng.Next(6);
            string emailProvider = EmailProviders[_rng.Next(EmailProviders.Length)];

            return pattern switch
            {
                0 => $"{firstName}.{lastName}@{emailProvider}",
                1 => $"{firstName}{lastName}@{emailProvider}",
                2 => $"{firstName}{_rng.Next(10, 99)}@{emailProvider}",
                3 => $"{firstName[0]}{lastName}@{emailProvider}",
                4 => $"{firstName}_{lastName}@{emailProvider}",
                _ => $"{lastName}.{firstName}@{emailProvider}"
            };
        }

        private string GenerateRealisticPassword()
        {

            int pattern = NextInt(0, 4);

            return pattern switch
            {
                0 => $"{CapitalizeFirst(FirstNames[NextInt(0, FirstNames.Length)])}{NextInt(1000, 9999)}!",
                1 => $"{CapitalizeFirst(LastNames[NextInt(0, LastNames.Length)])}{NextInt(10, 99)}@{NextInt(2015, 2025)}",
                2 => $"Welcome{NextInt(100, 999)}!",
                _ => GenerateRandomString(12, includeSpecial: true)
            };
        }

        private string GenerateRealisticWiFiPassword()
        {

            return GenerateRandomString(16, includeSpecial: false);
        }

        private string GenerateFakeCreditCardNumber()
        {

            var parts = new[]
            {
                NextInt(4000, 5000).ToString(),
                NextInt(1000, 9999).ToString(),
                NextInt(1000, 9999).ToString(),
                NextInt(1000, 9999).ToString()
            };
            return string.Join(" ", parts);
        }

        private string GenerateFakeApiKey()
        {

            string prefix = new[] { "sk", "pk", "api", "key" }[NextInt(0, 4)];
            string randomPart = GenerateRandomString(32, includeSpecial: false);
            return $"{prefix}_{randomPart}";
        }

        private int NextInt(int minInclusive, int maxExclusive)
        {
            return RandomNumberGenerator.GetInt32(minInclusive, maxExclusive);
        }

        private string GenerateRandomString(int length, bool includeSpecial)
        {
            const string alphanumeric = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            const string special = "!@#$%^&*";
            string chars = includeSpecial ? alphanumeric + special : alphanumeric;

            return new string(Enumerable.Range(0, length)
                .Select(_ => chars[NextInt(0, chars.Length)])
                .ToArray());
        }

        private string GenerateRealisticNotes()
        {
            int hasNotes = NextInt(0, 100);
            if (hasNotes < 70) return string.Empty;

            string[] notes = new[]
            {
                "Primary account",
                "Use for personal email",
                "Two-factor authentication enabled",
                "Shared with spouse",
                "Recovery email: backup@example.com",
                "Security questions set up",
                "Created during promotion"
            };

            return notes[NextInt(0, notes.Length)];
        }

        private string PickRandomCategory()
        {
            string[] categories = { "Personal", "Work", "Social", "Shopping", "Entertainment", "Finance", "Email" };
            return categories[_rng.Next(categories.Length)];
        }

        private DateTimeOffset GeneratePastDate(int maxDaysAgo, int minDaysAgo)
        {
            return DateTimeOffset.UtcNow.AddDays(-_rng.Next(minDaysAgo, maxDaysAgo + 1));
        }

        private DateTime GenerateFakeBirthdate()
        {
            int age = _rng.Next(25, 65);
            return DateTime.Now.AddYears(-age).AddDays(_rng.Next(-182, 183));
        }

        private string GetRandomCardType()
        {
            string[] types = { "Visa", "Mastercard", "Amex", "Discover" };
            return types[_rng.Next(types.Length)];
        }

        private string GetRandomState()
        {
            string[] states = { "CA", "NY", "TX", "FL", "IL", "PA", "OH", "GA", "NC", "MI" };
            return states[_rng.Next(states.Length)];
        }

        private string CapitalizeFirst(string str)
        {
            if (string.IsNullOrEmpty(str)) return str;
            return char.ToUpper(str[0]) + str.Substring(1);
        }
    }
}

