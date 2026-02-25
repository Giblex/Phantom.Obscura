using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Obscura.Keysmith;

internal static class Program
{
    // Base folder for keys (relative to the Keysmith exe)
    private static readonly string KeysBasePath = Path.Combine(AppContext.BaseDirectory, "keys");
    private static readonly string CertsBasePath = Path.Combine(AppContext.BaseDirectory, "certs");

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 1;
        }

        var command = args[0].ToLowerInvariant();

        try
        {
            switch (command)
            {
                case "init-root":
                    InitRootKey();
                    break;

                case "gen-cert":
                    GenCert();
                    break;

                case "sign-policy":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: sign-policy <inputPolicy.json> <outputPolicy.signed.json>");
                        return 1;
                    }
                    SignPolicy(args[1], args[2]);
                    break;

                case "verify-policy":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: verify-policy <signedPolicy.json>");
                        return 1;
                    }
                    VerifyPolicy(args[1]);
                    break;

                default:
                    Console.WriteLine($"Unknown command: {command}");
                    PrintHelp();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR:");
            Console.WriteLine(ex.Message);
            return 1;
        }

        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Obscura.Keysmith - Phantom Obscura signing tool");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  init-root");
        Console.WriteLine("      Generates a root ECDSA keypair and stores it under ./keys/root/");
        Console.WriteLine();
        Console.WriteLine("  gen-cert");
        Console.WriteLine("      Creates a self-signed PFX certificate from the root key.");
        Console.WriteLine("      Output: ./certs/obscura_root.pfx");
        Console.WriteLine();
        Console.WriteLine("  sign-policy <inputPolicy.json> <outputPolicy.signed.json>");
        Console.WriteLine("      Signs a policy JSON file using the root private key.");
        Console.WriteLine("      Loads key from ./keys/root/root_private.key first,");
        Console.WriteLine("      falls back to ./certs/obscura_root.pfx if not found.");
        Console.WriteLine();
        Console.WriteLine("  verify-policy <signedPolicy.json>");
        Console.WriteLine("      Verifies the signature of a signed policy JSON file");
        Console.WriteLine("      against the root public key.");
        Console.WriteLine();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // init-root — Generate root ECDSA keypair
    // ═══════════════════════════════════════════════════════════════════════

    private static void InitRootKey()
    {
        var rootDir = Path.Combine(KeysBasePath, "root");
        Directory.CreateDirectory(rootDir);

        var privateKeyPath = Path.Combine(rootDir, "root_private.key");
        var publicKeyPath  = Path.Combine(rootDir, "root_public.json");

        if (File.Exists(privateKeyPath) || File.Exists(publicKeyPath))
        {
            Console.WriteLine("Root key already exists. Delete the files in ./keys/root/ if you want to regenerate.");
            return;
        }

        Console.WriteLine("Generating root ECDSA keypair (P-256)...");

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        // Export keys
        byte[] privateKeyBytes = ecdsa.ExportECPrivateKey();
        byte[] publicKeyBytes  = ecdsa.ExportSubjectPublicKeyInfo();

        // Save private key as Base64 (keep folder offline / out of git)
        var privateKeyBase64 = Convert.ToBase64String(privateKeyBytes);
        File.WriteAllText(privateKeyPath, privateKeyBase64);

        // Prepare public key JSON
        var rootPublic = new
        {
            keyId     = "OBSCURA-ROOT-1",
            algorithm = "ECDSA-P256",
            format    = "SubjectPublicKeyInfo",
            publicKey = Convert.ToBase64String(publicKeyBytes),
            createdAt = DateTime.UtcNow.ToString("O")
        };

        var json = JsonSerializer.Serialize(rootPublic, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(publicKeyPath, json);

        Console.WriteLine($"Root private key saved to: {privateKeyPath}");
        Console.WriteLine($"Root public key saved to : {publicKeyPath}");
        Console.WriteLine("IMPORTANT: Keep ./keys/root/ OFFLINE and out of source control.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // gen-cert — Create a self-signed PFX from the root key
    // ═══════════════════════════════════════════════════════════════════════

    private static void GenCert()
    {
        var rootDir = Path.Combine(KeysBasePath, "root");
        var privateKeyPath = Path.Combine(rootDir, "root_private.key");

        if (!File.Exists(privateKeyPath))
        {
            throw new InvalidOperationException("Root private key not found. Run `init-root` first.");
        }

        Directory.CreateDirectory(CertsBasePath);

        var pfxPath = Path.Combine(CertsBasePath, "obscura_root.pfx");
        if (File.Exists(pfxPath))
        {
            Console.WriteLine("PFX certificate already exists. Delete ./certs/obscura_root.pfx to regenerate.");
            return;
        }

        // Load the raw ECDSA private key
        string privateKeyBase64 = File.ReadAllText(privateKeyPath).Trim();
        byte[] privateKeyBytes = Convert.FromBase64String(privateKeyBase64);

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportECPrivateKey(privateKeyBytes, out _);

        // Create a self-signed certificate
        var request = new CertificateRequest(
            "CN=Obscura Root CA, O=Phantom Obscura, OU=Policy Signing",
            ecdsa,
            HashAlgorithmName.SHA512);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(true, false, 0, true));

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyCertSign,
                true));

        var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(10));

        // Prompt for PFX password
        Console.Write("Enter password for PFX (or press Enter for none): ");
        string password = ReadPasswordFromConsole();

        byte[] pfxBytes = cert.Export(X509ContentType.Pfx, password);
        File.WriteAllBytes(pfxPath, pfxBytes);

        Console.WriteLine($"PFX certificate saved to: {pfxPath}");
        Console.WriteLine($"  Subject : {cert.Subject}");
        Console.WriteLine($"  Expires : {cert.NotAfter:O}");
        Console.WriteLine($"  Thumb   : {cert.Thumbprint}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // sign-policy — Sign a policy JSON file
    // ═══════════════════════════════════════════════════════════════════════

    private static void SignPolicy(string inputPath, string outputPath)
    {
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("Policy file not found.", inputPath);
        }

        // Load policy JSON
        string policyJson = File.ReadAllText(inputPath);

        // Parse into JsonNode so we can remove/add the signature field
        JsonNode? rootNode = JsonNode.Parse(policyJson);
        if (rootNode is null || rootNode is not JsonObject policyObj)
        {
            throw new InvalidOperationException("Policy JSON must be a JSON object at the root.");
        }

        // Remove any existing signature field before signing
        policyObj.Remove("signature");

        // Canonical serialization (no indentation)
        string unsignedJson = policyObj.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false
        });

        byte[] dataBytes = Encoding.UTF8.GetBytes(unsignedJson);

        // Hash policy
        byte[] hash;
        using (var sha = SHA512.Create())
        {
            hash = sha.ComputeHash(dataBytes);
        }

        // Load root private key (raw key file → PFX fallback)
        using var ecdsa = LoadRootSigningKey();

        // Sign hash
        byte[] signatureBytes = ecdsa.SignHash(hash);
        string signatureBase64 = Convert.ToBase64String(signatureBytes);

        // Attach signature block
        var signatureNode = new JsonObject
        {
            ["algorithm"] = "ECDSA-P256",
            ["hash"]      = "SHA-512",
            ["signedBy"]  = "OBSCURA-ROOT-1",
            ["value"]     = signatureBase64
        };

        policyObj["signature"] = signatureNode;

        // Write signed policy
        string signedJson = policyObj.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(outputPath, signedJson);

        Console.WriteLine($"Signed policy written to: {outputPath}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // verify-policy — Verify a signed policy against the root public key
    // ═══════════════════════════════════════════════════════════════════════

    private static void VerifyPolicy(string signedPolicyPath)
    {
        var rootDir = Path.Combine(KeysBasePath, "root");
        var publicKeyPath = Path.Combine(rootDir, "root_public.json");

        if (!File.Exists(publicKeyPath))
        {
            throw new InvalidOperationException("Root public key not found at: " + publicKeyPath);
        }

        if (!File.Exists(signedPolicyPath))
        {
            throw new FileNotFoundException("Signed policy file not found.", signedPolicyPath);
        }

        // Load root public key
        string publicKeyJson = File.ReadAllText(publicKeyPath);
        var pubDoc = JsonDocument.Parse(publicKeyJson);
        string algorithm = pubDoc.RootElement.GetProperty("algorithm").GetString() ?? "";
        if (algorithm != "ECDSA-P256")
        {
            throw new InvalidOperationException($"Unsupported root key algorithm: {algorithm}");
        }

        string publicKeyBase64 = pubDoc.RootElement.GetProperty("publicKey").GetString() ?? "";
        byte[] publicKeyBytes = Convert.FromBase64String(publicKeyBase64);

        using var verifier = ECDsa.Create();
        verifier.ImportSubjectPublicKeyInfo(publicKeyBytes, out _);

        // Load signed policy
        string signedPolicyJson = File.ReadAllText(signedPolicyPath);
        JsonNode? node = JsonNode.Parse(signedPolicyJson);
        if (node is null || node is not JsonObject policyObj)
        {
            throw new InvalidOperationException("Policy JSON must be a JSON object at the root.");
        }

        // Extract signature block
        if (!policyObj.TryGetPropertyValue("signature", out JsonNode? sigNode) || sigNode is not JsonObject sigObj)
        {
            throw new InvalidOperationException("Policy is missing 'signature' block.");
        }

        string sigAlgorithm = sigObj["algorithm"]?.GetValue<string>() ?? "";
        string sigHash      = sigObj["hash"]?.GetValue<string>() ?? "";
        string sigSignedBy  = sigObj["signedBy"]?.GetValue<string>() ?? "";
        string sigValue     = sigObj["value"]?.GetValue<string>() ?? "";

        if (sigAlgorithm != "ECDSA-P256" || sigHash != "SHA-512" || sigSignedBy != "OBSCURA-ROOT-1")
        {
            throw new InvalidOperationException(
                $"Untrusted signature metadata: alg={sigAlgorithm}, hash={sigHash}, signedBy={sigSignedBy}");
        }

        byte[] signatureBytes = Convert.FromBase64String(sigValue);

        // Remove signature field for hash verification
        policyObj.Remove("signature");

        string unsignedJson = policyObj.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false
        });

        byte[] dataBytes = Encoding.UTF8.GetBytes(unsignedJson);

        byte[] hash;
        using (var sha = SHA512.Create())
        {
            hash = sha.ComputeHash(dataBytes);
        }

        bool valid = verifier.VerifyHash(hash, signatureBytes);

        if (valid)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("VALID — Policy signature verified successfully.");
            Console.ResetColor();
            Console.WriteLine($"  Signed by : {sigSignedBy}");
            Console.WriteLine($"  Algorithm : {sigAlgorithm} + {sigHash}");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("INVALID — Policy signature verification FAILED.");
            Console.ResetColor();
            throw new CryptographicException("Policy signature verification failed.");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Key loading — raw key file first, PFX fallback
    // ═══════════════════════════════════════════════════════════════════════

    private static ECDsa LoadRootSigningKey()
    {
        // Path 1: Raw ECDSA private key (created by init-root)
        var rawKeyPath = Path.Combine(KeysBasePath, "root", "root_private.key");
        if (File.Exists(rawKeyPath))
        {
            Console.WriteLine("Loading root key from raw key file...");
            string privateKeyBase64 = File.ReadAllText(rawKeyPath).Trim();
            byte[] privateKeyBytes = Convert.FromBase64String(privateKeyBase64);

            var ecdsa = ECDsa.Create();
            ecdsa.ImportECPrivateKey(privateKeyBytes, out _);
            return ecdsa;
        }

        // Path 2: PFX certificate fallback
        var pfxPath = Path.Combine(CertsBasePath, "obscura_root.pfx");
        if (File.Exists(pfxPath))
        {
            Console.WriteLine("Loading root key from PFX certificate...");

            var envPassword = Environment.GetEnvironmentVariable("OBSCURA_ROOT_PFX_PASSWORD");
            string password;
            if (!string.IsNullOrEmpty(envPassword))
            {
                password = envPassword;
            }
            else
            {
                Console.Write("Enter root certificate password: ");
                password = ReadPasswordFromConsole();
            }

            var cert = new X509Certificate2(
                pfxPath,
                password,
                X509KeyStorageFlags.EphemeralKeySet |
                X509KeyStorageFlags.MachineKeySet |
                X509KeyStorageFlags.Exportable);

            var ecdsa = cert.GetECDsaPrivateKey();
            if (ecdsa is null)
                throw new InvalidOperationException("Root certificate does not contain an ECDSA private key.");

            return ecdsa;
        }

        throw new InvalidOperationException(
            "No root signing key found.\n" +
            $"  Expected raw key at: {rawKeyPath}\n" +
            $"  Or PFX cert at:      {pfxPath}\n" +
            "Run `init-root` to generate a new keypair.");
    }

    private static string ReadPasswordFromConsole()
    {
        var sb = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }
            if (key.Key == ConsoleKey.Backspace && sb.Length > 0)
            {
                sb.Length--;
                continue;
            }
            if (!char.IsControl(key.KeyChar))
                sb.Append(key.KeyChar);
        }
        return sb.ToString();
    }
}
