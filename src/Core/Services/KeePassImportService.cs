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

    public class KeePassImportService
    {

        public async Task<KeePassImportResult> ImportAsync(
            string kdbxPath,
            string password,
            string? keyfilePath = null,
            IProgress<int>? progress = null)
        {
            try
            {
                progress?.Report(0);

                if (!File.Exists(kdbxPath))
                    return KeePassImportResult.Failure("KeePass database file not found.");

                if (string.IsNullOrWhiteSpace(password))
                    return KeePassImportResult.Failure("Password cannot be empty.");

                progress?.Report(10);

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

                var parseResult = await Task.Run(() => ParseKdbx(kdbxData, password, keyfilePath, progress));

                progress?.Report(100);

                return parseResult;
            }
            catch (Exception ex)
            {
                return KeePassImportResult.Failure($"Import failed: {ex.Message}");
            }
        }

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

                var compositeKey = new CompositeKey();
                compositeKey.AddUserKey(new KcpPassword(password));

                if (!string.IsNullOrEmpty(keyfilePath) && File.Exists(keyfilePath))
                {
                    compositeKey.AddUserKey(new KcpKeyFile(keyfilePath));
                }

                progress?.Report(40);

                var database = new PwDatabase();

                string tempPath = Path.GetTempFileName();
                try
                {
                    File.WriteAllBytes(tempPath, kdbxData);

                    progress?.Report(50);

                    var ioConnInfo = new IOConnectionInfo { Path = tempPath };

                    database.Open(ioConnInfo, compositeKey, null);

                    progress?.Report(70);

                    var credentials = new List<Credential>();
                    if (database.RootGroup != null)
                    {
                        ExtractEntries(database.RootGroup, credentials, "");
                    }

                    progress?.Report(90);

                    database.Close();

                    return KeePassImportResult.Success(
                        credentials,
                        $"Successfully imported {credentials.Count} credentials from KeePass database.");
                }
                finally
                {

                    try
                    {
                        if (File.Exists(tempPath))
                            File.Delete(tempPath);
                    }
                    catch
                    {

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

        private void ExtractEntries(PwGroup group, List<Credential> credentials, string groupPath)
        {

            string currentPath = string.IsNullOrEmpty(groupPath)
                ? group.Name
                : $"{groupPath}/{group.Name}";

            foreach (var entry in group.Entries)
            {
                try
                {
                    string title = GetProtectedString(entry, PwDefs.TitleField);

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

                    foreach (var kvp in entry.Strings)
                    {

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

                    if (entry.Tags != null && entry.Tags.Count > 0)
                    {
                        credential.Tags = entry.Tags.ToList();
                    }

                    credentials.Add(credential);
                }
                catch (Exception ex)
                {

                    System.Diagnostics.Debug.WriteLine($"Failed to extract entry: {ex.Message}");
                }
            }

            foreach (var subgroup in group.Groups)
            {
                ExtractEntries(subgroup, credentials, currentPath);
            }
        }

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

