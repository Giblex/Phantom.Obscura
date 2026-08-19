using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services.Integrity;

public sealed record IntegrityAnchorProof(
    string Provider,
    string KeyId,
    string Algorithm,
    string PublicKeyB64,
    string SignatureEnvelopeB64);

public sealed record IntegrityAnchorReceipt(
    long Sequence,
    DateTimeOffset TimestampUtc,
    string AuditHeadHash,
    string ManifestKeyId,
    string ChallengeSha256,
    IntegrityAnchorProof Proof);

public interface IIntegrityAnchorProvider
{
    Task<IntegrityAnchorProof> SignDigestAsync(byte[] sha256Digest, int tier, CancellationToken cancellationToken);
}

/// <summary>
/// Anchors an authenticated audit head to an independently verifiable Phantom Key
/// TPM signature. Receipts contain public material only and can be verified offline.
/// </summary>
public sealed class IntegrityAnchorCoordinator
{
    private static readonly byte[] Domain = Encoding.UTF8.GetBytes("Phantom.Obscura:IntegrityAnchor:v1\0");
    private readonly TamperEvidentIntegrityLog _log;
    private readonly IIntegrityAnchorProvider _provider;
    private readonly string _receiptPath;
    private readonly string? _expectedKeyId;

    public IntegrityAnchorCoordinator(TamperEvidentIntegrityLog log, IIntegrityAnchorProvider provider,
        string receiptPath, string? expectedKeyId = null)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _receiptPath = Path.GetFullPath(receiptPath);
        _expectedKeyId = expectedKeyId;
    }

    public async Task<IntegrityAnchorReceipt?> AnchorCurrentHeadAsync(
        string manifestKeyId, int tier = 1, CancellationToken cancellationToken = default)
    {
        IntegrityEvent? head = _log.GetVerifiedHead();
        if (head is null) return null;
        byte[] digest = CreateChallenge(head.Sequence, head.Hash, manifestKeyId);
        IntegrityAnchorProof proof = await _provider.SignDigestAsync(digest, tier, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(_expectedKeyId) &&
            !string.Equals(_expectedKeyId, proof.KeyId, StringComparison.Ordinal))
            throw new CryptographicException("Phantom Key transaction-signing identity changed.");
        if (!VerifyProof(digest, proof))
            throw new CryptographicException("Phantom Key returned an invalid integrity anchor signature.");

        var receipt = new IntegrityAnchorReceipt(head.Sequence, DateTimeOffset.UtcNow, head.Hash,
            manifestKeyId, Convert.ToHexString(digest).ToLowerInvariant(), proof);
        Directory.CreateDirectory(Path.GetDirectoryName(_receiptPath)!);
        File.AppendAllText(_receiptPath, JsonSerializer.Serialize(receipt) + Environment.NewLine, new UTF8Encoding(false));
        return receipt;
    }

    public IReadOnlyList<IntegrityAnchorReceipt> ReadAndVerifyReceipts()
    {
        var receipts = new List<IntegrityAnchorReceipt>();
        if (!File.Exists(_receiptPath)) return receipts;
        long previousSequence = 0;
        foreach (string line in File.ReadLines(_receiptPath))
        {
            var receipt = JsonSerializer.Deserialize<IntegrityAnchorReceipt>(line)
                ?? throw new InvalidDataException("Invalid integrity anchor receipt.");
            byte[] digest = CreateChallenge(receipt.Sequence, receipt.AuditHeadHash, receipt.ManifestKeyId);
            if (receipt.Sequence <= previousSequence ||
                !string.Equals(receipt.ChallengeSha256, Convert.ToHexString(digest).ToLowerInvariant(), StringComparison.Ordinal) ||
                (!string.IsNullOrWhiteSpace(_expectedKeyId) && receipt.Proof.KeyId != _expectedKeyId) ||
                !VerifyProof(digest, receipt.Proof))
                throw new InvalidDataException($"Integrity anchor verification failed at audit sequence {receipt.Sequence}.");
            receipts.Add(receipt);
            previousSequence = receipt.Sequence;
        }
        return receipts;
    }

    internal static bool VerifyProof(byte[] digest, IntegrityAnchorProof proof)
    {
        if (proof.Algorithm != "ECDSA-P256-SHA256") return false;
        try
        {
            byte[] envelope = Convert.FromBase64String(proof.SignatureEnvelopeB64);
            var transactionProof = ParseTransactionSignature(envelope, digest);
            byte[] publicKey = Convert.FromBase64String(proof.PublicKeyB64);
            string computedKeyId = Convert.ToHexString(SHA256.HashData(publicKey)).ToLowerInvariant();
            if (!string.Equals(computedKeyId, proof.KeyId, StringComparison.Ordinal)) return false;
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKey, out int consumed);
            return consumed == publicKey.Length && ecdsa.VerifyHash(transactionProof.BindingHash, transactionProof.Signature);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    public static bool VerifyReceipt(IntegrityAnchorReceipt receipt, string? expectedKeyId = null)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (!string.IsNullOrWhiteSpace(expectedKeyId) && receipt.Proof.KeyId != expectedKeyId) return false;
        byte[] digest = CreateChallenge(receipt.Sequence, receipt.AuditHeadHash, receipt.ManifestKeyId);
        return string.Equals(receipt.ChallengeSha256, Convert.ToHexString(digest).ToLowerInvariant(), StringComparison.Ordinal) &&
               VerifyProof(digest, receipt.Proof);
    }

    private static byte[] CreateChallenge(long sequence, string headHash, string manifestKeyId)
    {
        byte[] sequenceBytes = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(sequenceBytes, sequence);
        byte[] head = Convert.FromHexString(headHash);
        byte[] manifest = Encoding.UTF8.GetBytes(manifestKeyId ?? string.Empty);
        byte[] payload = new byte[Domain.Length + sequenceBytes.Length + head.Length + manifest.Length];
        Domain.CopyTo(payload, 0);
        sequenceBytes.CopyTo(payload, Domain.Length);
        head.CopyTo(payload, Domain.Length + sequenceBytes.Length);
        manifest.CopyTo(payload, Domain.Length + sequenceBytes.Length + head.Length);
        return SHA256.HashData(payload);
    }

    private static ParsedTransactionProof ParseTransactionSignature(byte[] envelope, byte[] digest)
    {
        if (envelope.Length < 9 || envelope[0] != 'P' || envelope[1] != 'K' ||
            envelope[2] != 'T' || envelope[3] != '2')
            throw new CryptographicException("Invalid Phantom Key transaction signature envelope.");
        int tier = envelope[4];
        int signatureLength = envelope[5] | (envelope[6] << 8);
        int usbMacLength = envelope[7] | (envelope[8] << 8);
        if (tier is < 1 or > 4 || signatureLength <= 0 || 9 + signatureLength + usbMacLength != envelope.Length ||
            (tier >= 3 ? usbMacLength != 32 : usbMacLength != 0))
            throw new CryptographicException("Malformed Phantom Key transaction signature envelope.");
        byte[] signature = envelope.AsSpan(9, signatureLength).ToArray();
        byte[] usbMac = envelope.AsSpan(9 + signatureLength, usbMacLength).ToArray();
        byte[] domain = Encoding.UTF8.GetBytes("PhantomKey:TxnSignature:v2\0");
        byte[] input = new byte[domain.Length + 1 + digest.Length + usbMac.Length];
        domain.CopyTo(input, 0);
        input[domain.Length] = (byte)tier;
        digest.CopyTo(input, domain.Length + 1);
        usbMac.CopyTo(input, domain.Length + 1 + digest.Length);
        byte[] bindingHash = SHA256.HashData(input);
        CryptographicOperations.ZeroMemory(input);
        CryptographicOperations.ZeroMemory(usbMac);
        return new ParsedTransactionProof(signature, bindingHash, tier);
    }

    private sealed record ParsedTransactionProof(byte[] Signature, byte[] BindingHash, int Tier);
}
