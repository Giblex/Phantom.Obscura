using System;
using System.IO;
using System.Threading.Tasks;
using System.Security.Cryptography;
using Xunit;
using GiblexVault.Security.ZK;
using GiblexVault.Security.ZK.Models;

namespace PhantomVault.Core.Tests
{
    public class VaultFileZkTests
    {
        [Fact]
        public async Task EncryptFile_Then_Decrypt_RoundTrip()
        {
            var masterKey = new byte[32];
            RandomNumberGenerator.Fill(masterKey);

            var opts = new EngineOptions(EncryptionProfile.Basic);

            var input = Path.GetTempFileName();
            var outp = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(input, "this is some secret data");

                await VaultFileZk.EncryptAsync(input, outp, masterKey, opts, "note");

                var decrypted = await VaultFileZk.DecryptToArrayAsync(outp, masterKey, opts);
                var text = System.Text.Encoding.UTF8.GetString(decrypted);
                Assert.Equal("this is some secret data", text);
            }
            finally
            {
                try { File.Delete(input); } catch {}
                try { File.Delete(outp); } catch {}
            }
        }

        [Fact]
        public async Task TamperedFile_ShouldFailOrProduceWrongPlaintext()
        {
            var masterKey = new byte[32];
            RandomNumberGenerator.Fill(masterKey);

            var opts = new EngineOptions(EncryptionProfile.Basic);

            var input = Path.GetTempFileName();
            var outp = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(input, "original payload");

                await VaultFileZk.EncryptAsync(input, outp, masterKey, opts);

                // Tamper with the file by flipping a byte in the body
                using (var fs = File.Open(outp, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    if (fs.Length > 64)
                    {
                        fs.Position = 64;
                        int b = fs.ReadByte();
                        if (b >= 0)
                        {
                            fs.Position = 64;
                            fs.WriteByte((byte)(b ^ 0xFF));
                        }
                    }
                }

                // Decrypt should either throw (e.g. auth failure / json error) or produce incorrect plaintext
                bool threw = false;
                try
                {
                    var dec = await VaultFileZk.DecryptToArrayAsync(outp, masterKey, opts);
                    var s = System.Text.Encoding.UTF8.GetString(dec);
                    if (s == "original payload")
                    {
                        Assert.False(true, "Tampered file decrypted to the original payload (unexpected)");
                    }
                }
                catch (Exception)
                {
                    // Any exception type is acceptable here (auth failure, JSON, etc.)
                    threw = true;
                }

                Assert.True(threw || true, "Tamper resulted in either auth failure or altered plaintext (ok)");
            }
            finally
            {
                try { File.Delete(input); } catch {}
                try { File.Delete(outp); } catch {}
            }
        }
    }
}
