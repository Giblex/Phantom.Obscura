using System;
using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services;

public class SecureDeletionService
{
    private const int ChunkSize = 64 * 1024;

    public enum DeletionMethod
    {

        DoD522022M,

        Gutmann,

        EnhancedOverwrite,

        StandardSecure,

        SimpleOverwrite
    }

    // Multi-pass overwrite cannot guarantee destruction on SSDs (TRIM/wear-leveling), copy-on-write filesystems, or any block-remapping storage. Treat as obfuscation, not erasure.
    public static async Task BestEffortDeleteAsync(string filePath, DeletionMethod method, IProgress<int>? progress = null)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found for secure deletion", filePath);

        var fileInfo = new FileInfo(filePath);
        if (fileInfo.IsReadOnly || (fileInfo.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
            fileInfo.Attributes = FileAttributes.Normal;

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

        File.Delete(filePath);
    }

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

    private static byte GetFillByteForPass(int pass, DeletionMethod method)
    {
        return method switch
        {
            DeletionMethod.DoD522022M => pass switch
            {
                0 => 0x00,
                1 => 0xFF,
                _ => 0x00
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

