using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;

namespace PhantomVault.UI.Services;

public sealed class EntropyKeyfileGenerationResult
{
    public required byte[] KeyMaterial { get; init; }
    public required int CollectedEntropyBits { get; init; }
    public required int SampleCount { get; init; }
}

public sealed class EntropyKeyfileGenerator : IDisposable
{
    private const int MinimumEntropyBits = 4096;

    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly byte[] _seed;

    private long _lastSampleTicks;
    private double _lastX = double.NaN;
    private double _lastY = double.NaN;
    private int _collectedEntropyBits;
    private int _sampleCount;
    private bool _finalized;

    public EntropyKeyfileGenerator()
    {
        _seed = RandomNumberGenerator.GetBytes(64);
        _hash.AppendData(_seed);
        _lastSampleTicks = Stopwatch.GetTimestamp();
    }

    public int CollectedEntropyBits => _collectedEntropyBits;
    public int SampleCount => _sampleCount;
    public int MinimumRequiredBits => MinimumEntropyBits;
    public bool CanFinalize => _collectedEntropyBits >= MinimumEntropyBits;

    public void AddMouseSample(double x, double y, bool leftPressed, bool rightPressed)
    {
        ThrowIfFinalized();

        var timestamp = Stopwatch.GetTimestamp();
        var elapsedTicks = timestamp - _lastSampleTicks;
        _lastSampleTicks = timestamp;

        var deltaX = double.IsNaN(_lastX) ? 0d : x - _lastX;
        var deltaY = double.IsNaN(_lastY) ? 0d : y - _lastY;
        _lastX = x;
        _lastY = y;

        Span<byte> sample = stackalloc byte[40];
        BinaryPrimitives.WriteDoubleLittleEndian(sample[0..8], x);
        BinaryPrimitives.WriteDoubleLittleEndian(sample[8..16], y);
        BinaryPrimitives.WriteDoubleLittleEndian(sample[16..24], deltaX);
        BinaryPrimitives.WriteDoubleLittleEndian(sample[24..32], deltaY);
        BinaryPrimitives.WriteInt64LittleEndian(sample[32..40], elapsedTicks);
        _hash.AppendData(sample);

        Span<byte> flags = stackalloc byte[2];
        flags[0] = leftPressed ? (byte)1 : (byte)0;
        flags[1] = rightPressed ? (byte)1 : (byte)0;
        _hash.AppendData(flags);

        _sampleCount++;
        _collectedEntropyBits = Math.Min(
            65536,
            _collectedEntropyBits + EstimateSampleEntropy(deltaX, deltaY, elapsedTicks, leftPressed, rightPressed));
    }

    public EntropyKeyfileGenerationResult FinalizeKeyMaterial(int sizeBytes)
    {
        ThrowIfFinalized();

        if (sizeBytes < 1024)
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), "Keyfile output must be at least 1024 bytes.");

        if (!CanFinalize)
            throw new InvalidOperationException("Not enough user entropy has been collected yet.");

        _finalized = true;

        var finalSalt = RandomNumberGenerator.GetBytes(64);
        var digest = _hash.GetHashAndReset();
        var info = BuildInfoBlock();

        var ikm = new byte[_seed.Length + digest.Length + finalSalt.Length];
        Buffer.BlockCopy(_seed, 0, ikm, 0, _seed.Length);
        Buffer.BlockCopy(digest, 0, ikm, _seed.Length, digest.Length);
        Buffer.BlockCopy(finalSalt, 0, ikm, _seed.Length + digest.Length, finalSalt.Length);

        var output = HkdfSha512(ikm, finalSalt, info, sizeBytes);

        CryptographicOperations.ZeroMemory(ikm);
        CryptographicOperations.ZeroMemory(digest);
        CryptographicOperations.ZeroMemory(finalSalt);
        CryptographicOperations.ZeroMemory(info);
        CryptographicOperations.ZeroMemory(_seed);

        return new EntropyKeyfileGenerationResult
        {
            KeyMaterial = output,
            CollectedEntropyBits = _collectedEntropyBits,
            SampleCount = _sampleCount
        };
    }

    public void Dispose()
    {
        _hash.Dispose();
        CryptographicOperations.ZeroMemory(_seed);
    }

    private void ThrowIfFinalized()
    {
        if (_finalized)
            throw new InvalidOperationException("The entropy collector has already been finalized.");
    }

    private static int EstimateSampleEntropy(double deltaX, double deltaY, long elapsedTicks, bool leftPressed, bool rightPressed)
    {
        var distance = Math.Abs(deltaX) + Math.Abs(deltaY);
        var entropy = 1;

        if (distance > 0.05d) entropy++;
        if (distance > 0.5d) entropy++;
        if (distance > 2d) entropy++;
        if (elapsedTicks > 0) entropy++;
        if ((leftPressed ? 1 : 0) != (rightPressed ? 1 : 0)) entropy++;

        return Math.Min(6, entropy);
    }

    private byte[] BuildInfoBlock()
    {
        Span<byte> info = stackalloc byte[24];
        BinaryPrimitives.WriteInt64LittleEndian(info[0..8], _sampleCount);
        BinaryPrimitives.WriteInt64LittleEndian(info[8..16], _collectedEntropyBits);
        BinaryPrimitives.WriteInt64LittleEndian(info[16..24], _stopwatch.ElapsedMilliseconds);
        return info.ToArray();
    }

    private static byte[] HkdfSha512(byte[] ikm, byte[] salt, byte[] info, int outputLength)
    {
        using var hmac = new HMACSHA512(salt);
        var prk = hmac.ComputeHash(ikm);

        var output = new byte[outputLength];
        var previous = Array.Empty<byte>();
        var offset = 0;
        byte counter = 1;

        while (offset < outputLength)
        {
            using var expandHmac = new HMACSHA512(prk);
            expandHmac.TransformBlock(previous, 0, previous.Length, null, 0);
            expandHmac.TransformBlock(info, 0, info.Length, null, 0);
            expandHmac.TransformFinalBlock(new[] { counter }, 0, 1);
            previous = expandHmac.Hash ?? throw new CryptographicException("Failed to expand HKDF output.");

            var bytesToCopy = Math.Min(previous.Length, outputLength - offset);
            Buffer.BlockCopy(previous, 0, output, offset, bytesToCopy);
            offset += bytesToCopy;
            counter++;
        }

        CryptographicOperations.ZeroMemory(prk);
        if (previous.Length > 0)
            CryptographicOperations.ZeroMemory(previous);

        return output;
    }
}
