using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests
{
    /// <summary>
    /// Bitwarden CSV/JSON export, checked by round-tripping through the existing Bitwarden
    /// importers and against the structure Bitwarden's own importer expects.
    /// </summary>
    public sealed class BitwardenExportTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), $"PhantomVault_Bitwarden_{Guid.NewGuid():N}");
        private readonly ImportExportService _service = new();

        public BitwardenExportTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { /* best-effort cleanup */ }
        }

        private static EntryType NonLoginType() =>
            Enum.GetValues<EntryType>().First(t => t != EntryType.Password);

        private static List<Credential> Sample() => new()
        {
            new Credential
            {
                Title = "GitHub",
                Username = "octocat",
                Password = "p@ss, with \"quotes\"",
                Url = "https://github.com/login",
                Notes = "work account",
                Group = "Dev",
                IsFavorite = true,
                TotpSecret = "JBSWY3DPEHPK3PXP"
            },
            new Credential
            {
                Title = "Bank",
                Username = "me@example.com",
                Password = "correct horse",
                Url = "",
                Notes = "",
                Group = ""
            },
            new Credential
            {
                Title = "Not a login",
                EntryType = NonLoginType()
            }
        };

        [Fact]
        public async Task BitwardenJson_RoundTripsThroughTheBitwardenImporter()
        {
            var path = Path.Combine(_dir, "export.json");

            int written = await _service.ExportToBitwardenJsonAsync(Sample(), path);
            var imported = await _service.ImportFromBitwardenJsonAsync(path);

            Assert.Equal(2, written);
            Assert.Equal(2, imported.Count);

            var github = imported.Single(c => c.Title == "GitHub");
            Assert.Equal("octocat", github.Username);
            Assert.Equal("p@ss, with \"quotes\"", github.Password);
            Assert.Equal("https://github.com/login", github.Url);
            Assert.Equal("work account", github.Notes);
            Assert.Equal("Dev", github.Group);

            var bank = imported.Single(c => c.Title == "Bank");
            Assert.Equal("correct horse", bank.Password);
        }

        [Fact]
        public async Task BitwardenJson_HasTheStructureBitwardenExpects()
        {
            var path = Path.Combine(_dir, "export.json");
            await _service.ExportToBitwardenJsonAsync(Sample(), path);

            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            var root = doc.RootElement;

            Assert.False(root.GetProperty("encrypted").GetBoolean());
            Assert.Equal("Dev", root.GetProperty("folders")[0].GetProperty("name").GetString());

            var item = root.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("name").GetString() == "GitHub");
            Assert.Equal(1, item.GetProperty("type").GetInt32());
            Assert.True(item.GetProperty("favorite").GetBoolean());
            Assert.Equal(root.GetProperty("folders")[0].GetProperty("id").GetString(), item.GetProperty("folderId").GetString());
            Assert.Equal("JBSWY3DPEHPK3PXP", item.GetProperty("login").GetProperty("totp").GetString());
        }

        [Fact]
        public async Task BitwardenCsv_RoundTripsThroughTheBitwardenImporter()
        {
            var path = Path.Combine(_dir, "export.csv");

            int written = await _service.ExportToBitwardenCsvAsync(Sample(), path);
            var imported = await _service.ImportFromBitwardenCsvAsync(path);

            Assert.Equal(2, written);
            Assert.Equal(2, imported.Count);

            var github = imported.Single(c => c.Title == "GitHub");
            Assert.Equal("octocat", github.Username);
            Assert.Equal("p@ss, with \"quotes\"", github.Password);
            Assert.Equal("https://github.com/login", github.Url);
            Assert.Equal("Dev", github.Group);
        }

        [Fact]
        public async Task BitwardenCsv_StartsWithTheBitwardenHeaderAndNoBom()
        {
            var path = Path.Combine(_dir, "export.csv");
            await _service.ExportToBitwardenCsvAsync(Sample(), path);

            var bytes = await File.ReadAllBytesAsync(path);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);

            var firstLine = File.ReadLines(path).First();
            Assert.Equal("folder,favorite,type,name,notes,fields,reprompt,login_uri,login_username,login_password,login_totp", firstLine);
        }
    }
}
