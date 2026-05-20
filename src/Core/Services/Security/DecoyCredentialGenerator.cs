using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Generates realistic-looking fake credentials for decoy vaults.
    /// The goal is to make the decoy vault appear legitimate to an attacker,
    /// wasting their time and providing false intelligence.
    /// </summary>
    public sealed class DecoyCredentialGenerator
    {
        private readonly RandomNumberGenerator _rng;

        // Common website patterns
        private static readonly string[] PopularSites = new[]
        {
            "google.com", "facebook.com", "amazon.com", "microsoft.com", "apple.com",
            "twitter.com", "instagram.com", "linkedin.com", "github.com", "reddit.com",
            "netflix.com", "spotify.com", "dropbox.com", "paypal.com", "ebay.com",
            "bankofamerica.com", "chase.com", "wellsfargo.com", "citibank.com",
            "gmail.com", "outlook.com", "yahoo.com", "protonmail.com"
        };

        // Common email providers
        private static readonly string[] EmailProviders = new[]
        {
            "gmail.com", "outlook.com", "yahoo.com", "hotmail.com",
            "protonmail.com", "icloud.com", "aol.com"
        };

        // First names for generating realistic usernames
        private static readonly string[] FirstNames = new[]
        {
            "james", "john", "robert", "michael", "william", "david", "richard", "joseph",
            "mary", "patricia", "jennifer", "linda", "elizabeth", "barbara", "susan", "jessica",
            "alex", "chris", "sam", "jordan", "taylor", "morgan", "casey", "riley"
        };

        // Last names for generating realistic usernames
        private static readonly string[] LastNames = new[]
        {
            "smith", "johnson", "williams", "brown", "jones", "garcia", "miller", "davis",
            "rodriguez", "martinez", "hernandez", "lopez", "gonzalez", "wilson", "anderson",
            "thomas", "taylor", "moore", "jackson", "martin", "lee", "thompson", "white"
        };

        // WiFi network name patterns
        private static readonly string[] WifiPrefixes = new[]
        {
            "HOME", "NETGEAR", "LINKSYS", "TP-LINK", "Fios", "Xfinity",
            "ATT", "Spectrum", "CenturyLink", "MyNetwork"
        };

        public DecoyCredentialGenerator(int? seed = null)
        {
            _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        /// <summary>
        /// Generates a set of realistic decoy credentials.
        /// </summary>
        /// <param name="count">Number of credentials to generate (default: 15-30)</param>
        /// <returns>List of fake credentials</returns>
        public List<Credential> GenerateDecoyCredentials(int? count = null)
        {
            int credentialCount = count ?? _rng.Next(15, 31);
            var credentials = new List<Credential>();

            // Generate mix of different entry types
            int passwordCount = (int)(credentialCount * 0.6); // 60% passwords
            int wifiCount = _rng.Next(2, 5);
            int creditCardCount = _rng.Next(1, 3);
            int apiKeyCount = _rng.Next(2, 4);
            int identityCount = _rng.Next(1, 2);

            // Generate password entries
            for (int i = 0; i < passwordCount; i++)
            {
                credentials.Add(GeneratePasswordEntry());
            }

            // Generate WiFi entries
            for (int i = 0; i < wifiCount; i++)
            {
                credentials.Add(GenerateWiFiEntry());
            }

            // Generate credit card entries
            for (int i = 0; i < creditCardCount; i++)
            {
                credentials.Add(GenerateCreditCardEntry());
            }

            // Generate API key entries
            for (int i = 0; i < apiKeyCount; i++)
            {
                credentials.Add(GenerateApiKeyEntry());
            }

            // Generate identity entries
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

            // Generate realistic username variations
            string username = GenerateRealisticUsername(firstName, lastName);

            // Generate realistic password (appears complex but is fake)
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

            // Generate fake but realistic-looking card number (Luhn algorithm not required for decoy)
            string cardNumber = GenerateFakeCreditCardNumber();

            // Generate expiry date (1-3 years in future)
            DateTime expiry = DateTime.Now.AddMonths(_rng.Next(12, 37));
            string expiryDate = expiry.ToString("MM/yy");

            // Generate CVV
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

            // Generate fake but realistic-looking ID numbers
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
            // Generate passwords that appear complex (mix of patterns)
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
            // WiFi passwords tend to be longer and more complex
            return GenerateRandomString(16, includeSpecial: false);
        }

        private string GenerateFakeCreditCardNumber()
        {
            // Generate fake 16-digit card number (not valid Luhn, but looks real)
            var parts = new[]
            {
                NextInt(4000, 5000).ToString(), // Visa-like prefix
                NextInt(1000, 9999).ToString(),
                NextInt(1000, 9999).ToString(),
                NextInt(1000, 9999).ToString()
            };
            return string.Join(" ", parts);
        }

        private string GenerateFakeApiKey()
        {
            // Generate realistic-looking API key format
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
            if (hasNotes < 70) return string.Empty; // 70% have no notes

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
