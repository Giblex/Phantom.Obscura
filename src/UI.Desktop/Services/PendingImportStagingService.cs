using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PhantomVault.Core.Models;

namespace PhantomVault.UI.Services
{
    internal static class PendingImportStagingService
    {
        private static readonly string StagingDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhantomObscura",
            "staging");

        private static readonly string StagingFilePath = Path.Combine(StagingDirectory, "pending-imports.json");

        public static void SavePendingImports(IReadOnlyCollection<Credential> credentials)
        {
            Directory.CreateDirectory(StagingDirectory);
            File.WriteAllText(
                StagingFilePath,
                JsonSerializer.Serialize(credentials, new JsonSerializerOptions { WriteIndented = true }));
        }

        public static List<Credential> LoadPendingImports()
        {
            if (!File.Exists(StagingFilePath))
                return new List<Credential>();

            var json = File.ReadAllText(StagingFilePath);
            return JsonSerializer.Deserialize<List<Credential>>(json) ?? new List<Credential>();
        }

        public static bool HasPendingImports() => File.Exists(StagingFilePath);

        public static void Clear()
        {
            if (File.Exists(StagingFilePath))
                File.Delete(StagingFilePath);
        }
    }
}
