using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests
{
    public class PasswordHealthSectionTests
    {
        private const string StrongA = "7Gq!vX2mZr#9TpLw4Ke";
        private const string StrongB = "Q4z@Nf8Vb!3JhRt6Wy";
        private const string Weak = "password1";

        [Fact]
        public async Task Entry_passwords_are_still_audited()
        {
            var report = await Analyze(Login("GitHub", StrongA), Login("Weak site", Weak));

            Assert.Equal(2, report.TotalCredentials);
            Assert.Equal(2, report.AnalyzedSecretCount);
            Assert.Contains("Weak site", report.WeakTitles);
            Assert.DoesNotContain("GitHub", report.WeakTitles);
        }

        [Fact]
        public async Task Secret_sections_are_audited_and_reported_under_their_own_label()
        {
            // The concrete gap: a password preserved from a duplicate merge lives in a
            // secret section and was previously invisible to the health report.
            var credential = Login("GitHub", StrongA);
            credential.Sections = new List<EntrySection>
            {
                EntrySection.CreateInline(EntrySectionKind.Secret, "Password (from merged copy)", Weak)
            };

            var report = await Analyze(credential);

            Assert.Equal(1, report.TotalCredentials);
            Assert.Equal(2, report.AnalyzedSecretCount);
            Assert.Contains("GitHub › Password (from merged copy)", report.WeakTitles);
        }

        [Fact]
        public async Task Reuse_is_detected_between_an_entry_password_and_a_secret_section()
        {
            var a = Login("GitHub", StrongA);

            var b = Login("GitLab", StrongB);
            b.Sections = new List<EntrySection>
            {
                EntrySection.CreateInline(EntrySectionKind.Secret, "Old password", StrongA)
            };

            var report = await Analyze(a, b);

            Assert.Equal(1, report.ReusedCount);
            Assert.Contains("GitHub", report.ReusedTitles);
            Assert.Contains("GitLab › Old password", report.ReusedTitles);
        }

        [Fact]
        public async Task Pin_totp_and_recovery_code_sections_are_not_audited()
        {
            // These would be reported weak every time and drown the actionable findings.
            var credential = Login("Bank", StrongA);
            credential.Sections = new List<EntrySection>
            {
                EntrySection.CreateInline(EntrySectionKind.PinCode, "Card PIN", "1234"),
                EntrySection.CreateInline(EntrySectionKind.Totp, "2FA", "JBSWY3DPEHPK3PXP"),
                EntrySection.CreateInline(EntrySectionKind.RecoveryCodes, "Codes", "aaa\nbbb")
            };

            var report = await Analyze(credential);

            Assert.Equal(1, report.AnalyzedSecretCount);
            Assert.Empty(report.WeakTitles);
        }

        [Fact]
        public async Task Linked_secret_sections_are_not_double_counted()
        {
            // The linked entry is audited in its own right, so auditing the link too
            // would report the same secret twice and invent a false reuse pair.
            var target = Login("Shared secret", StrongA);

            var credential = Login("GitHub", StrongB);
            credential.Sections = new List<EntrySection>
            {
                EntrySection.CreateLink(EntrySectionKind.Secret, target.Id, "Linked secret")
            };

            var report = await Analyze(credential, target);

            Assert.Equal(2, report.AnalyzedSecretCount);
            Assert.Equal(0, report.ReusedCount);
        }

        [Fact]
        public async Task Average_entropy_is_measured_across_every_audited_secret()
        {
            var credential = Login("GitHub", StrongA);
            credential.Sections = new List<EntrySection>
            {
                EntrySection.CreateInline(EntrySectionKind.Secret, "Old password", Weak)
            };

            var report = await Analyze(credential);

            var strongOnly = await Analyze(Login("GitHub", StrongA));

            Assert.Equal(2, report.AnalyzedSecretCount);
            Assert.True(report.AverageEntropy < strongOnly.AverageEntropy,
                "a weak secret section should drag the average down");
        }

        [Fact]
        public async Task Age_is_still_counted_once_per_entry_not_once_per_secret()
        {
            var credential = Login("GitHub", StrongA);
            credential.LastUpdatedUtc = DateTimeOffset.UtcNow.AddDays(-400);
            credential.Sections = new List<EntrySection>
            {
                EntrySection.CreateInline(EntrySectionKind.Secret, "Old password", StrongB),
                EntrySection.CreateInline(EntrySectionKind.Secret, "Older password", Weak)
            };

            var report = await Analyze(credential);

            Assert.Equal(1, report.OldCount);
            Assert.Equal(new[] { "GitHub" }, report.OldTitles);
        }

        [Fact]
        public async Task Breach_check_covers_secret_sections()
        {
            var credential = Login("GitHub", StrongA);
            credential.Sections = new List<EntrySection>
            {
                EntrySection.CreateInline(EntrySectionKind.Secret, "Old password", StrongB)
            };

            // Treat every lookup as breached so both secrets are reported.
            var service = new PasswordHealthService(
                checkBreaches: true,
                hibpLookup: (_, _) => Task.FromResult(5));

            var report = await service.AnalyzeAsync(new[] { credential });

            Assert.True(report.BreachCheckPerformed);
            Assert.Equal(2, report.BreachedCount);
            Assert.Contains("GitHub", report.BreachedTitles);
            Assert.Contains("GitHub › Old password", report.BreachedTitles);
        }

        [Fact]
        public async Task An_empty_vault_reports_nothing_rather_than_dividing_by_zero()
        {
            var report = await new PasswordHealthService().AnalyzeAsync(Array.Empty<Credential>());

            Assert.Equal(0, report.TotalCredentials);
            Assert.Equal(0, report.AnalyzedSecretCount);
            Assert.Equal(0, report.AverageEntropy);
        }

        private static async Task<PasswordHealthReport> Analyze(params Credential[] credentials)
            => await new PasswordHealthService().AnalyzeAsync(credentials);

        private static Credential Login(string title, string password) => new()
        {
            EntryType = EntryType.Password,
            Title = title,
            Username = "me@example.com",
            Url = "https://example.com",
            Password = password,
            LastUpdatedUtc = DateTimeOffset.UtcNow
        };
    }
}
