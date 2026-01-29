using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PhantomVault.Core.Services;

/// <summary>
/// Provides certificate-based root trust verification for policy and manifest signing.
/// Replaces the previous raw public key approach with X.509 certificate-based verification.
/// </summary>
public static class RootTrust
{
    /// <summary>
    /// Creates an ECDsa verifier from the root certificate file.
    /// </summary>
    /// <param name="certPath">Path to the obscura_root.crt certificate file</param>
    /// <returns>ECDsa instance configured for signature verification</returns>
    /// <exception cref="FileNotFoundException">If certificate file is not found</exception>
    /// <exception cref="InvalidOperationException">If certificate does not contain an ECDSA public key</exception>
    /// <remarks>
    /// SECURITY: The returned ECDsa instance must be disposed by the caller.
    /// The certificate is disposed immediately after key extraction.
    /// </remarks>
    public static ECDsa CreateRootVerifierFromCert(string certPath)
    {
        if (!File.Exists(certPath))
            throw new FileNotFoundException("Root certificate not found", certPath);

        byte[] certBytes = File.ReadAllBytes(certPath);

        // SECURITY FIX: Dispose certificate after extracting the public key
        using var cert = new X509Certificate2(certBytes);

        var ecdsa = cert.GetECDsaPublicKey();
        if (ecdsa == null)
            throw new InvalidOperationException("Root certificate does not contain an ECDSA public key.");

        return ecdsa;
    }

    /// <summary>
    /// Creates an ECDsa verifier from an embedded certificate resource.
    /// </summary>
    /// <param name="resourceName">Full resource name (e.g., "PhantomVault.UI.Assets.Policies.obscura_root.crt")</param>
    /// <param name="assembly">Assembly containing the embedded resource</param>
    /// <returns>ECDsa instance configured for signature verification</returns>
    /// <exception cref="InvalidOperationException">If resource is not found or certificate is invalid</exception>
    /// <remarks>
    /// SECURITY: The returned ECDsa instance must be disposed by the caller.
    /// The certificate is disposed immediately after key extraction.
    /// </remarks>
    public static ECDsa CreateRootVerifierFromEmbeddedCert(string resourceName, System.Reflection.Assembly assembly)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new InvalidOperationException($"Root certificate resource not found: {resourceName}");

        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        byte[] certBytes = memoryStream.ToArray();

        // SECURITY FIX: Dispose certificate after extracting the public key
        using var cert = new X509Certificate2(certBytes);

        var ecdsa = cert.GetECDsaPublicKey();
        if (ecdsa == null)
            throw new InvalidOperationException("Root certificate does not contain an ECDSA public key.");

        return ecdsa;
    }
}
