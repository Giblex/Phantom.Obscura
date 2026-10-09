using PhantomVault.UI.Services.AutoFill;
using Xunit;

namespace PhantomVault.UI.Tests
{
    /// <summary>
    /// The auto-fill domain whitelist.
    ///
    /// This setting existed in the UI for a long time without being read by anything, so it
    /// restricted nothing. Now that it does, the default behaviour matters more than the
    /// restriction: an empty list has to mean "no restriction". Inverting that would silently
    /// disable auto-fill for every user who never opened the setting.
    /// </summary>
    public sealed class AutoFillDomainWhitelistTests
    {
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void An_empty_whitelist_restricts_nothing(string? whitelist)
        {
            Assert.True(AutoFillOrchestrator.IsDomainAllowed("example.com", whitelist));
            Assert.True(AutoFillOrchestrator.IsDomainAllowed("anything.co.uk", whitelist));
        }

        [Fact]
        public void A_listed_domain_is_allowed_and_an_unlisted_one_is_not()
        {
            const string list = "example.com, bank.co.uk";

            Assert.True(AutoFillOrchestrator.IsDomainAllowed("example.com", list));
            Assert.True(AutoFillOrchestrator.IsDomainAllowed("bank.co.uk", list));
            Assert.False(AutoFillOrchestrator.IsDomainAllowed("phishing.test", list));
        }

        [Fact]
        public void Listing_a_domain_covers_its_subdomains()
        {
            // Otherwise the user would have to enumerate every subdomain a site uses, and the
            // first one they missed would look like auto-fill being broken.
            Assert.True(AutoFillOrchestrator.IsDomainAllowed("accounts.example.com", "example.com"));
            Assert.True(AutoFillOrchestrator.IsDomainAllowed("login.secure.example.com", "example.com"));
        }

        [Fact]
        public void A_lookalike_domain_is_not_allowed_by_a_suffix_match()
        {
            // "notexample.com" ends with "example.com" as a string. Matching on the dot boundary
            // is what stops a lookalike domain inheriting a real one's permission.
            Assert.False(AutoFillOrchestrator.IsDomainAllowed("notexample.com", "example.com"));
            Assert.False(AutoFillOrchestrator.IsDomainAllowed("example.com.evil.test", "example.com"));
        }

        [Fact]
        public void Entries_are_tolerant_of_how_people_actually_type_them()
        {
            Assert.True(AutoFillOrchestrator.IsDomainAllowed("example.com", "*.example.com"));
            Assert.True(AutoFillOrchestrator.IsDomainAllowed("example.com", ".example.com"));
            Assert.True(AutoFillOrchestrator.IsDomainAllowed("EXAMPLE.com", "example.com"));
            Assert.True(AutoFillOrchestrator.IsDomainAllowed("example.com", "foo.test; example.com"));
        }

        [Fact]
        public void With_a_whitelist_configured_an_unknown_domain_is_refused()
        {
            Assert.False(AutoFillOrchestrator.IsDomainAllowed(null, "example.com"));
            Assert.False(AutoFillOrchestrator.IsDomainAllowed("", "example.com"));
        }
    }
}
