using System;
using System.IO;
using System.Linq;
using PhantomVault.UI.Services;
using Xunit;

namespace PhantomVault.UI.Tests;

public sealed class RecoveryPdfWriterTests : IDisposable
{
    private readonly string _tempDirectory;

    public RecoveryPdfWriterTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"PhantomVault_RecoveryPdf_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private static bool HasPdfMagic(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[5];
        var read = stream.Read(header);
        // "%PDF-"
        return read == 5
            && header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44
            && header[3] == 0x46 && header[4] == 0x2D;
    }

    [Fact]
    public void Write_ProducesValidNonEmptyPdf()
    {
        var path = Path.Combine(_tempDirectory, "kit.pdf");
        var codes = new[] { "AAAA-BBBB-CCCC", "DDDD-EEEE-FFFF", "GGGG-HHHH-IIII" };

        RecoveryPdfWriter.Write(path, "My Vault", codes);

        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 0);
        Assert.True(HasPdfMagic(path), "Output should begin with the %PDF- magic.");
    }

    [Fact]
    public void Write_WithEmptyCodes_StillProducesValidPdf()
    {
        var path = Path.Combine(_tempDirectory, "empty.pdf");

        RecoveryPdfWriter.Write(path, "Vault", Array.Empty<string>());

        Assert.True(File.Exists(path));
        Assert.True(HasPdfMagic(path));
    }

    [Fact]
    public void Write_WithManyCodes_SpansMultiplePagesWithoutError()
    {
        var path = Path.Combine(_tempDirectory, "many.pdf");
        var codes = Enumerable.Range(0, 120).Select(i => $"CODE-{i:0000}-XXXX-YYYY").ToArray();

        RecoveryPdfWriter.Write(path, "Big Vault", codes);

        Assert.True(File.Exists(path));
        Assert.True(HasPdfMagic(path));
    }

    [Fact]
    public void Write_WithBlankVaultName_DoesNotThrow()
    {
        var path = Path.Combine(_tempDirectory, "noname.pdf");

        RecoveryPdfWriter.Write(path, "   ", new[] { "ONE-TWO-THREE" });

        Assert.True(HasPdfMagic(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Write_WithMissingPath_Throws(string? path)
    {
        Assert.Throws<ArgumentException>(() => RecoveryPdfWriter.Write(path!, "Vault", new[] { "X" }));
    }
}
