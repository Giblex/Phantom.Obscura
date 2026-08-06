using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using PhantomVault.Core.Models.Licensing;

namespace PhantomVault.Core.Services.Licensing
{
    /// <summary>
    /// Encodes/decodes and signs/verifies license tokens.
    ///
    /// <para>Token wire format: <c>base64url(payloadJsonUtf8) "." base64url(signature)</c>.
    /// The signature is Ed25519 over the exact UTF-8 payload bytes that were
    /// encoded — verification decodes those same bytes, so there is no
    /// canonical-JSON round-trip to disagree about.</para>
    ///
    /// <para>This same format is what the production licensing server will emit;
    /// the env-gated dev authority uses it locally to make the flow testable.</para>
    /// </summary>
    public static class LicenseTokenCodec
    {
        private const int Ed25519PublicKeySize = 32;
        private const int Ed25519SignatureSize = 64;

        private static readonly JsonSerializerOptions PayloadOptions = new()
        {
            PropertyNamingPolicy = null,
            WriteIndented = false
        };

        /// <summary>Generates a fresh Ed25519 keypair. Returns raw 32-byte keys.</summary>
        public static (byte[] publicKey, byte[] privateKey) GenerateKeyPair()
        {
            var random = new SecureRandom();
            var generator = new Ed25519KeyPairGenerator();
            generator.Init(new Ed25519KeyGenerationParameters(random));
            var pair = generator.GenerateKeyPair();
            var priv = (Ed25519PrivateKeyParameters)pair.Private;
            var pub = (Ed25519PublicKeyParameters)pair.Public;
            return (pub.GetEncoded(), priv.GetEncoded());
        }

        /// <summary>Derives the 32-byte public key from a 32-byte private key.</summary>
        public static byte[] DerivePublicKey(byte[] privateKey)
        {
            if (privateKey is null) throw new ArgumentNullException(nameof(privateKey));
            var priv = new Ed25519PrivateKeyParameters(privateKey, 0);
            return priv.GeneratePublicKey().GetEncoded();
        }

        /// <summary>Signs claims with the given Ed25519 private key, returning a token string.</summary>
        public static string CreateToken(LicenseClaims claims, byte[] privateKey)
        {
            if (claims is null) throw new ArgumentNullException(nameof(claims));
            if (privateKey is null) throw new ArgumentNullException(nameof(privateKey));

            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(claims, PayloadOptions);
            byte[] signature = Sign(payload, privateKey);
            return Base64UrlEncode(payload) + "." + Base64UrlEncode(signature);
        }

        /// <summary>
        /// Verifies a token against a public key and parses its claims. Returns
        /// false (and null claims) on any structural or signature failure.
        /// </summary>
        public static bool TryVerify(string? token, ReadOnlySpan<byte> publicKey, out LicenseClaims? claims)
        {
            claims = null;
            if (string.IsNullOrWhiteSpace(token)) return false;
            if (publicKey.Length != Ed25519PublicKeySize) return false;

            int dot = token.IndexOf('.');
            if (dot <= 0 || dot >= token.Length - 1) return false;

            byte[] payload;
            byte[] signature;
            try
            {
                payload = Base64UrlDecode(token.Substring(0, dot));
                signature = Base64UrlDecode(token.Substring(dot + 1));
            }
            catch (FormatException)
            {
                return false;
            }

            if (signature.Length != Ed25519SignatureSize) return false;
            if (!Verify(publicKey, payload, signature)) return false;

            try
            {
                claims = JsonSerializer.Deserialize<LicenseClaims>(payload, PayloadOptions);
            }
            catch (JsonException)
            {
                return false;
            }

            return claims is not null;
        }

        private static byte[] Sign(byte[] message, byte[] privateKey)
        {
            var signer = new Ed25519Signer();
            signer.Init(forSigning: true, new Ed25519PrivateKeyParameters(privateKey, 0));
            signer.BlockUpdate(message, 0, message.Length);
            return signer.GenerateSignature();
        }

        private static bool Verify(ReadOnlySpan<byte> publicKey, byte[] message, byte[] signature)
        {
            byte[] pkArr = publicKey.ToArray();
            try
            {
                var verifier = new Ed25519Signer();
                verifier.Init(forSigning: false, new Ed25519PublicKeyParameters(pkArr, 0));
                verifier.BlockUpdate(message, 0, message.Length);
                return verifier.VerifySignature(signature);
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pkArr);
            }
        }

        private static string Base64UrlEncode(byte[] data) =>
            Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static byte[] Base64UrlDecode(string value)
        {
            string s = value.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
                case 1: throw new FormatException("Invalid base64url length.");
            }
            return Convert.FromBase64String(s);
        }
    }
}
