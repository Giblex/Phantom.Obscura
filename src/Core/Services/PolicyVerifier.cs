using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PhantomVault.Core.Services;

public static class PolicyVerifier
{
    // Call this at startup with the content of root_public.json
    public static ECDsa CreateRootVerifier(string rootPublicJson)
    {
        var doc = JsonDocument.Parse(rootPublicJson);
        var root = doc.RootElement;

        string algorithm = root.GetProperty("algorithm").GetString() ?? "";
        if (algorithm != "ECDSA-P256")
            throw new InvalidOperationException("Unsupported root algorithm.");

        string publicKeyBase64 = root.GetProperty("publicKey").GetString() ?? "";
        byte[] publicKeyBytes = Convert.FromBase64String(publicKeyBase64);

        var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(publicKeyBytes, out _);
        return ecdsa;
    }

    // Verifies a signed policy JSON string. Throws if invalid.
    public static void VerifyPolicy(string signedPolicyJson, ECDsa rootVerifier)
    {
        JsonNode? node = JsonNode.Parse(signedPolicyJson);
        if (node is null || node is not JsonObject obj)
            throw new InvalidOperationException("Policy JSON must be an object.");

        // Extract signature block
        if (!obj.TryGetPropertyValue("signature", out JsonNode? sigNode) || sigNode is not JsonObject sigObj)
            throw new InvalidOperationException("Policy is missing 'signature' block.");

        string algorithm = sigObj["algorithm"]?.GetValue<string>() ?? "";
        string hashAlg   = sigObj["hash"]?.GetValue<string>() ?? "";
        string signedBy  = sigObj["signedBy"]?.GetValue<string>() ?? "";
        string value     = sigObj["value"]?.GetValue<string>() ?? "";

        if (algorithm != "ECDSA-P256" || hashAlg != "SHA-512" || signedBy != "OBSCURA-ROOT-1")
            throw new InvalidOperationException("Policy signature metadata not trusted.");

        byte[] signatureBytes = Convert.FromBase64String(value);

        // Remove signature field for hashing
        obj.Remove("signature");

        string unsignedJson = obj.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false
        });

        byte[] dataBytes = Encoding.UTF8.GetBytes(unsignedJson);

        byte[] hash;
        using (var sha = SHA512.Create())
        {
            hash = sha.ComputeHash(dataBytes);
        }

        bool valid = rootVerifier.VerifyHash(hash, signatureBytes);
        if (!valid)
            throw new CryptographicException("Policy signature verification failed.");
    }
}
