using PhantomVault.Core.Models;
using PhantomVault.UI.Services;
using Xunit;

namespace PhantomVault.UI.Tests;

public sealed class AccountConsolidationServiceTests
{
    [Fact]
    public void GooglePasswordAndTotpRemainSeparateButShareIdentity()
    {
        var password = new Credential
        {
            Title = "Google",
            Username = "person@example.com",
            Password = "first-password",
            Url = "https://accounts.google.com"
        };
        var totp = new Credential
        {
            Title = "Google TOTP",
            Username = "person@example.com",
            TotpIssuer = "Google",
            TotpSecret = "JBSWY3DPEHPK3PXP"
        };

        AccountConsolidationService.Consolidate(totp, new[] { password });

        Assert.NotEqual(password.Id, totp.Id);
        Assert.Equal("Google", totp.Title);
        Assert.Equal("first-password", password.Password);
        Assert.Equal("JBSWY3DPEHPK3PXP", totp.TotpSecret);
        Assert.Equal("google", totp.CustomFields[AccountConsolidationService.ServiceKeyField]);
    }

    [Fact]
    public void DifferentPasswordForSameServiceAndUsernameIsNotOverwritten()
    {
        var first = new Credential
        {
            Title = "Google",
            Username = "person@example.com",
            Password = "old-password",
            Url = "google.com"
        };
        var second = new Credential
        {
            Title = "Google password",
            Username = "person@example.com",
            Password = "new-password"
        };

        AccountConsolidationService.Consolidate(second, new[] { first });

        Assert.Equal("Google", second.Title);
        Assert.Equal("old-password", first.Password);
        Assert.Equal("new-password", second.Password);
    }
}
