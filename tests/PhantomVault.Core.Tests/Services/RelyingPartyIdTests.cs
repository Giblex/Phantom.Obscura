using PhantomVault.Core.Services.Autofill;
using Xunit;

namespace PhantomVault.Core.Tests.Services;

/// <summary>
/// The rule that decides whether a page may obtain a passkey assertion for a given relying
/// party. A defect here is a credential-theft vector — an assertion scoped to a bank, handed
/// to a page that is not the bank, is replayable — so the interesting cases are the near
/// misses rather than the happy path.
/// </summary>
public sealed class RelyingPartyIdTests
{
    [Theory]
    [InlineData("example.com", "example.com")]              // exact
    [InlineData("login.example.com", "example.com")]        // registrable suffix
    [InlineData("a.b.example.com", "example.com")]          // deeper subdomain
    [InlineData("EXAMPLE.com", "example.COM")]              // case-insensitive
    [InlineData("example.com.", "example.com")]             // trailing root dot
    public void Permits_a_relying_party_that_covers_the_site(string observed, string stored)
    {
        Assert.Equal("example.com", RelyingPartyId.Resolve(observed, stored));
    }

    [Theory]
    [InlineData("notexample.com", "example.com")]           // suffix without the dot boundary
    [InlineData("example.com.evil.test", "example.com")]    // RP as a left-hand label
    [InlineData("evil.test", "example.com")]                // unrelated
    [InlineData("example.co", "example.com")]               // truncated
    [InlineData("xexample.com", "example.com")]
    public void Refuses_a_relying_party_the_site_does_not_own(string observed, string stored)
    {
        Assert.Null(RelyingPartyId.Resolve(observed, stored));
    }

    [Fact]
    public void A_site_with_no_recorded_relying_party_asserts_only_for_itself()
    {
        Assert.Equal("example.com", RelyingPartyId.Resolve("example.com", null));
        Assert.Equal("example.com", RelyingPartyId.Resolve("example.com", ""));
    }

    [Fact]
    public void With_no_observed_domain_the_stored_value_stands()
    {
        // The native (non-browser) path has no page to compare against.
        Assert.Equal("example.com", RelyingPartyId.Resolve(null, "example.com"));
        Assert.Null(RelyingPartyId.Resolve(null, null));
    }

    [Theory]
    [InlineData("https://example.com", "example.com")]
    [InlineData("https://login.example.com", "example.com")]
    [InlineData("http://localhost:3000", "localhost")]
    public void Permits_secure_origins_that_cover_the_relying_party(string origin, string rpId)
    {
        Assert.True(RelyingPartyId.IsPermittedForOrigin(origin, rpId));
    }

    [Theory]
    [InlineData("http://example.com", "example.com")]       // WebAuthn requires a secure context
    [InlineData("https://evil.test", "example.com")]        // origin does not cover the RP
    [InlineData("https://notexample.com", "example.com")]
    [InlineData("file:///tmp/page.html", "example.com")]
    [InlineData("not-a-url", "example.com")]
    [InlineData("", "example.com")]
    [InlineData("https://example.com", "")]
    public void Refuses_insecure_or_mismatched_origins(string origin, string rpId)
    {
        Assert.False(RelyingPartyId.IsPermittedForOrigin(origin, rpId));
    }

    [Fact]
    public void Plain_http_on_localhost_stays_usable_for_development()
    {
        // Treated as potentially trustworthy by the platform, so refusing it here would push
        // developers to weaken the rule rather than work with it.
        Assert.True(RelyingPartyId.IsPermittedForOrigin("http://localhost:8080", "localhost"));
        Assert.True(RelyingPartyId.IsPermittedForOrigin("http://app.localhost:8080", "localhost"));
    }
}
