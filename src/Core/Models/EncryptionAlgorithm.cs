namespace PhantomVault.Core.Models
{
    /// <summary>
    /// Defines constants for supported encryption algorithms. The
    /// manifest stores one of these values in its <c>Algorithm</c>
    /// property to indicate which cipher was used to encrypt its
    /// contents. Use these constants when setting or comparing
    /// algorithms to avoid spelling mistakes.
    /// </summary>
    public static class EncryptionAlgorithm
    {
        /// <summary>
        /// AES‑256 in Galois/Counter Mode with a 96‑bit nonce and
        /// 128‑bit authentication tag. Widely considered the gold
        /// standard for symmetric encryption as of 2025.
        /// </summary>
        public const string Aes256Gcm = "AES-256-GCM";

        /// <summary>
        /// XChaCha20 stream cipher combined with Poly1305 MAC.
        /// Provides a 192‑bit nonce for easier random nonce
        /// generation and is suitable for long messages. Reserved
        /// for future use — not currently exercised by the manifest
        /// writer.
        /// </summary>
        public const string XChaCha20Poly1305 = "XChaCha20-Poly1305";

        /// <summary>
        /// Hybrid post‑quantum scheme combining CRYSTALS‑Kyber
        /// (ML‑KEM‑768) for key encapsulation with AES‑256‑GCM for
        /// payload encryption. The KEM half is implemented by
        /// <c>HybridEncryptionService</c> via BouncyCastle and is
        /// exercised on every vault provision (KEM key‑pair generation
        /// + encapsulation) and unlock (decapsulation +
        /// <c>IZkVaultService.UnlockWithHybridKeyAsync</c>). Manifests
        /// that opt into the hybrid wrapping carry the ML‑KEM public
        /// and (encrypted) private keys alongside the standard AES‑GCM
        /// envelope; the <see cref="Aes256Gcm"/> string is still used
        /// for the symmetric layer's <c>Algorithm</c> field, while
        /// this constant labels manifests whose envelope explicitly
        /// records the hybrid contract.
        /// </summary>
        public const string KyberAesHybrid = "Kyber-AES";
    }
}