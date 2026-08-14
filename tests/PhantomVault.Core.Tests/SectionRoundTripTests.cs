using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests
{
    public class SectionRoundTripTests
    {
        [Fact]
        public void Sections_survive_a_json_round_trip()
        {
            // The vault payload and the secure bin both persist credentials as JSON, so a
            // section that does not round-trip would be lost on lock/unlock or on restore.
            var original = WithSections();

            var json = JsonSerializer.Serialize(original);
            var restored = JsonSerializer.Deserialize<Credential>(json);

            Assert.NotNull(restored);
            Assert.Equal(3, restored!.Sections.Count);

            var note = restored.Sections.Single(s => s.Kind == EntrySectionKind.Note);
            Assert.Equal("note body", note.Value);

            var totp = restored.Sections.Single(s => s.Kind == EntrySectionKind.Totp);
            Assert.Equal("linked-entry-id", totp.LinkedEntryId);
            Assert.True(totp.IsLinked);
            Assert.Equal("8", totp.GetMeta(EntrySection.MetaTotpDigits));

            var codes = restored.Sections.Single(s => s.Kind == EntrySectionKind.RecoveryCodes);
            Assert.True(codes.IsSecret);
            Assert.Equal(new[] { 0, 2 }, codes.GetUsedRecoveryCodeIndexes().OrderBy(i => i));
            Assert.Equal(3, codes.GetRecoveryCodes().Count);
        }

        [Fact]
        public void Sections_keep_their_order_through_a_json_round_trip()
        {
            var original = WithSections();
            var restored = JsonSerializer.Deserialize<Credential>(JsonSerializer.Serialize(original));

            Assert.Equal(
                original.Sections.Select(s => s.SortOrder),
                restored!.Sections.Select(s => s.SortOrder));
        }

        [Fact]
        public async Task Sections_survive_a_json_export_and_import()
        {
            var service = new ImportExportService();
            var path = Path.Combine(Path.GetTempPath(), $"phantom-sections-{Guid.NewGuid():N}.json");

            try
            {
                await service.ExportToJsonAsync(new List<Credential> { WithSections() }, path);
                var imported = await service.ImportFromJsonAsync(path);

                var credential = Assert.Single(imported);
                Assert.Equal(3, credential.Sections.Count);
                Assert.Contains(credential.Sections, s => s.Kind == EntrySectionKind.RecoveryCodes && s.IsSecret);
                Assert.Contains(credential.Sections, s => s.IsLinked && s.LinkedEntryId == "linked-entry-id");
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        private static Credential WithSections()
        {
            var note = EntrySection.CreateInline(EntrySectionKind.Note, "A note", "note body");
            note.SortOrder = 0;

            var totp = EntrySection.CreateLink(EntrySectionKind.Totp, "linked-entry-id", "Linked 2FA");
            totp.SetMeta(EntrySection.MetaTotpDigits, "8");
            totp.SetMeta(EntrySection.MetaTotpPeriod, "60");
            totp.SortOrder = 1;

            var codes = EntrySection.CreateInline(EntrySectionKind.RecoveryCodes, "Codes", "aaa\nbbb\nccc");
            codes.SetUsedRecoveryCodeIndexes(new[] { 0, 2 });
            codes.SortOrder = 2;

            return new Credential
            {
                EntryType = EntryType.Password,
                Title = "GitHub",
                Username = "me@example.com",
                Url = "https://github.com",
                Sections = new List<EntrySection> { note, totp, codes }
            };
        }
    }
}
