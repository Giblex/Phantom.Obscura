using System;
using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services;

/// <summary>
/// Provides multiple secure file deletion methods with cryptographic overwriting.
/// Uses chunked streaming to handle files of any size without exhausting memory.
/// </summary>
public class SecureDeletionService
{
    private const int ChunkSize = 64 * 1024; // 64 KB buffer

    /// <summary>
    /// Secure deletion method types
    /// </summary>
    public enum DeletionMethod
    {
        /// <summary>DoD 5220.22-M standard (7 passes)</summary>
        DoD522022M,
        /// <summary>Gutmann method (35 passes)</summary>
        Gutmann,
        /// <summary>Enhanced overwrite (7 passes with random data)</summary>
        EnhancedOverwrite,
        /// <summary>Standard secure erasure (3 passes)</summary>
        StandardSecure,
        /// <summary>Simple single pass overwrite</summary>
        SimpleOverwrite
    }

    /// <summary>
    /// Securely deletes a file using the specified method.
    /// </summary>
    /// <param name="filePath">Path to file to delete</param>
    /// <param name="method">Deletion method to use</param>
    /// <param name="progress">Optional progress callback (0-100)</param>
    public static async Task SecureDeleteFileAsync(string filePath, DeletionMethod method, IProgress<int>? progress = null)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found for secure deletion", filePath);

        var fileInfo = new FileInfo(filePath);
        long fileSize = fileInfo.Length;

        int passes = method switch
        {
            DeletionMethod.DoD522022M => 7,
            DeletionMethod.Gutmann => 35,
            DeletionMethod.EnhancedOverwrite => 7,
            DeletionMethod.StandardSecure => 3,
            DeletionMethod.SimpleOverwrite => 1,
            _ => 3
        };

        await OverwriteFileAsync(filePath, fileSize, passes, method, progress);

        // Final deletion
        File.Delete(filePath);
    }

    /// <summary>
    /// Overwrites a file with the specified number of passes using chunked streaming.
    /// </summary>
    private static async Task OverwriteFileAsync(string filePath, long fileSize, int passes, DeletionMethod method, IProgress<int>? progress)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(ChunkSize);
        try
        {
            for (int pass = 0; pass < passes; pass++)
            {
                byte fillByte = GetFillByteForPass(pass, method);
                bool useRandom = ShouldUseRandomForPass(pass, method);

                await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None, ChunkSize, FileOptions.WriteThrough);
                long remaining = fileSize;

                while (remaining > 0)
                {
                    int toWrite = (int)Math.Min(ChunkSize, remaining);

                    if (useRandom)
                    {
                        RandomNumberGenerator.Fill(buffer.AsSpan(0, toWrite));
                    }
                    else
                    {
                        Array.Fill(buffer, fillByte, 0, toWrite);
                    }

                    await fs.WriteAsync(buffer.AsMemory(0, toWrite));
                    remaining -= toWrite;
                }

                await fs.FlushAsync();

                int progressPercent = (int)(((pass + 1) / (double)passes) * 100);
                progress?.Report(progressPercent);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer.AsSpan(0, ChunkSize));
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Determines the fill byte for a given pass based on the deletion method.
    /// </summary>
    private static byte GetFillByteForPass(int pass, DeletionMethod method)
    {
        return method switch
        {
            DeletionMethod.DoD522022M => pass switch
            {
                0 => 0x00,
                1 => 0xFF,
                _ => 0x00 // Random passes handled separately
            },
            DeletionMethod.StandardSecure => pass switch
            {
                0 => 0x00,
                1 => 0xFF,
                _ => 0x00
            },
            _ => 0x00
        };
    }

    /// <summary>
    /// Determines if a pass should use random data instead of a fixed pattern.
    /// </summary>
    private static bool ShouldUseRandomForPass(int pass, DeletionMethod method)
    {
        return method switch
        {
            DeletionMethod.DoD522022M => pass >= 2,
            DeletionMethod.Gutmann => true,
            DeletionMethod.EnhancedOverwrite => true,
            DeletionMethod.StandardSecure => pass >= 2,
            DeletionMethod.SimpleOverwrite => true,
            _ => true
        };
    }
}
