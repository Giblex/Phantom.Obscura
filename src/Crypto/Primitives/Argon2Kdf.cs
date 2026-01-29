using System;
using System.Security.Cryptography;
using Isopoh.Cryptography.Argon2;
using GiblexVault.Security.ZK.Models;

namespace GiblexVault.Security.ZK.Primitives
{
    public static class Argon2Kdf
    {
        public static byte[] DeriveKey(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, KdfParams p)
        {
            // Copy inputs to managed arrays so we can zero them deterministically
            var passBytes = password.ToArray();
            var saltBytes = salt.ToArray();

            try
            {
                var cfg = new Argon2Config
                {
                    // Argon2id (data-independent addressing)
                    Type = Argon2Type.DataIndependentAddressing,
                    Version = Argon2Version.Nineteen,
                    TimeCost = Math.Max(1, p.Ops),
                    MemoryCost = Math.Max(8, p.MemMiB) * 1024,
                    Lanes = Math.Max(1, p.Parallelism),
                    Threads = Math.Max(1, p.Parallelism),
                    Password = passBytes,
                    Salt = saltBytes,
                    HashLength = 32
                };

                using var a = new Argon2(cfg);
                using var res = a.Hash();

                // Copy library-managed buffer into our own array that we control
                var output = new byte[res.Buffer.Length];
                Array.Copy(res.Buffer, output, output.Length);

                // Zero library buffer as soon as we've copied it
                try { CryptographicOperations.ZeroMemory(res.Buffer); } catch { /* best-effort */ }

                return output;
            }
            finally
            {
                // Zero input buffers
                try { CryptographicOperations.ZeroMemory(passBytes); } catch { }
                try { CryptographicOperations.ZeroMemory(saltBytes); } catch { }
            }
        }

        public static byte[] DeriveKeyFromString(string password, byte[] salt, KdfParams p)
            => DeriveKey(System.Text.Encoding.UTF8.GetBytes(password), salt, p);
    }
}
