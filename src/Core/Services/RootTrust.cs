using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PhantomVault.Core.Services;

public static class RootTrust
{

    public static ECDsa CreateRootVerifierFromCert(string certPath)
    {
        if (!File.Exists(certPath))
            throw new FileNotFoundException("Root certificate not found", certPath);

        byte[] certBytes = File.ReadAllBytes(certPath);

        using var cert = X509CertificateLoader.LoadCertificate(certBytes);

        var ecdsa = cert.GetECDsaPublicKey();
        if (ecdsa == null)
            throw new InvalidOperationException("Root certificate does not contain an ECDSA public key.");

        return ecdsa;
    }

    public static ECDsa CreateRootVerifierFromEmbeddedCert(string resourceName, System.Reflection.Assembly assembly)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new InvalidOperationException($"Root certificate resource not found: {resourceName}");

        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        byte[] certBytes = memoryStream.ToArray();

        using var cert = X509CertificateLoader.LoadCertificate(certBytes);

        var ecdsa = cert.GetECDsaPublicKey();
        if (ecdsa == null)
            throw new InvalidOperationException("Root certificate does not contain an ECDSA public key.");

        return ecdsa;
    }
}

