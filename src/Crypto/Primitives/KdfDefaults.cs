using GiblexVault.Security.ZK.Models;

namespace GiblexVault.Security.ZK.Primitives;

/// <summary>
/// Single source of truth for Argon2id KDF parameter sets used throughout the suite.
/// Add new profiles here; never inline numeric defaults in call sites.
/// </summary>
public static class KdfDefaults
{
    /// <summary>Recovery-code derivation: Argon2id, 3 ops, 64 MiB, 1 lane.</summary>
    public static KdfParams Recovery(byte[] salt) => new()
    {
        Kdf = "argon2id",
        Ops = 3,
        MemMiB = 64,
        Parallelism = 1,
        Salt = salt,
    };
}
