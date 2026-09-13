using System;
using System.IO;
using PhantomVault.PrivilegedBroker;
using Xunit;

namespace PhantomVault.PrivilegedBroker.Tests;

/// <summary>
/// Covers the decision that gates every privileged operation the broker performs: whether the
/// process on the other end of the pipe is the allow-listed Obscura UI.
///
/// <para>
/// The broker runs elevated and mounts VHDXs on request, so the interesting property is not
/// that a legitimate client is admitted — it is that everything else is refused, including in
/// the degenerate states (no allow-list yet, unreadable config, a path that merely looks
/// similar). Each of those is a way in if it defaults open, so each is pinned here.
/// </para>
/// </summary>
public sealed class BrokerClientAuthorizationTests : IDisposable
{
    private readonly string _configDir;

    public BrokerClientAuthorizationTests()
    {
        _configDir = Path.Combine(Path.GetTempPath(), $"phantom-broker-cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_configDir);
        BrokerConfig.ConfigDirectoryOverride = _configDir;
    }

    public void Dispose()
    {
        BrokerConfig.ConfigDirectoryOverride = null;
        try { Directory.Delete(_configDir, recursive: true); } catch { /* best effort */ }
    }

    private string WriteFakeClient(string name = "PhantomVault.UI.exe")
    {
        var path = Path.Combine(_configDir, name);
        File.WriteAllText(path, "not a real executable");
        return path;
    }

    [Fact]
    public void No_allow_list_recorded_denies_everything()
    {
        // Before install records the UI executable there is no legitimate client, so the
        // broker must refuse rather than treat "nothing configured" as "anything goes".
        var client = WriteFakeClient();

        Assert.False(BrokerPipeServer.IsClientAllowed(client));
    }

    [Fact]
    public void Null_or_blank_client_path_is_denied()
    {
        BrokerConfig.SaveAllowedClient(WriteFakeClient(), "abc123");

        Assert.False(BrokerPipeServer.IsClientAllowed(null));
        Assert.False(BrokerPipeServer.IsClientAllowed(""));
        Assert.False(BrokerPipeServer.IsClientAllowed("   "));
    }

    [Fact]
    public void A_different_executable_is_denied()
    {
        BrokerConfig.SaveAllowedClient(WriteFakeClient(), "abc123");
        var impostor = WriteFakeClient("Impostor.exe");

        Assert.False(BrokerPipeServer.IsClientAllowed(impostor));
    }

    [Fact]
    public void A_path_that_only_shares_a_prefix_is_denied()
    {
        // "…\PhantomVault.UI.exe" vs "…\PhantomVault.UI.exe.bak" must not compare equal.
        // A prefix or StartsWith comparison here would admit a neighbouring file.
        BrokerConfig.SaveAllowedClient(WriteFakeClient(), "abc123");
        var lookalike = WriteFakeClient("PhantomVault.UI.exe.bak");

        Assert.False(BrokerPipeServer.IsClientAllowed(lookalike));
    }

    [Fact]
    public void An_unsigned_client_is_denied_once_a_signer_has_been_recorded()
    {
        // The recorded signer is enforced in every configuration. A Debug build relaxes only
        // the case where installation recorded NO signer at all; it must never downgrade a
        // pairing that did record one, or a developer machine would accept any binary sitting
        // at the allow-listed path.
        var client = WriteFakeClient();
        BrokerConfig.SaveAllowedClient(client, "0123456789abcdef");

        // The file is not Authenticode-signed, so it cannot match the recorded signer.
        Assert.False(BrokerPipeServer.IsClientAllowed(client));
    }

    [Fact]
    public void Equivalent_spellings_of_the_allowed_path_still_match()
    {
        // Canonicalisation must be permissive enough that the real UI is not locked out by a
        // redundant path segment — otherwise the broker is unusable and the pressure is to
        // loosen the check itself.
        var client = WriteFakeClient();
        BrokerConfig.SaveAllowedClient(client, signerSha256: "");

        var roundabout = Path.Combine(_configDir, ".", "PhantomVault.UI.exe");
        Assert.Equal(Path.GetFullPath(client), Path.GetFullPath(roundabout));

#if DEBUG
        // With no signer recorded, a Debug build admits the matching path.
        Assert.True(BrokerPipeServer.IsClientAllowed(roundabout));
#else
        // Release fails closed when no signer was recorded, regardless of path match.
        Assert.False(BrokerPipeServer.IsClientAllowed(roundabout));
#endif
    }

    [Fact]
    public void Round_trips_the_allow_list_it_saved()
    {
        var client = WriteFakeClient();
        BrokerConfig.SaveAllowedClient(client, "DEADBEEF");

        Assert.Equal(client, BrokerConfig.LoadAllowedClientPath());
        Assert.Equal("DEADBEEF", BrokerConfig.LoadAllowedClientSignerSha256());
    }

    [Fact]
    public void Missing_config_files_read_as_null_rather_than_throwing()
    {
        // The pipe accept loop calls these per connection; a throw here would take the
        // broker's listener down, which is a denial of service on every Obscura operation
        // that needs elevation.
        Assert.Null(BrokerConfig.LoadAllowedClientPath());
        Assert.Null(BrokerConfig.LoadAllowedClientSignerSha256());
        Assert.Null(BrokerConfig.LoadAllowedClientUserSid());
        Assert.Null(BrokerConfig.LoadManifestKeyPin());
    }

    [Fact]
    public void Blank_recorded_values_are_treated_as_absent()
    {
        // A truncated or whitespace-only file must not read back as a valid pin — that would
        // turn a corrupt config into an allow-anything config.
        BrokerConfig.SaveManifestKeyPin("   ");

        Assert.Null(BrokerConfig.LoadManifestKeyPin());
    }
}
