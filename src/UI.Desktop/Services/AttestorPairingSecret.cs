using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PhantomVault.UI.Services;

/// <summary>
/// Obscura's view of the pairing secret shared with Phantom Attestor's credential broker.
///
/// <para>
/// Attestor owns the secret and writes it, DPAPI-protected for the current Windows user, when
/// its broker first starts. Obscura reads the same file rather than being handed the secret
/// over the pipe, because a handshake that issues the secret to whoever asks would authenticate
/// nothing — the thing being proved is possession of a file only this user's processes can
/// unprotect.
/// </para>
///
/// <para>
/// <b>Honest scope.</b> Both applications run as the same user, so a same-user adversary can
/// unprotect this too. It is not a boundary against local code running as you; paired with
/// Authenticode verification of the calling image it raises the cost from "rename an exe" to
/// "read another application's protected store or inject into a signed process", and it gives
/// the pairing something revocable. Anything stronger needs the secret held somewhere the user
/// account cannot silently reach — a TPM-sealed key or an OS credential prompt.
/// </para>
/// </summary>
internal static class AttestorPairingSecret
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Phantom.Attestor.BrokerPairing.v1");

    private static string SecretPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PhantomAttestor", "Broker", "pairing.dat");

    /// <summary>
    /// The pairing secret. Throws when Attestor has never run — callers surface that as
    /// "Attestor is not available" rather than silently degrading to an unauthenticated call,
    /// which would defeat the point of pairing.
    /// </summary>
    internal static byte[] Read()
    {
        var path = SecretPath;
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                "Phantom Attestor has not been paired with this Obscura install yet. "
                + "Start Attestor once to establish the pairing.");
        }

        try
        {
            return ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                "The Phantom Attestor pairing could not be read by this Windows user.", ex);
        }
    }

    /// <summary>True when a pairing exists, for callers that probe availability rather than act.</summary>
    internal static bool Exists() => File.Exists(SecretPath);
}
