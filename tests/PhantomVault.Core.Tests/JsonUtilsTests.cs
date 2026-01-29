using System.Text.Json;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests
{
    public class JsonUtilsTests
    {
        [Fact]
        public void TryParseRecovering_HappyPath_ReturnsTrue()
        {
            var json = "{\"a\":1}";
            Assert.True(JsonUtils.TryParseRecovering(json, out var doc, out var err));
            using (doc)
            {
                Assert.Null(err);
                Assert.Equal(1, doc.RootElement.GetProperty("a").GetInt32());
            }
        }

        [Fact]
        public void TryParseRecovering_WithBom_ReturnsTrue()
        {
            var json = "\uFEFF{\"b\":2}";
            Assert.True(JsonUtils.TryParseRecovering(json, out var doc, out var err));
            using (doc)
            {
                Assert.Null(err);
                Assert.Equal(2, doc.RootElement.GetProperty("b").GetInt32());
            }
        }

        [Fact]
        public void TryParseRecovering_GarbagePrefix_ReturnsTrue()
        {
            var json = "garbage before\n\n{\"c\":3}\ntrail";
            Assert.True(JsonUtils.TryParseRecovering(json, out var doc, out var err));
            using (doc)
            {
                Assert.Null(err);
                Assert.Equal(3, doc.RootElement.GetProperty("c").GetInt32());
            }
        }
    }
}
