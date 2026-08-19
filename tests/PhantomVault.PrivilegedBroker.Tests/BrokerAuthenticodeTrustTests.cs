using System;
using System.IO;
using PhantomVault.PrivilegedBroker;
using Xunit;

namespace PhantomVault.PrivilegedBroker.Tests;

public sealed class BrokerAuthenticodeTrustTests
{
    [Fact]
    public void MissingFile_IsRejected()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.exe");

        var trusted = AuthenticodeTrust.TryGetTrustedSignerSha256(missing, out var signer);

        Assert.False(trusted);
        Assert.Null(signer);
    }

    [Fact]
    public void UnsignedFile_IsRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), $"phantom-unsigned-{Guid.NewGuid():N}.exe");
        try
        {
            File.WriteAllText(path, "not a signed executable");

            var trusted = AuthenticodeTrust.TryGetTrustedSignerSha256(path, out var signer);

            Assert.False(trusted);
            Assert.Null(signer);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
