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
        /// generation and is suitable for long messages. Not
        /// currently implemented but reserved for future use.
        /// </summary>
        public const string XChaCha20Poly1305 = "XChaCha20-Poly1305";

        /// <summary>
        /// Hybrid post‑quantum scheme combining CRYSTALS‑Kyber for
        /// key encapsulation with AES‑256‑GCM for payload encryption.
        /// Only a stub in this implementation; real Kyber support
        /// requires an external library.
        /// </summary>
        public const string KyberAesHybrid = "Kyber-AES";
    }
}