using System;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests;

public sealed class ErrorMessageServiceTests
{
    [Fact]
    public void UnknownException_DoesNotExposeOriginalMessage()
    {
        const string secret = @"C:\Users\Alice\private\vault.json api_token=secret";
        var exception = new InvalidOperationException(secret);

        var result = ErrorMessageService.GetErrorMessageFromException(exception);

        Assert.DoesNotContain(secret, result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Alice", result.Message, StringComparison.Ordinal);
        Assert.Contains(secret, result.TechnicalDetails, StringComparison.Ordinal);
        Assert.Equal("UNKNOWN", result.ErrorCode);
    }

    [Fact]
    public void UserSafeMessage_NeverReturnsUnknownExceptionText()
    {
        var message = ErrorMessageService.GetUserSafeMessage(
            new Exception("device-id=ABC123; key-path=private.key"));

        Assert.DoesNotContain("ABC123", message, StringComparison.Ordinal);
        Assert.DoesNotContain("private.key", message, StringComparison.Ordinal);
    }
}
