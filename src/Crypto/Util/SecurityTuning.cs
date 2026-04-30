using System;
using System.Security.Cryptography;
using System.Runtime.Versioning;
using GiblexVault.Security.ZK.Models;
using GiblexVault.Security.ZK.Primitives;
using GiblexVault.Security.ZK.Util.KeyProtector;

namespace GiblexVault.Security.ZK.Util
{
    public static class SecurityTuning
    {
        public static KeyProtector.IKeyProtector KeyProtector { get; } = KeyProtectorProvider.CreateDefault();

        public static EngineOptions Calibrate(EngineOptions target)
        {
            var pwd = RandomNumberGenerator.GetBytes(32);
            var salt = RandomNumberGenerator.GetBytes(32);

            int ops = Math.Max(2, target.ArgonOpsLimit);
            int mem = Math.Max(32, target.ArgonMemMiB);

            var sw = new System.Diagnostics.Stopwatch();
            do
            {
                var p = new KdfParams { MemMiB = mem, Ops = ops, Parallelism = target.ArgonParallelism, Salt = salt };
                sw.Restart();
                _ = Argon2Kdf.DeriveKey(pwd, salt, p);
                sw.Stop();
                if (sw.ElapsedMilliseconds < target.TargetUnlockMs)
                {
                    if (mem < 1024) mem *= 2;
                    else ops++;
                }
                else
                {
                    break;
                }
            } while (ops < 20);

            return target with { ArgonOpsLimit = ops, ArgonMemMiB = mem };
        }

        public static byte[] CreatePepperProtected()
        {
            var raw = RandomNumberGenerator.GetBytes(64);
            try
            {
                var sealedData = KeyProtector.Protect(raw);
                return sealedData;
            }
            finally
            {
                // Zero the raw pepper
                try { CryptographicOperations.ZeroMemory(raw); } catch { }
            }
        }

        public static byte[] UnsealPepper(byte[] protectedPepper)
        {
            return KeyProtector.Unprotect(protectedPepper);
        }
    }
}
