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

        // A-8: explicit, audit-defensible ceilings. Without these, Calibrate can
        // walk mem to 1024 MiB and ops to 20+ on slow machines, which (a) risks
        // OOM on average laptops and (b) makes unlock take tens of seconds.
        // Argon2id RFC 9106 §7.4 recommends mem≥64 MiB, ops≥1 for "memory-bound"
        // settings; we keep a generous max but refuse to walk past it.
        private const int MaxArgonMemMiB = 1024;   // 1 GiB hard cap
        private const int MaxArgonOpsLimit = 10;   // 10 passes hard cap

        public static EngineOptions Calibrate(EngineOptions target)
        {
            var pwd = RandomNumberGenerator.GetBytes(32);
            var salt = RandomNumberGenerator.GetBytes(32);

            int ops = Math.Max(2, target.ArgonOpsLimit);
            int mem = Math.Max(32, target.ArgonMemMiB);

            var sw = new System.Diagnostics.Stopwatch();
            try
            {
                do
                {
                    var p = new KdfParams { MemMiB = mem, Ops = ops, Parallelism = target.ArgonParallelism, Salt = salt };
                    sw.Restart();
                    _ = Argon2Kdf.DeriveKey(pwd, salt, p);
                    sw.Stop();
                    if (sw.ElapsedMilliseconds < target.TargetUnlockMs)
                    {
                        if (mem < MaxArgonMemMiB) mem = Math.Min(mem * 2, MaxArgonMemMiB);
                        else if (ops < MaxArgonOpsLimit) ops++;
                        else break; // both ceilings reached; accept what we have
                    }
                    else
                    {
                        break;
                    }
                } while (ops <= MaxArgonOpsLimit);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pwd);
                CryptographicOperations.ZeroMemory(salt);
            }

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

                try { CryptographicOperations.ZeroMemory(raw); } catch { }
            }
        }

        public static byte[] UnsealPepper(byte[] protectedPepper)
        {
            return KeyProtector.Unprotect(protectedPepper);
        }
    }
}

