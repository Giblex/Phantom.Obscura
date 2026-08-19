using System.Security.Cryptography;

namespace PhantomVault.PrivilegedBroker;

internal static class ProtectedIntegrityKeyStore
{
    private static readonly byte[] Entropy = "Phantom.Obscura:IntegrityAuditKey:v1"u8.ToArray();

    public static byte[] LoadOrCreate(string path)
    {
        if (File.Exists(path))
            return ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.LocalMachine);

        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] protectedKey = ProtectedData.Protect(key, Entropy, DataProtectionScope.LocalMachine);
        string temporary = path + ".tmp." + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(temporary, protectedKey);
        File.Move(temporary, path, false);
        CryptographicOperations.ZeroMemory(protectedKey);
        return key;
    }
}
