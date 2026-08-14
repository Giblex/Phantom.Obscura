using System;
using System.Collections.Generic;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests
{
    public class CredentialTotpResolverTests
    {
        private const string Seed = "JBSWY3DPEHPK3PXP";
        private const string OtherSeed = "KRSXG5CTMVRXEZLU";

        [Fact]
        public void Entry_seed_is_used_when_present()
        {
            var credential = new Credential
            {
                Title = "GitHub",
                TotpSecret = Seed,
                TotpDigits = 8,
                TotpTimeStep = 60,
                TotpAlgorithm = "SHA256"
            };

            var totp = CredentialTotpResolver.Resolve(credential);

            Assert.NotNull(totp);
            Assert.Equal(Seed, totp!.Secret);
            Assert.Equal(8, totp.Digits);
            Assert.Equal(60, totp.Period);
            Assert.Equal(TotpSource.Entry, totp.Source);
        }

        [Fact]
        public void Entry_seed_wins_over_a_section_so_existing_behaviour_is_unchanged()
        {
            var credential = new Credential
            {
                Title = "GitHub",
                TotpSecret = Seed,
                Sections = new List<EntrySection>
                {
                    EntrySection.CreateInline(EntrySectionKind.Totp, "Backup 2FA", OtherSeed)
                }
            };

            Assert.Equal(Seed, CredentialTotpResolver.Resolve(credential)!.Secret);
        }

        [Fact]
        public void Inline_totp_section_is_used_when_the_entry_has_no_seed()
        {
            // This is the gap: following the "Account with 2FA" template puts the seed in
            // a section, and autofill previously could not see it.
            var section = EntrySection.CreateInline(EntrySectionKind.Totp, "Authenticator", Seed);
            section.SetMeta(EntrySection.MetaTotpDigits, "8");
            section.SetMeta(EntrySection.MetaTotpPeriod, "60");

            var credential = new Credential
            {
                Title = "GitHub",
                Sections = new List<EntrySection> { section }
            };

            var totp = CredentialTotpResolver.Resolve(credential);

            Assert.NotNull(totp);
            Assert.Equal(Seed, totp!.Secret);
            Assert.Equal(8, totp.Digits);
            Assert.Equal(60, totp.Period);
            Assert.Equal(TotpSource.InlineSection, totp.Source);
        }

        [Fact]
        public void Linked_totp_section_resolves_through_the_lookup()
        {
            var authenticator = new Credential
            {
                EntryType = EntryType.TotpGenerator,
                Title = "GitHub 2FA",
                TotpSecret = Seed,
                TotpDigits = 7,
                TotpTimeStep = 45
            };

            var credential = new Credential
            {
                Title = "GitHub",
                Sections = new List<EntrySection> { EntrySectionService.CreateLinkTo(authenticator) }
            };

            var totp = CredentialTotpResolver.Resolve(
                credential,
                id => id == authenticator.Id ? authenticator : null);

            Assert.NotNull(totp);
            Assert.Equal(Seed, totp!.Secret);
            Assert.Equal(7, totp.Digits);
            Assert.Equal(45, totp.Period);
            Assert.Equal(TotpSource.LinkedSection, totp.Source);
        }

        [Fact]
        public void Linked_section_without_a_lookup_reports_no_totp()
        {
            // Better to report nothing than to promise a code that cannot be generated:
            // autofill would otherwise wait for a TOTP field it can never fill.
            var credential = new Credential
            {
                Title = "GitHub",
                Sections = new List<EntrySection>
                {
                    EntrySection.CreateLink(EntrySectionKind.Totp, "missing-id", "Linked 2FA")
                }
            };

            Assert.Null(CredentialTotpResolver.Resolve(credential));
            Assert.False(CredentialTotpResolver.HasTotp(credential));
        }

        [Fact]
        public void Broken_link_is_skipped_in_favour_of_a_usable_section()
        {
            var broken = EntrySection.CreateLink(EntrySectionKind.Totp, "missing-id", "Dead link");
            broken.SortOrder = 0;

            var usable = EntrySection.CreateInline(EntrySectionKind.Totp, "Working", Seed);
            usable.SortOrder = 1;

            var credential = new Credential
            {
                Title = "GitHub",
                Sections = new List<EntrySection> { broken, usable }
            };

            var totp = CredentialTotpResolver.Resolve(credential);

            Assert.NotNull(totp);
            Assert.Equal(Seed, totp!.Secret);
        }

        [Fact]
        public void Sections_are_considered_in_display_order()
        {
            var second = EntrySection.CreateInline(EntrySectionKind.Totp, "Second", OtherSeed);
            second.SortOrder = 1;

            var first = EntrySection.CreateInline(EntrySectionKind.Totp, "First", Seed);
            first.SortOrder = 0;

            var credential = new Credential
            {
                Title = "GitHub",
                Sections = new List<EntrySection> { second, first }
            };

            Assert.Equal(Seed, CredentialTotpResolver.Resolve(credential)!.Secret);
        }

        [Fact]
        public void Non_totp_sections_are_ignored()
        {
            var credential = new Credential
            {
                Title = "GitHub",
                Sections = new List<EntrySection>
                {
                    EntrySection.CreateInline(EntrySectionKind.Note, "Note", "not a seed"),
                    EntrySection.CreateInline(EntrySectionKind.RecoveryCodes, "Codes", "aaa\nbbb")
                }
            };

            Assert.Null(CredentialTotpResolver.Resolve(credential));
        }

        [Fact]
        public void Entry_with_no_totp_anywhere_reports_none()
        {
            Assert.Null(CredentialTotpResolver.Resolve(new Credential { Title = "GitHub" }));
            Assert.Null(CredentialTotpResolver.Resolve(null));
            Assert.False(CredentialTotpResolver.HasTotp(new Credential()));
        }

        [Fact]
        public void GenerateCode_produces_the_same_code_from_entry_and_section_seeds()
        {
            var at = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

            var onEntry = new Credential { Title = "A", TotpSecret = Seed };
            var inSection = new Credential
            {
                Title = "B",
                Sections = new List<EntrySection>
                {
                    EntrySection.CreateInline(EntrySectionKind.Totp, "2FA", Seed)
                }
            };

            var fromEntry = CredentialTotpResolver.GenerateCode(onEntry, timestamp: at);
            var fromSection = CredentialTotpResolver.GenerateCode(inSection, timestamp: at);

            Assert.False(string.IsNullOrEmpty(fromEntry));
            Assert.Equal(fromEntry, fromSection);
        }

        [Fact]
        public void GenerateCode_returns_null_rather_than_throwing_on_a_malformed_seed()
        {
            var credential = new Credential { Title = "Broken", TotpSecret = "!!!not-base32!!!" };

            Assert.Null(CredentialTotpResolver.GenerateCode(credential));
        }
    }
}
