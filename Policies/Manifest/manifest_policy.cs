switch (args[0])
{
    case "sign-manifest":
        if (args.Length < 3)
        {
            Console.WriteLine("Usage: sign-manifest <inputManifest.json> <outputManifest.signed.json>");
            return 1;
        }
        SignGenericJsonWithRoot(args[1], args[2], "manifest");
        break;
}

static void SignGenericJsonWithRoot(string inputPath, string outputPath, string objectType)
{
    var rootDir = Path.Combine(KeysBasePath, "root");
    var privateKeyPath = Path.Combine(rootDir, "root_private.key");

    if (!File.Exists(privateKeyPath))
        throw new InvalidOperationException("Root private key not found. Run `init-root` first.");

    if (!File.Exists(inputPath))
        throw new FileNotFoundException($"{objectType} file not found", inputPath);

    string json = File.ReadAllText(inputPath);
    JsonNode? node = JsonNode.Parse(json);
    if (node is null || node is not JsonObject obj)
        throw new InvalidOperationException($"{objectType} JSON must be an object at the root.");

    obj.Remove("signature");

    string unsignedJson = obj.ToJsonString(new JsonSerializerOptions
    {
        WriteIndented = false
    });

    byte[] dataBytes = Encoding.UTF8.GetBytes(unsignedJson);

    byte[] hash;
    using (var sha = SHA512.Create())
        hash = sha.ComputeHash(dataBytes);

    // Load root private key from certificate
    using var ecdsa = LoadRootSigningKey();

    byte[] signatureBytes = ecdsa.SignHash(hash);
    string signatureBase64 = Convert.ToBase64String(signatureBytes);

    var sigNode = new JsonObject
    {
        ["algorithm"] = "ECDSA-P256",
        ["hash"]      = "SHA-512",
        ["signedBy"]  = "OBSCURA-ROOT-1",
        ["value"]     = signatureBase64
    };

    obj["signature"] = sigNode;

    string signedJson = obj.ToJsonString(new JsonSerializerOptions
    {
        WriteIndented = true
    });

    File.WriteAllText(outputPath, signedJson);

    Console.WriteLine($"Signed {objectType} written to: {outputPath}");
}

static ECDsa LoadRootSigningKey()
{
    string certDir = Path.Combine(AppContext.BaseDirectory, "certs");
    string pfxPath = Path.Combine(certDir, "obscura_root.pfx");

    if (!File.Exists(pfxPath))
        throw new InvalidOperationException("Root PFX not found. Expected at: " + pfxPath);

    Console.Write("Enter root certificate password: ");
    string password = ReadPasswordFromConsole();

    var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(
        pfxPath,
        password,
        System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.EphemeralKeySet |
        System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.MachineKeySet |
        System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.Exportable);

    var ecdsa = cert.GetECDsaPrivateKey();
    if (ecdsa == null)
        throw new InvalidOperationException("Root certificate does not contain an ECDSA private key.");

    return ecdsa;
}

static string ReadPasswordFromConsole()
{
    var password = new System.Text.StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
            break;
        if (key.Key == ConsoleKey.Backspace && password.Length > 0)
            password.Length--;
        else if (!char.IsControl(key.KeyChar))
            password.Append(key.KeyChar);
    }
    Console.WriteLine();
    return password.ToString();
}
