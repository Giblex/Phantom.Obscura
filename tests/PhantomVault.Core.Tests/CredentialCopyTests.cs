using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests
{
    public class CredentialCopyTests
    {
        [Fact]
        public void Clone_carries_every_writable_property()
        {
            // Guards the failure mode that motivated Credential.CopyValuesFrom: a new
            // field is added to Credential and the hand-written copy lists never learn
            // about it, so it silently vanishes on merge, export or restore.
            var source = FullyPopulated();
            var clone = source.Clone();

            var missed = new List<string>();

            foreach (var property in typeof(Credential).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length > 0)
                    continue;

                if (IgnoredProperties.Contains(property.Name))
                    continue;

                var expected = property.GetValue(source);
                var actual = property.GetValue(clone);

                if (!ValuesMatch(expected, actual))
                    missed.Add($"{property.Name}: expected '{Describe(expected)}', got '{Describe(actual)}'");
            }

            Assert.True(missed.Count == 0,
                "Credential.CopyValuesFrom did not copy: " + string.Join("; ", missed));
        }

        [Fact]
        public void Clone_deep_copies_collections()
        {
            var source = FullyPopulated();
            var clone = source.Clone();

            clone.Tags.Add("added-later");
            clone.CustomFields["new"] = "value";
            clone.Sections[0].Value = "changed";

            Assert.DoesNotContain("added-later", source.Tags);
            Assert.False(source.CustomFields.ContainsKey("new"));
            Assert.NotEqual("changed", source.Sections[0].Value);
        }

        [Fact]
        public void Clone_with_new_id_produces_a_distinct_identity()
        {
            var source = FullyPopulated();

            var same = source.Clone();
            var fresh = source.Clone(newId: true);

            Assert.Equal(source.Id, same.Id);
            Assert.NotEqual(source.Id, fresh.Id);
            Assert.Equal(source.Title, fresh.Title);
        }

        [Fact]
        public void Merge_keeps_type_specific_fields_that_no_strategy_touches()
        {
            // The bug: merging an imported duplicate against a saved card wiped the card
            // number, because the merge only ever copied the generic login fields.
            var existing = new Credential
            {
                EntryType = EntryType.CreditCard,
                Title = "Visa",
                CardNumber = "4111111111111111",
                CardholderName = "A Halliday",
                CardCVV = "123",
                Notes = "existing note"
            };

            var incoming = new Credential
            {
                EntryType = EntryType.CreditCard,
                Title = "Visa",
                Notes = "imported note"
            };

            var merged = new MergeStrategyService()
                .MergeCredentials(existing, incoming, MergeStrategy.MergeNotes);

            Assert.Equal("4111111111111111", merged.CardNumber);
            Assert.Equal("A Halliday", merged.CardholderName);
            Assert.Equal("123", merged.CardCVV);
            Assert.Equal(EntryType.CreditCard, merged.EntryType);
        }

        [Fact]
        public void Merge_keeps_sections_that_no_strategy_touches()
        {
            var existing = new Credential
            {
                EntryType = EntryType.Password,
                Title = "GitHub",
                Sections = new List<EntrySection>
                {
                    EntrySection.CreateInline(EntrySectionKind.RecoveryEmail, "Recovery", "rescue@example.com")
                }
            };

            var incoming = new Credential { EntryType = EntryType.Password, Title = "GitHub" };

            var merged = new MergeStrategyService()
                .MergeCredentials(existing, incoming, MergeStrategy.MergeTags);

            var section = Assert.Single(merged.Sections);
            Assert.Equal("rescue@example.com", section.Value);
        }

        [Fact]
        public void KeepBoth_gives_the_imported_copy_its_own_identity()
        {
            var existing = new Credential { Title = "GitHub", Username = "me" };
            var incoming = new Credential { Title = "GitHub", Username = "me" };

            var merged = new MergeStrategyService()
                .MergeCredentials(existing, incoming, MergeStrategy.KeepBoth);

            Assert.NotEqual(existing.Id, merged.Id);
            Assert.NotEqual(incoming.Id, merged.Id);
            Assert.Contains("(imported)", merged.Title);
        }

        [Fact]
        public void ReplaceWithNew_preserves_the_original_creation_date()
        {
            var created = new DateTimeOffset(2019, 3, 4, 0, 0, 0, TimeSpan.Zero);
            var existing = new Credential { Title = "GitHub", CreatedUtc = created };
            var incoming = new Credential { Title = "GitHub", CreatedUtc = DateTimeOffset.UtcNow };

            var merged = new MergeStrategyService()
                .MergeCredentials(existing, incoming, MergeStrategy.ReplaceWithNew);

            Assert.Equal(created, merged.CreatedUtc);
        }

        /// <summary>
        /// Computed or alias properties that mirror another field, so comparing them adds
        /// nothing. <see cref="Credential.Type"/> mirrors EntryType, the date aliases
        /// mirror the Utc fields, and Category mirrors Group.
        /// </summary>
        private static readonly HashSet<string> IgnoredProperties = new(StringComparer.Ordinal)
        {
            nameof(Credential.Type),
            nameof(Credential.Category),
            nameof(Credential.CreatedAt),
            nameof(Credential.CreatedDate),
            nameof(Credential.ModifiedDate),
            nameof(Credential.LastAccessedDate),
            nameof(Credential.SecureCustomFields),
            nameof(Credential.CardCVVSecure),
            nameof(Credential.CardPINSecure),
            nameof(Credential.ApiKeyValueSecure)
        };

        private static bool ValuesMatch(object? expected, object? actual)
        {
            if (expected is null || actual is null)
                return Equals(expected, actual);

            if (expected is List<string> expectedTags && actual is List<string> actualTags)
                return expectedTags.SequenceEqual(actualTags);

            if (expected is Dictionary<string, string> expectedFields && actual is Dictionary<string, string> actualFields)
                return expectedFields.Count == actualFields.Count &&
                       expectedFields.All(kvp => actualFields.TryGetValue(kvp.Key, out var v) && v == kvp.Value);

            if (expected is List<EntrySection> expectedSections && actual is List<EntrySection> actualSections)
                return expectedSections.Count == actualSections.Count &&
                       expectedSections.Zip(actualSections).All(pair =>
                           pair.First.Kind == pair.Second.Kind &&
                           pair.First.Label == pair.Second.Label &&
                           pair.First.Value == pair.Second.Value &&
                           pair.First.IsSecret == pair.Second.IsSecret &&
                           pair.First.LinkedEntryId == pair.Second.LinkedEntryId);

            return Equals(expected, actual);
        }

        private static string Describe(object? value) => value switch
        {
            null => "(null)",
            List<string> tags => string.Join(",", tags),
            Dictionary<string, string> fields => string.Join(",", fields.Select(kvp => $"{kvp.Key}={kvp.Value}")),
            List<EntrySection> sections => $"{sections.Count} section(s)",
            _ => value.ToString() ?? "(null)"
        };

        private static Credential FullyPopulated()
        {
            var credential = new Credential
            {
                EntryType = EntryType.CreditCard,
                Title = "Title value",
                Username = "user value",
                Password = "password value",
                Url = "https://example.com",
                Notes = "notes value",
                Group = "group value",
                Icon = "icon value",
                IconColor = "#112233",
                IsFavorite = true,
                IsPasskey = true,
                AutoTypeSequence = "{USERNAME}{TAB}{PASSWORD}",
                PasskeyId = "passkey-1",
                CreatedUtc = new DateTimeOffset(2021, 1, 2, 3, 4, 5, TimeSpan.Zero),
                LastUpdatedUtc = new DateTimeOffset(2022, 2, 3, 4, 5, 6, TimeSpan.Zero),
                ExpiryUtc = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
                LastUsedUtc = new DateTime(2023, 5, 6, 7, 8, 9, DateTimeKind.Utc),

                Tags = new List<string> { "one", "two" },
                CustomFields = new Dictionary<string, string> { ["k1"] = "v1", ["k2"] = "v2" },
                Sections = new List<EntrySection>
                {
                    EntrySection.CreateInline(EntrySectionKind.Note, "A note", "note body"),
                    EntrySection.CreateLink(EntrySectionKind.Totp, "linked-entry-id", "Linked 2FA")
                },

                WiFiSSID = "ssid",
                WiFiSecurityType = "WPA3",
                WiFiBSSID = "AA:BB:CC:DD:EE:FF",
                WiFiPassword = "wifi-pass",

                IdDocumentType = "Passport",
                IdNumber = "P1234567",
                IdCardNumber = "C7654321",
                IdIssuingCountry = "Australia",
                IdIssuingState = "VIC",
                IdIssueDate = new DateTimeOffset(2018, 6, 1, 0, 0, 0, TimeSpan.Zero),
                IdExpiryDate = new DateTimeOffset(2028, 6, 1, 0, 0, 0, TimeSpan.Zero),

                ApiKeyValue = "sk_live_abc",
                ApiKeyType = "Secret",
                ApiEndpoint = "https://api.example.com",
                ApiEnvironment = "Production",
                ApiDocumentationUrl = "https://docs.example.com",

                ContactFullName = "Jane Doe",
                ContactEmail = "jane@example.com",
                ContactPhone = "+61400000000",
                ContactAddress = "1 Test St",
                ContactCompany = "Contoso",
                ContactJobTitle = "CTO",

                CardNumber = "4111111111111111",
                CardholderName = "J DOE",
                CardType = "Visa",
                CardCVV = "123",
                CardExpiryMonth = "04",
                CardExpiryYear = "2029",
                CardPIN = "4321",
                CardBillingAddress = "2 Test St",

                BankName = "Contoso Bank",
                BankAccountNumber = "000123456789",
                BankRoutingNumber = "110000000",
                BankIBAN = "DE89370400440532013000",
                BankSWIFT = "COBADEFFXXX",
                BankAccountType = "Checking",
                BankBranchCode = "1234",
                BankBranchAddress = "3 Test St",

                TotpSecret = "JBSWY3DPEHPK3PXP",
                TotpDigits = 8,
                TotpTimeStep = 60,
                TotpAlgorithm = "SHA256",
                TotpIssuer = "Example",
                TotpAccountName = "jane@example.com",

                PinLabel = "Front door",
                PinValue = "987654",
                PinCategory = "Home",
                PinIssuer = "Landlord"
            };

            return credential;
        }
    }
}
