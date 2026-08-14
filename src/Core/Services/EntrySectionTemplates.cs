using System;
using System.Collections.Generic;
using System.Linq;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services
{

    public sealed record EntrySectionTemplate(
        string Name,
        string Description,
        IReadOnlyList<EntrySectionKind> Kinds)
    {
        public override string ToString() => Name;

        /// <summary>
        /// Builds a fresh, unsaved set of sections for this template. Sort order follows
        /// the declared order so the entry reads top-to-bottom the way it was designed.
        /// </summary>
        public List<EntrySection> CreateSections(int startingSortOrder = 0)
        {
            var sections = new List<EntrySection>();

            for (var i = 0; i < Kinds.Count; i++)
            {
                var section = EntrySection.CreateInline(Kinds[i]);
                section.SortOrder = startingSortOrder + i;
                sections.Add(section);
            }

            return sections;
        }
    }

    /// <summary>
    /// Ready-made section sets for building an entry from scratch. These exist so a blank
    /// entry does not start as a blank page: the common shapes (an account with 2FA, a
    /// device PIN, a recovery kit) are one click instead of five.
    /// </summary>
    public static class EntrySectionTemplates
    {
        public static IReadOnlyList<EntrySectionTemplate> All { get; } = new List<EntrySectionTemplate>
        {
            new(
                "Account with 2FA",
                "Authenticator secret, recovery codes and a recovery email.",
                new[]
                {
                    EntrySectionKind.Totp,
                    EntrySectionKind.RecoveryCodes,
                    EntrySectionKind.RecoveryEmail
                }),

            new(
                "Recovery kit",
                "Recovery email, recovery codes and two security questions.",
                new[]
                {
                    EntrySectionKind.RecoveryEmail,
                    EntrySectionKind.RecoveryCodes,
                    EntrySectionKind.SecurityQuestion,
                    EntrySectionKind.SecurityQuestion
                }),

            new(
                "Device or card PIN",
                "A PIN plus a note for where it is used.",
                new[]
                {
                    EntrySectionKind.PinCode,
                    EntrySectionKind.Note
                }),

            new(
                "Membership",
                "Membership number, expiry date and a scannable QR code.",
                new[]
                {
                    EntrySectionKind.Text,
                    EntrySectionKind.Date,
                    EntrySectionKind.QrCode
                }),

            new(
                "Contact details",
                "Email, phone and address.",
                new[]
                {
                    EntrySectionKind.RecoveryEmail,
                    EntrySectionKind.Phone,
                    EntrySectionKind.Address
                }),

            new(
                "Just a note",
                "A single free-text note.",
                new[] { EntrySectionKind.Note })
        };

        public static EntrySectionTemplate? FindByName(string? name)
            => string.IsNullOrWhiteSpace(name)
                ? null
                : All.FirstOrDefault(t => string.Equals(t.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
