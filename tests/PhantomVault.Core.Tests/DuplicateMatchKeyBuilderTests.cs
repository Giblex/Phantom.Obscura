using System;
using System.Collections.Generic;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests
{
    public class DuplicateMatchKeyBuilderTests
    {
        [Theory]
        [InlineData("https://github.com/login", "github.com")]
        [InlineData("http://www.github.com", "github.com")]
        [InlineData("github.com", "github.com")]
        [InlineData("https://GitHub.com:443/user/settings?tab=x", "github.com")]
        [InlineData("www.github.com/", "github.com")]
        public void NormalizeUrlToHost_collapses_scheme_www_port_and_path(string input, string expected)
        {
            Assert.Equal(expected, DuplicateMatchKeyBuilder.NormalizeUrlToHost(input));
        }

        [Fact]
        public void NormalizeUrlToHost_returns_empty_for_blank()
        {
            Assert.Equal(string.Empty, DuplicateMatchKeyBuilder.NormalizeUrlToHost(null));
            Assert.Equal(string.Empty, DuplicateMatchKeyBuilder.NormalizeUrlToHost("   "));
        }

        [Theory]
        [InlineData("GitHub Login", "github")]
        [InlineData("github", "github")]
        [InlineData("My  Bank - Account", "my bank")]
        public void NormalizeTitle_strips_case_punctuation_and_filler(string input, string expected)
        {
            Assert.Equal(expected, DuplicateMatchKeyBuilder.NormalizeTitle(input));
        }

        [Fact]
        public void NormalizeTitle_keeps_text_when_every_word_is_filler()
        {
            // "Login" alone must not normalise to an empty identity, or unrelated
            // entries titled "Login" would all collapse into one duplicate group.
            Assert.Equal("login", DuplicateMatchKeyBuilder.NormalizeTitle("Login"));
        }

        [Theory]
        [InlineData("Me@Example.COM", "me@example.com")]
        [InlineData("me+shopping@example.com", "me@example.com")]
        [InlineData("  AJ  ", "aj")]
        public void NormalizeAccount_folds_case_and_plus_addressing(string input, string expected)
        {
            Assert.Equal(expected, DuplicateMatchKeyBuilder.NormalizeAccount(input));
        }

        [Fact]
        public void Build_groups_same_account_across_differently_written_urls()
        {
            var a = Login("GitHub", "me@example.com", "https://github.com/login");
            var b = Login("Github Account", "Me@Example.com", "www.github.com");

            Assert.Equal(DuplicateMatchKeyBuilder.Build(a), DuplicateMatchKeyBuilder.Build(b));
        }

        [Fact]
        public void Build_keeps_different_accounts_on_the_same_site_apart()
        {
            var a = Login("GitHub", "me@example.com", "https://github.com");
            var b = Login("GitHub", "work@example.com", "https://github.com");

            Assert.NotEqual(DuplicateMatchKeyBuilder.Build(a), DuplicateMatchKeyBuilder.Build(b));
        }

        [Fact]
        public void Build_keeps_different_entry_types_apart()
        {
            var login = Login("Chase", "me@example.com", "https://chase.com");
            var pin = new Credential
            {
                EntryType = EntryType.PinCode,
                Title = "Chase",
                PinIssuer = "Chase",
                PinCategory = "me@example.com"
            };

            Assert.NotEqual(DuplicateMatchKeyBuilder.Build(login), DuplicateMatchKeyBuilder.Build(pin));
        }

        [Fact]
        public void DetermineStrength_reports_exact_for_identical_surface_fields()
        {
            var a = Login("GitHub", "me@example.com", "https://github.com");
            var b = Login("GitHub", "me@example.com", "https://github.com");

            Assert.Equal(DuplicateMatchStrength.Exact,
                DuplicateMatchKeyBuilder.DetermineStrength(new List<Credential> { a, b }));
        }

        [Fact]
        public void DetermineStrength_reports_strong_when_host_and_account_agree()
        {
            var a = Login("GitHub", "me@example.com", "https://github.com/login");
            var b = Login("Github Account", "me@example.com", "www.github.com");

            Assert.Equal(DuplicateMatchStrength.Strong,
                DuplicateMatchKeyBuilder.DetermineStrength(new List<Credential> { a, b }));
        }

        [Fact]
        public void DetermineStrength_reports_likely_when_only_titles_line_up()
        {
            var a = Login("GitHub", string.Empty, string.Empty);
            var b = Login("Github Login", string.Empty, string.Empty);

            Assert.Equal(DuplicateMatchStrength.Likely,
                DuplicateMatchKeyBuilder.DetermineStrength(new List<Credential> { a, b }));
        }

        private static Credential Login(string title, string username, string url) => new()
        {
            EntryType = EntryType.Password,
            Title = title,
            Username = username,
            Url = url
        };
    }
}
