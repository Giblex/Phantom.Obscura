using System;
using System.Collections.Generic;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services.Autofill;
using Xunit;

namespace PhantomVault.Core.Tests.Services;

/// <summary>
/// The gate in front of the browser passkey relay. Its job is to refuse anything that was
/// never going to be legitimate BEFORE it reaches Attestor, because reaching Attestor raises a
/// Windows Hello prompt — and an attacker able to provoke prompts at will can train a user to
/// approve them.
/// </summary>
public sealed class PasskeyAssertionGateTests
{
    private static Credential Passkey(string url, string reference = "ref-1", DateTime? lastUsed = null) =>
        new()
        {
            Title = url,
            Url = url,
            IsPasskey = true,
            AttestorPasskeyReference = reference,
            LastUsedUtc = lastUsed
        };

    private const string ClientData = "{\"type\":\"webauthn.get\",\"challenge\":\"abc\"}";

    [Fact]
    public void Allows_a_passkey_registered_for_the_site()
    {
        var vault = new List<Credential> { Passkey("https://example.com") };

        var decision = PasskeyAssertionGate.Evaluate(vault, "https://example.com", "example.com", ClientData);

        Assert.True(decision.IsAllowed);
        Assert.Equal("ref-1", decision.Credential!.AttestorPasskeyReference);
        Assert.Equal("example.com", decision.RelyingPartyId);
    }

    [Fact]
    public void Allows_a_subdomain_page_to_use_a_registrable_domain_passkey()
    {
        var vault = new List<Credential> { Passkey("https://login.example.com") };

        var decision = PasskeyAssertionGate.Evaluate(vault, "https://login.example.com", "example.com", ClientData);

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void Refuses_a_site_asking_for_someone_elses_relying_party()
    {
        // The whole point of the gate: evil.test must not obtain an assertion for example.com,
        // which it could replay to the real site.
        var vault = new List<Credential> { Passkey("https://example.com") };

        var decision = PasskeyAssertionGate.Evaluate(vault, "https://evil.test", "example.com", ClientData);

        Assert.Equal(PasskeyAssertionGate.Outcome.OriginNotEntitled, decision.Outcome);
        Assert.Null(decision.Credential);
    }

    [Fact]
    public void Refuses_an_insecure_origin()
    {
        var vault = new List<Credential> { Passkey("https://example.com") };

        var decision = PasskeyAssertionGate.Evaluate(vault, "http://example.com", "example.com", ClientData);

        Assert.Equal(PasskeyAssertionGate.Outcome.OriginNotEntitled, decision.Outcome);
    }

    [Fact]
    public void Refuses_when_clientDataJson_is_absent()
    {
        // Without it there is nothing to sign over, and a signature made over anything we
        // invented ourselves would be unverifiable by the relying party.
        var vault = new List<Credential> { Passkey("https://example.com") };

        Assert.Equal(PasskeyAssertionGate.Outcome.MissingClientData,
            PasskeyAssertionGate.Evaluate(vault, "https://example.com", "example.com", null).Outcome);
        Assert.Equal(PasskeyAssertionGate.Outcome.MissingClientData,
            PasskeyAssertionGate.Evaluate(vault, "https://example.com", "example.com", "   ").Outcome);
    }

    [Fact]
    public void Refuses_when_the_vault_holds_no_passkey_for_the_site()
    {
        var vault = new List<Credential> { Passkey("https://other.test") };

        var decision = PasskeyAssertionGate.Evaluate(vault, "https://example.com", "example.com", ClientData);

        Assert.Equal(PasskeyAssertionGate.Outcome.NoMatchingPasskey, decision.Outcome);
    }

    [Fact]
    public void Ignores_password_entries_and_passkeys_with_no_authenticator_handle()
    {
        // A password credential for the same site is not a passkey, and a passkey row with no
        // Attestor reference has nothing to assert with. Either one being picked would surface
        // as a confusing failure after a Hello prompt.
        var vault = new List<Credential>
        {
            new() { Title = "pw", Url = "https://example.com", IsPasskey = false, AttestorPasskeyReference = "ref-pw" },
            new() { Title = "orphan", Url = "https://example.com", IsPasskey = true, AttestorPasskeyReference = null }
        };

        var decision = PasskeyAssertionGate.Evaluate(vault, "https://example.com", "example.com", ClientData);

        Assert.Equal(PasskeyAssertionGate.Outcome.NoMatchingPasskey, decision.Outcome);
    }

    [Fact]
    public void Prefers_the_most_recently_used_passkey_when_several_match()
    {
        // The site may send allowCredentials, but the vault does not record WebAuthn credential
        // IDs, so it cannot filter on them. Picking the one the user actually signs in with is
        // the best available guess; the extension re-checks the returned id against
        // allowCredentials and falls back to the browser if it was the wrong one.
        var older = Passkey("https://example.com", "ref-old", new DateTime(2026, 1, 1));
        var newer = Passkey("https://example.com", "ref-new", new DateTime(2026, 6, 1));
        var never = Passkey("https://example.com", "ref-never");

        var decision = PasskeyAssertionGate.Evaluate(
            new List<Credential> { never, older, newer }, "https://example.com", "example.com", ClientData);

        Assert.True(decision.IsAllowed);
        Assert.Equal("ref-new", decision.Credential!.AttestorPasskeyReference);
    }

    [Fact]
    public void Defaults_a_blank_relying_party_to_the_origins_host()
    {
        // WebAuthn: an omitted rpId means "this origin's effective domain".
        var vault = new List<Credential> { Passkey("https://example.com") };

        var decision = PasskeyAssertionGate.Evaluate(vault, "https://example.com", null, ClientData);

        Assert.True(decision.IsAllowed);
        Assert.Equal("example.com", decision.RelyingPartyId);
    }

    [Fact]
    public void Matches_credentials_stored_as_a_bare_host()
    {
        // Stored URLs are user-entered and often lack a scheme. Discarding those would silently
        // make working passkeys unusable from the browser.
        var vault = new List<Credential> { Passkey("example.com") };

        var decision = PasskeyAssertionGate.Evaluate(vault, "https://example.com", "example.com", ClientData);

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void An_empty_vault_is_refused_rather_than_throwing()
    {
        Assert.Equal(PasskeyAssertionGate.Outcome.NoMatchingPasskey,
            PasskeyAssertionGate.Evaluate(new List<Credential>(), "https://example.com", "example.com", ClientData).Outcome);
        Assert.Equal(PasskeyAssertionGate.Outcome.NoMatchingPasskey,
            PasskeyAssertionGate.Evaluate(null, "https://example.com", "example.com", ClientData).Outcome);
    }

    [Fact]
    public void Every_refusal_has_a_message_that_leaks_no_vault_contents()
    {
        foreach (var outcome in Enum.GetValues<PasskeyAssertionGate.Outcome>())
        {
            var message = PasskeyAssertionGate.DescribeRefusal(outcome);
            Assert.False(string.IsNullOrWhiteSpace(message));
            Assert.DoesNotContain("ref-", message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
