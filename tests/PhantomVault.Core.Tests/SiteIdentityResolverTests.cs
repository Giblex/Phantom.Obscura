using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests
{
    public class SiteIdentityResolverTests
    {
        [Theory]
        [InlineData("example.com", "example.com")]
        [InlineData("www.example.com", "example.com")]
        [InlineData("a.b.c.example.com", "example.com")]
        [InlineData("example.co.uk", "example.co.uk")]
        [InlineData("www.example.co.uk", "example.co.uk")]
        [InlineData("shop.example.com.au", "example.com.au")]
        [InlineData("example.co.jp", "example.co.jp")]
        public void GetRegistrableDomain_respects_multi_label_public_suffixes(string host, string expected)
        {
            Assert.Equal(expected, SiteIdentityResolver.GetRegistrableDomain(host));
        }

        [Fact]
        public void GetRegistrableDomain_never_reduces_to_a_bare_public_suffix()
        {
            // The bug this guards: treating "co.uk" as the site would merge every
            // unrelated UK account in the vault into one duplicate group.
            Assert.NotEqual("co.uk", SiteIdentityResolver.GetRegistrableDomain("bank.co.uk"));
            Assert.Equal("bank.co.uk", SiteIdentityResolver.GetRegistrableDomain("bank.co.uk"));
        }

        [Theory]
        [InlineData("login.example.com", "example.com")]
        [InlineData("accounts.example.com", "example.com")]
        [InlineData("secure.my.example.com", "example.com")]
        [InlineData("sso.example.co.uk", "example.co.uk")]
        public void StripAuthSubdomains_removes_sign_in_hosts(string host, string expected)
        {
            Assert.Equal(expected, SiteIdentityResolver.StripAuthSubdomains(host));
        }

        [Fact]
        public void StripAuthSubdomains_stops_at_the_registrable_domain()
        {
            // "my.com" is a site in its own right; stripping "my" would destroy it.
            Assert.Equal("my.com", SiteIdentityResolver.StripAuthSubdomains("my.com"));
        }

        [Theory]
        [InlineData("https://github.com/login")]
        [InlineData("http://www.github.com")]
        [InlineData("github.com")]
        [InlineData("https://GitHub.com:443/user/settings?tab=x")]
        public void FromUrl_resolves_the_same_site_however_the_url_is_written(string url)
        {
            var site = SiteIdentityResolver.FromUrl(url);

            Assert.Equal("github.com", site.RegistrableDomain);
            Assert.Equal("github", site.SiteFamily);
        }

        [Fact]
        public void FromUrl_ignores_text_that_is_not_a_site()
        {
            Assert.False(SiteIdentityResolver.FromUrl("My Bank Account").HasSite);
            Assert.False(SiteIdentityResolver.FromUrl("Amazon").HasSite);
            Assert.False(SiteIdentityResolver.FromUrl(null).HasSite);
        }

        [Fact]
        public void Affiliated_properties_share_a_site_family()
        {
            Assert.Equal("google", SiteIdentityResolver.FromUrl("mail.google.com").SiteFamily);
            Assert.Equal("google", SiteIdentityResolver.FromUrl("youtube.com").SiteFamily);
            Assert.Equal("amazon", SiteIdentityResolver.FromUrl("amazon.co.uk").SiteFamily);
            Assert.Equal("amazon", SiteIdentityResolver.FromUrl("www.amazon.com").SiteFamily);
        }

        [Fact]
        public void Unaffiliated_domains_keep_their_own_family()
        {
            var a = SiteIdentityResolver.FromUrl("example.com");
            var b = SiteIdentityResolver.FromUrl("example.co.uk");

            Assert.NotEqual(a.SiteFamily, b.SiteFamily);
        }

        [Fact]
        public void Duplicate_key_groups_the_same_account_across_url_spellings()
        {
            var a = Login("GitHub", "me@example.com", "https://github.com/login");
            var b = Login("Github", "me@example.com", "www.github.com");
            var c = Login("GH", "me@example.com", "https://login.github.com/session");

            var keyA = DuplicateMatchKeyBuilder.Build(a);

            Assert.Equal(keyA, DuplicateMatchKeyBuilder.Build(b));
            Assert.Equal(keyA, DuplicateMatchKeyBuilder.Build(c));
        }

        [Fact]
        public void Duplicate_key_keeps_separate_accounts_on_one_site_apart()
        {
            var personal = Login("GitHub", "me@example.com", "https://github.com");
            var work = Login("GitHub", "work@example.com", "https://github.com");

            Assert.NotEqual(
                DuplicateMatchKeyBuilder.Build(personal),
                DuplicateMatchKeyBuilder.Build(work));
        }

        [Fact]
        public void Duplicate_key_keeps_unrelated_sites_apart()
        {
            var a = Login("Bank", "me@example.com", "https://bank-a.co.uk");
            var b = Login("Bank", "me@example.com", "https://bank-b.co.uk");

            Assert.NotEqual(
                DuplicateMatchKeyBuilder.Build(a),
                DuplicateMatchKeyBuilder.Build(b));
        }

        [Fact]
        public void Card_and_bank_entries_are_not_grouped_by_website()
        {
            // Two different cards issued by the same bank must never look like duplicates
            // just because they share the bank's URL.
            var cardA = new Credential
            {
                EntryType = EntryType.CreditCard,
                Title = "Visa",
                Url = "https://bank.com",
                CardholderName = "A Halliday",
                CardType = "Visa"
            };

            var cardB = new Credential
            {
                EntryType = EntryType.CreditCard,
                Title = "Mastercard",
                Url = "https://bank.com",
                CardholderName = "J Smith",
                CardType = "Mastercard"
            };

            Assert.NotEqual(
                DuplicateMatchKeyBuilder.Build(cardA),
                DuplicateMatchKeyBuilder.Build(cardB));
        }

        [Fact]
        public void Site_metadata_is_attached_to_the_key_for_organisation()
        {
            var key = DuplicateMatchKeyBuilder.Build(Login("GitHub", "me@example.com", "https://github.com"));

            Assert.True(key.HasSite);
            Assert.Equal("github", key.SiteFamily);
            Assert.Equal("Github", key.SiteDisplayName);
        }

        [Fact]
        public void Entries_without_a_site_report_no_site()
        {
            var pin = new Credential
            {
                EntryType = EntryType.PinCode,
                Title = "Front door",
                PinLabel = "Front door"
            };

            Assert.False(DuplicateMatchKeyBuilder.Build(pin).HasSite);
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
