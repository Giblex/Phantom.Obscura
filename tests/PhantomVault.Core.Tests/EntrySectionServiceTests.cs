using System;
using System.Collections.Generic;
using System.Linq;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests
{
    public class EntrySectionServiceTests
    {
        private readonly EntrySectionService _service = new();

        [Fact]
        public void Resolve_returns_the_inline_value_when_the_section_is_not_linked()
        {
            var section = EntrySection.CreateInline(EntrySectionKind.RecoveryEmail, "Recovery", "rescue@example.com");

            var resolved = _service.Resolve(section, _ => null);

            Assert.Equal("rescue@example.com", resolved.Value);
            Assert.False(resolved.IsBrokenLink);
        }

        [Fact]
        public void Resolve_pulls_the_value_from_the_linked_entry()
        {
            var target = new Credential
            {
                EntryType = EntryType.TotpGenerator,
                Title = "GitHub 2FA",
                TotpSecret = "JBSWY3DPEHPK3PXP",
                TotpDigits = 8,
                TotpTimeStep = 60
            };

            var section = EntrySectionService.CreateLinkTo(target);
            var resolved = _service.Resolve(section, id => id == target.Id ? target : null);

            Assert.Equal("JBSWY3DPEHPK3PXP", resolved.Value);
            Assert.Equal(8, resolved.TotpDigits);
            Assert.Equal(60, resolved.TotpPeriod);
            Assert.False(resolved.IsBrokenLink);
        }

        [Fact]
        public void Resolve_flags_a_link_whose_target_is_gone()
        {
            var section = EntrySection.CreateLink(EntrySectionKind.Note, "missing-id", "Orphan");

            var resolved = _service.Resolve(section, _ => null);

            Assert.True(resolved.IsBrokenLink);
            Assert.Equal(string.Empty, resolved.Value);
        }

        [Fact]
        public void Resolve_builds_an_otpauth_qr_payload_for_totp_sections()
        {
            var section = EntrySection.CreateInline(EntrySectionKind.Totp, "2FA", "JBSWY3DPEHPK3PXP");
            section.SetMeta(EntrySection.MetaTotpIssuer, "GitHub");
            section.SetMeta(EntrySection.MetaTotpAccount, "me@example.com");

            var resolved = _service.Resolve(section, _ => null);

            Assert.NotNull(resolved.QrPayload);
            Assert.StartsWith("otpauth://totp/", resolved.QrPayload);
            Assert.Contains("secret=JBSWY3DPEHPK3PXP", resolved.QrPayload);
            Assert.Contains("issuer=GitHub", resolved.QrPayload);
        }

        [Fact]
        public void Resolve_produces_no_qr_payload_for_an_ordinary_note()
        {
            var section = EntrySection.CreateInline(EntrySectionKind.Note, "Note", "nothing scannable");

            Assert.Null(_service.Resolve(section, _ => null).QrPayload);
        }

        [Fact]
        public void ResolveAll_orders_sections_by_sort_order()
        {
            var credential = new Credential
            {
                Sections = new List<EntrySection>
                {
                    Ordered(EntrySectionKind.Note, "third", 2),
                    Ordered(EntrySectionKind.Note, "first", 0),
                    Ordered(EntrySectionKind.Note, "second", 1)
                }
            };

            var resolved = _service.ResolveAll(credential, _ => null);

            Assert.Equal(new[] { "first", "second", "third" }, resolved.Select(r => r.Value));
        }

        [Fact]
        public void RecoveryCodes_split_on_newlines_and_commas()
        {
            var section = EntrySection.CreateInline(EntrySectionKind.RecoveryCodes, "Codes", "aaa\nbbb, ccc\r\nddd");

            var resolved = _service.Resolve(section, _ => null);

            Assert.Equal(new[] { "aaa", "bbb", "ccc", "ddd" }, resolved.RecoveryCodes);
        }

        [Fact]
        public void Used_recovery_code_indexes_round_trip()
        {
            var section = EntrySection.CreateInline(EntrySectionKind.RecoveryCodes, "Codes", "a\nb\nc");

            section.SetUsedRecoveryCodeIndexes(new[] { 2, 0, 2 });

            Assert.Equal(new[] { 0, 2 }, section.GetUsedRecoveryCodeIndexes().OrderBy(i => i));
        }

        [Fact]
        public void Secret_defaults_follow_the_section_kind()
        {
            Assert.True(EntrySection.CreateInline(EntrySectionKind.Totp).IsSecret);
            Assert.True(EntrySection.CreateInline(EntrySectionKind.PinCode).IsSecret);
            Assert.True(EntrySection.CreateInline(EntrySectionKind.RecoveryCodes).IsSecret);
            Assert.False(EntrySection.CreateInline(EntrySectionKind.Note).IsSecret);
            Assert.False(EntrySection.CreateInline(EntrySectionKind.RecoveryEmail).IsSecret);
        }

        [Fact]
        public void Validate_rejects_a_malformed_recovery_email()
        {
            var section = EntrySection.CreateInline(EntrySectionKind.RecoveryEmail, "Recovery", "not-an-email");

            Assert.NotEmpty(EntrySectionService.Validate(section));
        }

        [Fact]
        public void Validate_accepts_a_well_formed_recovery_email()
        {
            var section = EntrySection.CreateInline(EntrySectionKind.RecoveryEmail, "Recovery", "rescue@example.com");

            Assert.Empty(EntrySectionService.Validate(section));
        }

        [Fact]
        public void Validate_rejects_a_pin_length_outside_the_supported_range()
        {
            var section = EntrySection.CreateInline(EntrySectionKind.PinCode, "PIN", "1234");
            section.SetMeta(EntrySection.MetaPinLength, "64");

            Assert.NotEmpty(EntrySectionService.Validate(section));
        }

        [Fact]
        public void PinLengthRange_clamps_to_one_through_thirty_two()
        {
            Assert.Equal(1, PinLengthRange.Clamp(0));
            Assert.Equal(1, PinLengthRange.Clamp(-5));
            Assert.Equal(32, PinLengthRange.Clamp(99));
            Assert.Equal(6, PinLengthRange.Clamp(6));
            Assert.Equal(32, PinLengthRange.All.Count);
        }

        [Fact]
        public void Templates_produce_sequential_sort_orders()
        {
            var template = EntrySectionTemplates.FindByName("Account with 2FA");
            Assert.NotNull(template);

            var sections = template!.CreateSections(5);

            Assert.Equal(new[] { 5, 6, 7 }, sections.Select(s => s.SortOrder));
            Assert.Equal(EntrySectionKind.Totp, sections[0].Kind);
        }

        private static EntrySection Ordered(EntrySectionKind kind, string value, int sortOrder)
        {
            var section = EntrySection.CreateInline(kind, kind.ToString(), value);
            section.SortOrder = sortOrder;
            return section;
        }
    }
}
