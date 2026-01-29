using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using PhantomVault.Core.Models;
using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Serialization;
using KeePassLib.Security;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Service for importing credentials from KeePass 2.x KDBX database files.
    /// Supports KDBX v3 and v4 formats (KeePass 2.x, KeePassXC).
    /// </summary>
    public class KeePassImportService
    {
        /// <summary>
        /// Imports credentials from a KeePass KDBX file.
        /// </summary>
        /// <param name="kdbxPath">Path to the .kdbx file.</param>
        /// <param name="password">Master password for the database.</param>
        /// <param name="keyfilePath">Optional keyfile path.</param>
        /// <param name="progress">Optional progress reporter (0-100).</param>
        /// <returns>Result containing success status, list of imported credentials, and message.</returns>
        public async Task<KeePassImportResult> ImportAsync(
            string kdbxPath,
            string password,
            string? keyfilePath = null,
            IProgress<int>? progress = null)
        {
            try
            {
                progress?.Report(0);

                // Validate inputs
                if (!File.Exists(kdbxPath))
                    return KeePassImportResult.Failure("KeePass database file not found.");

                if (string.IsNullOrWhiteSpace(password))
                    return KeePassImportResult.Failure("Password cannot be empty.");

                progress?.Report(10);

                // Read KDBX file
                byte[] kdbxData;
                try
                {
                    kdbxData = await File.ReadAllBytesAsync(kdbxPath);
                }
                catch (Exception ex)
                {
                    return KeePassImportResult.Failure($"Failed to read file: {ex.Message}");
                }

                progress?.Report(20);

                // Parse KDBX header and determine version
                var parseResult = await Task.Run(() => ParseKdbx(kdbxData, password, keyfilePath, progress));

                progress?.Report(100);

                return parseResult;
            }
            catch (Exception ex)
            {
                return KeePassImportResult.Failure($"Import failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Validates a KeePass database without importing (checks password).
        /// </summary>
        public async Task<(bool IsValid, string Message)> ValidateAsync(
            string kdbxPath,
            string password,
            string? keyfilePath = null)
        {
            try
            {
                if (!File.Exists(kdbxPath))
                    return (false, "File not found.");

                var result = await ImportAsync(kdbxPath, password, keyfilePath);
                return (result.IsSuccess, result.Message);
            }
            catch (Exception ex)
            {
                return (false, $"Validation failed: {ex.Message}");
            }
        }

        #region Private KDBX Parsing Methods

        private KeePassImportResult ParseKdbx(
            byte[] kdbxData,
            string password,
            string? keyfilePath,
            IProgress<int>? progress)
        {
            try
            {
                progress?.Report(30);

                // Create composite key (password + optional keyfile)
                var compositeKey = new CompositeKey();
                compositeKey.AddUserKey(new KcpPassword(password));

                if (!string.IsNullOrEmpty(keyfilePath) && File.Exists(keyfilePath))
                {
                    compositeKey.AddUserKey(new KcpKeyFile(keyfilePath));
                }

                progress?.Report(40);

                // Create PwDatabase instance
                var database = new PwDatabase();

                // Create temporary file to load from (KeePassLib requires file path)
                string tempPath = Path.GetTempFileName();
                try
                {
                    File.WriteAllBytes(tempPath, kdbxData);

                    progress?.Report(50);

                    // Create IOConnectionInfo
                    var ioConnInfo = new IOConnectionInfo { Path = tempPath };

                    // Open database
                    database.Open(ioConnInfo, compositeKey, null);

                    progress?.Report(70);

                    // Extract credentials
                    var credentials = new List<Credential>();
                    if (database.RootGroup != null)
                    {
                        ExtractEntries(database.RootGroup, credentials, "");
                    }

                    progress?.Report(90);

                    // Close database
                    database.Close();

                    return KeePassImportResult.Success(
                        credentials,
                        $"Successfully imported {credentials.Count} credentials from KeePass database.");
                }
                finally
                {
                    // Clean up temporary file
                    try
                    {
                        if (File.Exists(tempPath))
                            File.Delete(tempPath);
                    }
                    catch
                    {
                        // Best effort cleanup
                    }
                }
            }
            catch (KeePassLib.Keys.InvalidCompositeKeyException)
            {
                return KeePassImportResult.Failure("Invalid password or keyfile. Please check your credentials and try again.");
            }
            catch (Exception ex)
            {
                return KeePassImportResult.Failure($"KDBX parsing error: {ex.Message}");
            }
        }

        /// <summary>
        /// Recursively extracts entries from KeePass groups.
        /// </summary>
        private void ExtractEntries(PwGroup group, List<Credential> credentials, string groupPath)
        {
            // Build current group path
            string currentPath = string.IsNullOrEmpty(groupPath)
                ? group.Name
                : $"{groupPath}/{group.Name}";

            // Extract entries from this group
            foreach (var entry in group.Entries)
            {
                try
                {
                    string title = GetProtectedString(entry, PwDefs.TitleField);

                    // Skip entries without titles (likely meta entries)
                    if (string.IsNullOrWhiteSpace(title))
                        continue;

                    var credential = new Credential
                    {
                        Title = title,
                        Username = GetProtectedString(entry, PwDefs.UserNameField),
                        Password = GetProtectedString(entry, PwDefs.PasswordField),
                        Url = GetProtectedString(entry, PwDefs.UrlField),
                        Notes = GetProtectedString(entry, PwDefs.NotesField),
                        Group = currentPath,
                        CreatedUtc = entry.CreationTime,
                        LastUpdatedUtc = entry.LastModificationTime,
                        ExpiryUtc = entry.Expires ? (DateTimeOffset?)entry.ExpiryTime : null,
                        CustomFields = new Dictionary<string, string>(),
                        Tags = new List<string>()
                    };

                    // Extract custom fields
                    foreach (var kvp in entry.Strings)
                    {
                        // Skip standard fields
                        if (kvp.Key == PwDefs.TitleField ||
                            kvp.Key == PwDefs.UserNameField ||
                            kvp.Key == PwDefs.PasswordField ||
                            kvp.Key == PwDefs.UrlField ||
                            kvp.Key == PwDefs.NotesField)
                        {
                            continue;
                        }

                        credential.CustomFields[kvp.Key] = kvp.Value.ReadString();
                    }

                    // Extract tags
                    if (entry.Tags != null && entry.Tags.Count > 0)
                    {
                        credential.Tags = entry.Tags.ToList();
                    }

                    credentials.Add(credential);
                }
                catch (Exception ex)
                {
                    // Log and continue with other entries
                    System.Diagnostics.Debug.WriteLine($"Failed to extract entry: {ex.Message}");
                }
            }

            // Recursively process subgroups
            foreach (var subgroup in group.Groups)
            {
                ExtractEntries(subgroup, credentials, currentPath);
            }
        }

        /// <summary>
        /// Safely reads a protected string from a KeePass entry.
        /// </summary>
        private string GetProtectedString(PwEntry entry, string key)
        {
            if (entry.Strings.Exists(key))
            {
                return entry.Strings.Get(key).ReadString();
            }
            return string.Empty;
        }

        #endregion
    }

    /// <summary>
    /// Result of a KeePass import operation.
    /// </summary>
    public class KeePassImportResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<Credential> Credentials { get; set; } = new();
        public int TotalEntries => Credentials.Count;
        public int TotalGroups => Credentials.Select(c => c.Group).Distinct().Count();

        public static KeePassImportResult Success(List<Credential> credentials, string message = "")
        {
            return new KeePassImportResult
            {
                IsSuccess = true,
                Message = string.IsNullOrEmpty(message)
                    ? $"Successfully imported {credentials.Count} credentials."
                    : message,
                Credentials = credentials
            };
        }

        public static KeePassImportResult Failure(string message)
        {
            return new KeePassImportResult
            {
                IsSuccess = false,
                Message = message,
                Credentials = new List<Credential>()
            };
        }
    }
}
