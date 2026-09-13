using System;
using System.IO;
using System.Linq;
using PhantomVault.Core.Services.Icons;
using Xunit;

namespace PhantomVault.Core.Tests
{
    /// <summary>
    /// The icon library index (organisation and de-duplication) and the closest-match ranking
    /// used when the picker is opened for a category or an entry.
    /// </summary>
    public sealed class IconLibraryTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"PhantomVault_Icons_{Guid.NewGuid():N}");
        private string Visuals => Path.Combine(_root, "Visuals");
        private string MyIcons => Path.Combine(_root, "MyIcons");
        private string Downloads => Path.Combine(_root, "Downloads");

        public IconLibraryTests() => Directory.CreateDirectory(Visuals);

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { /* best-effort cleanup */ }
        }

        private void Write(string relativePath, string content)
        {
            var path = Path.Combine(_root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        private IconLibraryIndex Build() =>
            IconLibraryIndex.Build(new IconLibrarySources(Visuals, MyIcons, new[] { Downloads }));

        [Fact]
        public void ColourVariantFolder_BecomesOneLineIcon_UsingTheCharcoalCopy()
        {
            Write("Visuals/Cat Icons/Book/1_aqua.png", "aqua");
            Write("Visuals/Cat Icons/Book/1_charcoal.png", "charcoal");
            Write("Visuals/Cat Icons/Book/1_teal.png", "teal");

            var index = Build();

            var book = Assert.Single(index.LineIcons);
            Assert.Equal("Book", book.Name);
            Assert.EndsWith("1_charcoal.png", book.FilePath);
            Assert.True(book.IsTintable);
        }

        [Fact]
        public void Logos_AreDeduplicatedByName_KeepTheBestFormat_AndSkipImportAndCardLogos()
        {
            Write("Visuals/Entry Logos/Acer.ico", "ico");
            Write("Visuals/Entry Logos/Entry Logos/Acer.png", "png");
            Write("Visuals/Entry Logos/twitter (5).png", "t5");
            Write("Visuals/Entry Logos/twitter.png", "t");
            Write("Visuals/Entry Logos/Visa.png", "visa");
            Write("Visuals/Entry Logos/AccorSeeklogo.png", "accor");
            Write("Visuals/Entry Logos/Import logos/Chrome.png", "chrome");

            var index = Build();
            var names = index.Logos.Select(l => l.Name).ToList();

            Assert.Contains("Acer", names);
            Assert.EndsWith(".png", index.Logos.Single(l => l.Name == "Acer").FilePath);
            Assert.Single(names, n => n.Equals("twitter", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("Accor", names);
            Assert.DoesNotContain("Visa", names);
            Assert.DoesNotContain("Chrome", names);
            Assert.All(index.Logos, l => Assert.False(l.IsTintable));
        }

        [Fact]
        public void ByteIdenticalFiles_CollapseToOne_AndTheUsersCopyWins()
        {
            Write("Visuals/Entry Logos/Github.png", "same-bytes");
            Write("MyIcons/my-github.png", "same-bytes");

            var index = Build();

            Assert.Single(index.Uploads);
            Assert.Empty(index.Logos);
            Assert.True(index.DuplicatesRemoved >= 1);
        }

        [Fact]
        public void Uploads_AndDownloads_AreListedSeparately()
        {
            Write("MyIcons/mine.png", "mine");
            Write("MyIcons/Recoloured/book_FF0000.png", "generated");
            Write("Downloads/flaticon/fetched.png", "fetched");

            var index = Build();

            Assert.Equal("mine", Assert.Single(index.Uploads).Name);
            Assert.Equal("fetched", Assert.Single(index.Downloads).Name);
        }

        [Fact]
        public void EntryRanking_PutsTheClosestLogoFirst()
        {
            Write("Visuals/Entry Logos/GitHub.png", "gh");
            Write("Visuals/Entry Logos/GitLab.png", "gl");
            Write("Visuals/Entry Logos/Netflix.png", "nf");

            var index = Build();
            var queries = IconRanker.QueriesForEntry("Work account", "https://github.com/login");
            var ranked = IconRanker.Rank(index.Logos, queries, 0.5, 5);

            Assert.Equal("GitHub", ranked[0].Name);
            Assert.DoesNotContain(ranked, i => i.Name == "Netflix");
        }

        [Fact]
        public void CategoryRanking_UsesTheNameAndItsSynonyms()
        {
            Write("Visuals/Cat Icons/Wallet/w_charcoal.png", "w");
            Write("Visuals/Cat Icons/Bank/b_charcoal.png", "b");
            Write("Visuals/Cat Icons/Music/m_charcoal.png", "m");

            var index = Build();
            var ranked = IconRanker.Rank(index.LineIcons, IconRanker.QueriesForCategory("Banking"), 0.5, 5);

            Assert.Equal("Bank", ranked[0].Name);
            Assert.Contains(ranked, i => i.Name == "Wallet");
            Assert.DoesNotContain(ranked, i => i.Name == "Music");
        }

        [Theory]
        [InlineData("GoogleDrive", new[] { "google", "drive" })]
        [InlineData("Proton Pass", new[] { "proton", "pass" })]
        public void Tokens_SplitCamelCaseAndPunctuation(string input, string[] expected)
        {
            Assert.Equal(expected, IconRanker.Tokens(input));
        }
    }
}
