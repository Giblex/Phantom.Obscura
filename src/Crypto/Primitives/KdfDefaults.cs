using GiblexVault.Security.ZK.Models;

namespace GiblexVault.Security.ZK.Primitives;

/// <summary>
/// Single source of truth for Argon2id KDF parameter sets used throughout the suite.
/// Add new profiles here; never inline numeric defaults in call sites.
/// </summary>
public static class KdfDefaults
{
    /// <summary>
    /// Primary vault derivation: Argon2id, 6 ops, 256 MiB, 4 lanes.
    ///
    /// This is the profile the rest of the suite already assumed (it matches both the
    /// KdfParams default and SecurityCapabilitiesManifest), but it existed only as
    /// inline literals at each call site. Naming it here makes the intended cost
    /// explicit and gives a single place to raise it.
    /// </summary>
    public static KdfParams Primary(byte[] salt) => new()
    {
        Kdf = "argon2id",
        Ops = 6,
        MemMiB = 256,
        Parallelism = 4,
        Salt = salt,
    };

    /// <summary>
    /// Recovery-code derivation: Argon2id, 3 ops, 64 MiB, 1 lane.
    ///
    /// Deliberately cheaper than <see cref="Primary"/>, and that is correct — do not
    /// "harden" it to match without reading this first.
    ///
    /// A KDF's work factor exists to compensate for LOW entropy in a human-chosen
    /// password. A recovery code is not human-chosen: RecoveryCodeService generates it
    /// from RandomNumberGenerator.GetBytes(16), i.e. 128 bits. Brute force is already
    /// infeasible at any work factor, so extra stretching buys no security while
    /// costing real time — a set is 10 codes, each derived separately, and the same
    /// derivation runs again on every verification attempt.
    ///
    /// The cost here is only guarding against a weak RNG, so it is kept modest.
    /// </summary>
    public static KdfParams Recovery(byte[] salt) => new()
    {
        Kdf = "argon2id",
        Ops = 3,
        MemMiB = 64,
        Parallelism = 1,
        Salt = salt,
    };
}
