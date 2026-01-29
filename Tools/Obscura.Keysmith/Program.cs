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

                case "sign-policy":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: sign-policy <inputPolicy.json> <outputPolicy.signed.json>");
                        return 1;
                    }
                    SignPolicy(args[1], args[2]);
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
        Console.WriteLine("  sign-policy <inputPolicy.json> <outputPolicy.signed.json>");
        Console.WriteLine("      Signs a policy JSON file using the root private key.");
        Console.WriteLine();
    }

    #region Root key

    private static void InitRootKey()
    {
        var rootDir = Path.Combine(KeysBasePath, "root");
        Directory.CreateDirectory(rootDir);

        var privateKeyPath = Path.Combine(rootDir, "root_private.key");
        var publicKeyPath  = Path.Combine(rootDir, "root_public.json");

        if (File.Exists(privateKeyPath) || File.Exists(publicKeyPath))
        {
            Console.WriteLine("Root key already exists. Delete the files in ./keys/root if you want to regenerate.");
            return;
        }

        Console.WriteLine("Generating root ECDSA keypair (P-256)...");

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        // Export keys
        byte[] privateKeyBytes = ecdsa.ExportECPrivateKey();
        byte[] publicKeyBytes  = ecdsa.ExportSubjectPublicKeyInfo();

        // Save private key as Base64 (v1 - keep folder offline / out of git)
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
        Console.WriteLine("IMPORTANT: Keep ./keys/root OFFLINE and out of source control.");
    }

    #endregion

    #region Policy signing

    private static void SignPolicy(string inputPath, string outputPath)
    {
        var rootDir = Path.Combine(KeysBasePath, "root");
        var privateKeyPath = Path.Combine(rootDir, "root_private.key");
        var publicKeyPath  = Path.Combine(rootDir, "root_public.json");

        if (!File.Exists(privateKeyPath) || !File.Exists(publicKeyPath))
        {
            throw new InvalidOperationException("Root keys not found. Run `init-root` first.");
        }

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("Policy file not found", inputPath);
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

        // Canonical-ish serialization (no indentation)
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

        // Load root private key from certificate
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
    private static ECDsa LoadRootSigningKey()
    {
        string certDir = Path.Combine(AppContext.BaseDirectory, "certs");
        string pfxPath = Path.Combine(certDir, "obscura_root.pfx");

        if (!File.Exists(pfxPath))
            throw new InvalidOperationException("Root PFX not found. Expected at: " + pfxPath);

        // Allow non-interactive use via environment variable to support CI/automation
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
    if (ecdsa == null)
        throw new InvalidOperationException("Root certificate does not contain an ECDSA private key.");

    return ecdsa;
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

    #endregion
}
