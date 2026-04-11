using PhantomVault.Core.Models;
using PhantomVault.UI.ViewModels;
using Xunit;

namespace PhantomVault.UI.Tests;

public sealed class VaultViewModelIntegrationTests
{
    [Fact]
    public void CredentialViewModel_ExposesBasicFields()
    {
        var model = new Credential
        {
            Title = "GitHub",
            Username = "octocat",
            Password = "Sup3rSecret!",
            Url = "https://github.com"
        };

        var vm = new CredentialViewModel(model);

        Assert.Equal("GitHub", vm.Title);
        Assert.Equal("octocat", vm.Username);
        Assert.True(vm.HasUsername);
        Assert.True(vm.HasPassword);
        Assert.True(vm.HasUrl);
    }

    [Fact]
    public void CredentialViewModel_FavoriteState_UpdatesIcon()
    {
        var vm = new CredentialViewModel(new Credential { Title = "Favorite Test" });

        vm.IsFavorite = true;
        Assert.Equal("⭐", vm.FavoriteIcon);

        vm.IsFavorite = false;
        Assert.Equal("☆", vm.FavoriteIcon);
    }

    [Fact]
    public void CredentialViewModel_MasksCreditCardNumber()
    {
        var model = new Credential
        {
            EntryType = EntryType.CreditCard,
            Title = "Card",
            CardNumber = "4111111111111111"
        };

        var vm = new CredentialViewModel(model);

        Assert.NotEqual(model.CardNumber, vm.MaskedCardNumber);
        Assert.EndsWith("1111", vm.MaskedCardNumber);
    }

    [Fact]
    public void CredentialViewModel_MasksApiKeyValue()
    {
        var model = new Credential
        {
            EntryType = EntryType.ApiKey,
            Title = "API",
            ApiKeyValue = "sk_live_1234567890abcdef"
        };

        var vm = new CredentialViewModel(model);

        Assert.Contains("••••", vm.MaskedApiKeyValue);
        Assert.StartsWith("sk_l", vm.MaskedApiKeyValue);
        Assert.EndsWith("cdef", vm.MaskedApiKeyValue);
    }
}
