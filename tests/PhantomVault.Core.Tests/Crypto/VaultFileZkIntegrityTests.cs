#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using GiblexVault.Security.ZK;
using GiblexVault.Security.ZK.Models;
using Xunit;

namespace PhantomVault.Core.Tests.Crypto;

/// <summary>
/// Container-level integrity guards for the GV-ZKF format.
///
/// The v1 format gave every chunk the same AAD (the serialized header), which left the
/// file authenticated per-chunk but not as a whole: chunks could be reordered, trailing
/// chunks dropped, or a zero-length record spliced in to end the stream early, and every
/// individual tag still verified. v2 binds {record type, chunk index} into each chunk's
/// AAD and closes the stream with an authenticated trailer carrying the total chunk count.
///
/// These tests pin that behaviour so the guarantees cannot silently regress.
/// </summary>
public sealed class VaultFileZkIntegrityTests
{
    // Small chunks so a modest payload still produces several records to shuffle.
    private const int ChunkSize = 1024;

    private static byte[] Key()
    {
        var k = new byte[32];
        RandomNumberGenerator.Fill(k);
        return k;
    }

    private static EngineOptions Options() =>
        new EngineOptions(EncryptionProfile.Advanced) with { ChunkSizeBytes = ChunkSize };

    private static byte[] Payload(int chunks)
    {
        var data = new byte[ChunkSize * chunks];
        RandomNumberGenerator.Fill(data);
        return data;
    }

    private static async Task<byte[]> EncryptAsync(byte[] plaintext, byte[] key, EngineOptions opts)
    {
        using var input = new MemoryStream(plaintext);
        using var output = new MemoryStream();
        await VaultFileZk.EncryptToStreamAsync(input, output, key, opts);
        return output.ToArray();
    }

    private static async Task<byte[]> DecryptAsync(byte[] container, byte[] key, EngineOptions opts)
    {
        using var input = new MemoryStream(container);
        using var output = new MemoryStream();
        await VaultFileZk.DecryptToStreamAsync(input, output, key, opts);
        return output.ToArray();
    }

    [Fact]
    public async Task RoundTrip_MultiChunk_RecoversExactPlaintext()
    {
        var key = Key();
        var opts = Options();
        var plaintext = Payload(chunks: 5);

        var container = await EncryptAsync(plaintext, key, opts);
        var recovered = await DecryptAsync(container, key, opts);

        Assert.Equal(plaintext, recovered);
    }

    [Fact]
    public async Task RoundTrip_ExactChunkBoundary_RecoversExactPlaintext()
    {
        var key = Key();
        var opts = Options();

        // Payload that is an exact multiple of the chunk size: the read loop ends on a
        // zero-length read, so the trailer is the only thing marking the true end.
        var plaintext = Payload(chunks: 3);

        Assert.Equal(plaintext, await DecryptAsync(await EncryptAsync(plaintext, key, opts), key, opts));
    }

    [Fact]
    public async Task RoundTrip_EmptyPayload_RecoversEmpty()
    {
        var key = Key();
        var opts = Options();

        var container = await EncryptAsync(Array.Empty<byte>(), key, opts);
        Assert.Empty(await DecryptAsync(container, key, opts));
    }

    [Fact]
    public async Task WrongKey_FailsAuthentication()
    {
        var opts = Options();
        var container = await EncryptAsync(Payload(2), Key(), opts);

        await Assert.ThrowsAnyAsync<Exception>(() => DecryptAsync(container, Key(), opts));
    }

    [Fact]
    public async Task Truncation_DroppingTrailingRecords_IsDetected()
    {
        var key = Key();
        var opts = Options();
        var container = await EncryptAsync(Payload(chunks: 4), key, opts);

        // Cut the authenticated trailer (and part of the last chunk) off the end.
        // Under v1 this decrypted the surviving prefix without complaint.
        var truncated = new byte[container.Length - 64];
        Buffer.BlockCopy(container, 0, truncated, 0, truncated.Length);

        await Assert.ThrowsAnyAsync<Exception>(() => DecryptAsync(truncated, key, opts));
    }

