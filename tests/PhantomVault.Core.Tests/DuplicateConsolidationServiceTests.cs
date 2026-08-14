using System;
using System.Collections.Generic;
using System.Linq;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests
{
    public class DuplicateConsolidationServiceTests
    {
        private readonly DuplicateConsolidationService _service = new();

        [Fact]
        public void Consolidate_fills_empty_fields_from_the_absorbed_copies()
        {
            var primary = Login("GitHub", "me@example.com", url: "", notes: "");
            primary.Tags = new List<string> { "work" };

            var other = Login("GitHub", "me@example.com", url: "https://github.com", notes: "recovery in safe");
            other.Tags = new List<string> { "dev" };

            var result = _service.Consolidate(new[] { primary, other }, primary.Id);

            Assert.Equal("https://github.com", result.Consolidated.Url);
            Assert.Contains("recovery in safe", result.Consolidated.Notes);
            Assert.Equal(new[] { "work", "dev" }, result.Consolidated.Tags);
        }

        [Fact]
        public void Consolidate_keeps_the_primary_value_and_records_the_conflict()
        {
            var primary = Login("GitHub", "me@example.com", "https://github.com", "");
            primary.Password = "keep-me";

            var other = Login("GitHub", "me@example.com", "https://github.com", "");
            other.Password = "discard-me";

            var result = _service.Consolidate(new[] { primary, other }, primary.Id);

            Assert.Equal("keep-me", result.Consolidated.Password);
            Assert.True(result.HasConflicts);

            var conflict = Assert.Single(result.Conflicts, c => c.FieldName == "Password");
            Assert.Equal("keep-me", conflict.KeptValue);
            Assert.Contains("discard-me", conflict.DiscardedValues);
        }

        [Fact]
        public void Consolidate_preserves_a_discarded_password_as_a_secret_section()
        {
            var primary = Login("GitHub", "me@example.com", "https://github.com", "");
            primary.Password = "keep-me";

            var other = Login("GitHub", "me@example.com", "https://github.com", "");
            other.Password = "discard-me";

            var result = _service.Consolidate(new[] { primary, other }, primary.Id);

            var preserved = Assert.Single(
                result.Consolidated.Sections,
                s => s.Kind == EntrySectionKind.Secret && s.Label.StartsWith("Password"));

            Assert.Equal("discard-me", preserved.Value);
            Assert.True(preserved.IsSecret);
        }

        [Fact]
        public void Consolidate_does_not_preserve_conflicts_on_cosmetic_fields()
        {
            var primary = Login("GitHub", "me@example.com", "https://github.com", "");
            primary.Icon = "a.png";

            var other = Login("GitHub", "me@example.com", "https://github.com", "");
            other.Icon = "b.png";

            var result = _service.Consolidate(new[] { primary, other }, primary.Id);

            Assert.Empty(result.Consolidated.Sections);
        }

        [Fact]
        public void Consolidate_does_not_report_a_conflict_when_values_agree()
        {
            var primary = Login("GitHub", "me@example.com", "https://github.com", "");
            primary.Password = "same";

            var other = Login("GitHub", "me@example.com", "https://github.com", "");
            other.Password = "same";

            var result = _service.Consolidate(new[] { primary, other }, primary.Id);

            Assert.False(result.HasConflicts);
        }

        [Fact]
        public void Consolidate_unions_sections_and_drops_exact_repeats()
        {
            var primary = Login("GitHub", "me@example.com", "https://github.com", "");
            primary.Sections = new List<EntrySection>
            {
                EntrySection.CreateInline(EntrySectionKind.RecoveryEmail, "Recovery email", "rescue@example.com")
            };

            var other = Login("GitHub", "me@example.com", "https://github.com", "");
            other.Sections = new List<EntrySection>
            {
                EntrySection.CreateInline(EntrySectionKind.RecoveryEmail, "Recovery email", "rescue@example.com"),
                EntrySection.CreateInline(EntrySectionKind.Note, "Note", "kept in the drawer")
            };

            var result = _service.Consolidate(new[] { primary, other }, primary.Id);

            Assert.Equal(2, result.Consolidated.Sections.Count);
            Assert.Contains(result.Consolidated.Sections, s => s.Kind == EntrySectionKind.Note);
            Assert.Equal(new[] { 0, 1 }, result.Consolidated.Sections.Select(s => s.SortOrder));
        }

        [Fact]
        public void Consolidate_keeps_the_earliest_creation_date()
        {
            var older = Login("GitHub", "me@example.com", "https://github.com", "");
            older.CreatedUtc = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

            var newer = Login("GitHub", "me@example.com", "https://github.com", "");
            newer.CreatedUtc = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);

            var result = _service.Consolidate(new[] { newer, older }, newer.Id);

            Assert.Equal(older.CreatedUtc, result.Consolidated.CreatedUtc);
        }

        [Fact]
        public void Consolidate_marks_the_result_favourite_if_any_copy_was()
        {
            var primary = Login("GitHub", "me@example.com", "https://github.com", "");
            var other = Login("GitHub", "me@example.com", "https://github.com", "");
            other.IsFavorite = true;

            var result = _service.Consolidate(new[] { primary, other }, primary.Id);

            Assert.True(result.Consolidated.IsFavorite);
        }

        [Fact]
        public void Consolidate_does_not_duplicate_identical_notes()
        {
            var primary = Login("GitHub", "me@example.com", "https://github.com", "same note");
            var other = Login("GitHub", "me@example.com", "https://github.com", "same note");

            var result = _service.Consolidate(new[] { primary, other }, primary.Id);

            Assert.Equal("same note", result.Consolidated.Notes);
        }

        [Fact]
        public void Consolidate_keeps_the_primary_id_so_the_retained_entry_survives()
        {
            var primary = Login("GitHub", "me@example.com", "https://github.com", "");
            var other = Login("GitHub", "me@example.com", "https://github.com", "");

            var result = _service.Consolidate(new[] { primary, other }, primary.Id);

            Assert.Equal(primary.Id, result.Consolidated.Id);
            Assert.Equal(other.Id, Assert.Single(result.Absorbed).Id);
        }

        [Fact]
        public void SelectPrimary_prefers_the_richer_entry_when_none_is_pinned()
        {
            var sparse = Login("GitHub", "", "", "");
            var rich = Login("GitHub", "me@example.com", "https://github.com", "notes here");

            var chosen = DuplicateConsolidationService.SelectPrimary(new[] { sparse, rich }, null);

            Assert.Same(rich, chosen);
        }

        [Fact]
        public void Consolidate_throws_on_an_empty_group()
        {
            Assert.Throws<ArgumentException>(() => _service.Consolidate(Array.Empty<Credential>()));
        }

        private static Credential Login(string title, string username, string url, string notes) => new()
        {
            EntryType = EntryType.Password,
            Title = title,
            Username = username,
            Url = url,
            Notes = notes
        };
    }
}
