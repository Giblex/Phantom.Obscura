using PhantomKey.Integration;
using PhantomVault.Core.Services.Integrity;

namespace PhantomVault.PrivilegedBroker;

internal sealed class PhantomKeyWatchdogAnchorProvider : IIntegrityAnchorProvider
{
    private readonly PhantomKeyClient _client = new("phantom-obscura-integrity-watchdog");

    public async Task<IntegrityAnchorProof> SignDigestAsync(byte[] sha256Digest, int tier, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PhantomKeyTransactionKeyInfo key = await _client.GetTransactionSigningKeyAsync().ConfigureAwait(false);
        byte[] signature = await _client.SignTxnAsync(sha256Digest, tier).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Phantom Key returned no integrity anchor signature.");
        return new IntegrityAnchorProof("PhantomKey", key.KeyId, key.Algorithm,
            key.PublicKeyB64, Convert.ToBase64String(signature));
    }
}