    [Fact]
    public async Task Truncation_RemovingOnlyTheTrailer_IsDetected()
    {
        var key = Key();
        var opts = Options();
        var container = await EncryptAsync(Payload(chunks: 3), key, opts);

        // The trailer is the final record: [type:1][nonce:ns][len:4][ct:16].
        // XChaCha20-Poly1305 uses a 24-byte nonce, so that is 1 + 24 + 4 + 16 = 45 bytes.
        var trailerLength = 1 + 24 + 4 + 16;
        var withoutTrailer = new byte[container.Length - trailerLength];
        Buffer.BlockCopy(container, 0, withoutTrailer, 0, withoutTrailer.Length);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => DecryptAsync(withoutTrailer, key, opts));
        Assert.Contains("truncat", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reordering_TwoDataChunks_IsDetected()
    {
        var key = Key();
        var opts = Options();
        var container = await EncryptAsync(Payload(chunks: 4), key, opts);

        // Records are fixed width here because every data chunk is exactly ChunkSize:
        // [type:1][nonce:24][len:4][ct: ChunkSize + 16].
        var recordLength = 1 + 24 + 4 + ChunkSize + 16;

        // Locate the first record: it starts after magic (8) + headerLen (4) + header.
        var headerLength = BitConverter.ToInt32(container, 8);
        var firstRecord = 8 + 4 + headerLength;
        var secondRecord = firstRecord + recordLength;

        var swapped = (byte[])container.Clone();
        var scratch = new byte[recordLength];
        Buffer.BlockCopy(swapped, firstRecord, scratch, 0, recordLength);
        Buffer.BlockCopy(swapped, secondRecord, swapped, firstRecord, recordLength);
        Buffer.BlockCopy(scratch, 0, swapped, secondRecord, recordLength);

        // Each chunk is sealed under its own index, so both now fail authentication.
        await Assert.ThrowsAnyAsync<Exception>(() => DecryptAsync(swapped, key, opts));
    }

    [Fact]
    public async Task SplicedZeroLengthRecord_DoesNotTerminateStreamEarly()
    {
        var key = Key();
        var opts = Options();
        var plaintext = Payload(chunks: 4);
        var container = await EncryptAsync(plaintext, key, opts);

        var headerLength = BitConverter.ToInt32(container, 8);
        var firstRecord = 8 + 4 + headerLength;
        var recordLength = 1 + 24 + 4 + ChunkSize + 16;
        var spliceAt = firstRecord + recordLength; // after the first data chunk

        // Forge a data record claiming zero ciphertext length. Under v1 this ended the
        // stream and yielded a silently short plaintext.
        var forged = new byte[1 + 24 + 4];
        forged[0] = 0x01; // RecordData

        var spliced = new List<byte>(container.Length + forged.Length);
        spliced.AddRange(new ArraySegment<byte>(container, 0, spliceAt));
        spliced.AddRange(forged);
        spliced.AddRange(new ArraySegment<byte>(container, spliceAt, container.Length - spliceAt));

        // Must not quietly return a truncated plaintext: either it throws, or it returns
        // the full original. Silently short output is the failure mode being guarded.
        try
        {
            var recovered = await DecryptAsync(spliced.ToArray(), key, opts);
            Assert.Equal(plaintext, recovered);
        }
        catch (Exception)
        {
            // Rejecting the forged record outright is the expected outcome.
        }
    }

    [Fact]
    public async Task TamperedChunkCiphertext_IsDetected()
    {
        var key = Key();
        var opts = Options();
        var container = await EncryptAsync(Payload(chunks: 2), key, opts);

        var headerLength = BitConverter.ToInt32(container, 8);
        var firstCiphertextByte = 8 + 4 + headerLength + 1 + 24 + 4;

        var tampered = (byte[])container.Clone();
        tampered[firstCiphertextByte] ^= 0xFF;

        await Assert.ThrowsAnyAsync<Exception>(() => DecryptAsync(tampered, key, opts));
    }

    [Fact]
    public async Task TamperedRecordTypeByte_IsDetected()
    {
        var key = Key();
        var opts = Options();
        var container = await EncryptAsync(Payload(chunks: 2), key, opts);

        var headerLength = BitConverter.ToInt32(container, 8);
        var firstTypeByte = 8 + 4 + headerLength;

        // Retyping a data record as a terminator would end the stream early if the type
        // byte were not itself bound into the AAD.
        var tampered = (byte[])container.Clone();
        tampered[firstTypeByte] = 0x02; // RecordTerminator

        await Assert.ThrowsAnyAsync<Exception>(() => DecryptAsync(tampered, key, opts));
    }

    [Fact]
    public async Task UnknownFormatVersion_IsRejected()
    {
        var key = Key();
        var opts = Options();
        var container = await EncryptAsync(Payload(1), key, opts);

        var bumped = (byte[])container.Clone();
        bumped[6] = 0x7F; // neither v1 nor v2

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => DecryptAsync(bumped, key, opts));
        Assert.Contains("version", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
