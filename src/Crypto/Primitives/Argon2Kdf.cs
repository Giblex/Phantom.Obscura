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

            var passBytes = password.ToArray();
            var saltBytes = salt.ToArray();

            try
            {
                var cfg = new Argon2Config
                {

                    Type = ResolveArgon2Type(p.Kdf),
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

                var output = new byte[res.Buffer.Length];
                Array.Copy(res.Buffer, output, output.Length);

                try { CryptographicOperations.ZeroMemory(res.Buffer); } catch {  }

                return output;
            }
            finally
            {

                try { CryptographicOperations.ZeroMemory(passBytes); } catch { }
                try { CryptographicOperations.ZeroMemory(saltBytes); } catch { }
            }
        }

        private static Argon2Type ResolveArgon2Type(string? kdf)
            => (kdf?.Trim().ToLowerInvariant()) switch
            {
                null or "" or "argon2id" => Argon2Type.HybridAddressing,
                "argon2i" => Argon2Type.DataIndependentAddressing,
                "argon2d" => Argon2Type.DataDependentAddressing,
                _ => throw new NotSupportedException($"Unsupported KDF '{kdf}'. Expected argon2id, argon2i, or argon2d.")
            };

        public static byte[] DeriveKeyFromString(string password, byte[] salt, KdfParams p)
            => DeriveKey(System.Text.Encoding.UTF8.GetBytes(password), salt, p);
    }
}

