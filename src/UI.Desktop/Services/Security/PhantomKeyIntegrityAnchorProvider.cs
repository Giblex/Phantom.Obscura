using System;
using System.Threading;
using System.Threading.Tasks;
using PhantomKey.Integration;
using PhantomVault.Core.Services.Integrity;

namespace PhantomVault.UI.Services.Security;

public sealed class PhantomKeyIntegrityAnchorProvider : IIntegrityAnchorProvider
{
    private readonly PhantomKeyClient _client;

    public PhantomKeyIntegrityAnchorProvider(PhantomKeyClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<IntegrityAnchorProof> SignDigestAsync(
        byte[] sha256Digest, int tier, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sha256Digest);
        if (sha256Digest.Length != 32) throw new ArgumentException("A SHA-256 digest is required.", nameof(sha256Digest));
        cancellationToken.ThrowIfCancellationRequested();
        PhantomKeyTransactionKeyInfo key = await _client.GetTransactionSigningKeyAsync().ConfigureAwait(false);
        byte[] signature = await _client.SignTxnAsync(sha256Digest, tier).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Phantom Key returned no transaction signature.");
        cancellationToken.ThrowIfCancellationRequested();
        return new IntegrityAnchorProof("PhantomKey", key.KeyId, key.Algorithm,
            key.PublicKeyB64, Convert.ToBase64String(signature));
    }
}
