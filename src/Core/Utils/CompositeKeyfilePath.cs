using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;

namespace PhantomVault.Core.Utils
{
    public static class CompositeKeyfilePath
    {
        public const char Delimiter = ';';

        public static string? Compose(params string?[] paths)
        {
            var parts = paths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return parts.Length == 0
                ? null
                : string.Join(Delimiter, parts);
        }

        public static IReadOnlyList<string> Split(string? compositePath)
        {
            if (string.IsNullOrWhiteSpace(compositePath))
                return Array.Empty<string>();

            return compositePath
                .Split(Delimiter, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static string? GetPrimaryPath(string? compositePath)
            => Split(compositePath).FirstOrDefault();

        public static bool Exists(string? compositePath)
            => Split(compositePath).All(File.Exists);

        public static byte[] ReadCombinedBytes(string? compositePath, bool required)
        {
            var paths = Split(compositePath);
            if (paths.Count == 0)
            {
                if (required)
                    throw new SecurityException("Keyfile material is required but no keyfile path was provided.");

                return Array.Empty<byte>();
            }

            using var buffer = new MemoryStream();
            foreach (var path in paths)
            {
                if (!File.Exists(path))
                    throw new SecurityException($"Keyfile path '{path}' could not be resolved.");

                byte[] keyfileBytes = File.ReadAllBytes(path);
                try
                {
                    buffer.Write(keyfileBytes, 0, keyfileBytes.Length);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(keyfileBytes);
                }
            }

            return buffer.ToArray();
        }

        public static async System.Threading.Tasks.Task<byte[]> ReadCombinedBytesAsync(string? compositePath, bool required)
        {
            var paths = Split(compositePath);
            if (paths.Count == 0)
            {
                if (required)
                    throw new SecurityException("Keyfile material is required but no keyfile path was provided.");

                return Array.Empty<byte>();
            }

            using var buffer = new MemoryStream();
            foreach (var path in paths)
            {
                if (!File.Exists(path))
                    throw new SecurityException($"Keyfile path '{path}' could not be resolved.");

                byte[] keyfileBytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
                try
                {
                    await buffer.WriteAsync(keyfileBytes, 0, keyfileBytes.Length).ConfigureAwait(false);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(keyfileBytes);
                }
            }

            return buffer.ToArray();
        }
    }
}
