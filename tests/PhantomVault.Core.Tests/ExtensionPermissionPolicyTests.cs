using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace PhantomVault.Core.Tests;

public sealed class ExtensionPermissionPolicyTests
{
    [Fact]
    public void Manifest_UsesActiveTabAndOptionalExactSchemeHosts()
    {
        var manifestPath = FindRepositoryFile("src", "Extension", "manifest.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = manifest.RootElement;

        Assert.False(root.TryGetProperty("content_scripts", out _));

        var permissions = root.GetProperty("permissions")
            .EnumerateArray().Select(static value => value.GetString()).ToArray();
        Assert.Contains("activeTab", permissions);
        Assert.Contains("scripting", permissions);

        var optionalHosts = root.GetProperty("optional_host_permissions")
            .EnumerateArray().Select(static value => value.GetString()).ToArray();
        Assert.Equal(new[] { "https://*/*", "http://*/*" }, optionalHosts);
        Assert.DoesNotContain("<all_urls>", File.ReadAllText(manifestPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Background_DerivesCredentialOriginFromTrustedTabSender()
    {
        var backgroundPath = FindRepositoryFile("src", "Extension", "background.js");
        var source = File.ReadAllText(backgroundPath);

        Assert.Contains("sender?.tab?.url", source, StringComparison.Ordinal);
        Assert.Contains("data: { domain: context.domain, url: context.url, pageOrigin: context.origin }", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(segments)}");
    }
}
