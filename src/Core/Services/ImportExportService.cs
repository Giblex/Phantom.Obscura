using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Represents the result of an import operation.
    /// </summary>
    public sealed class ImportResult
    {
        public List<Credential> SuccessfulCredentials { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public List<DuplicateInfo> Duplicates { get; set; } = new();
        public int TotalProcessed { get; set; }
        public int SuccessCount => SuccessfulCredentials.Count;
        public int ErrorCount => Errors.Count;
        public int WarningCount => Warnings.Count;
        public int DuplicateCount => Duplicates.Count;
        public int WeakPasswordCount { get; set; }
        public int InvalidUrlCount { get; set; }
        public int ExpiredCount { get; set; }
    }

    /// <summary>
    /// Represents password strength levels.
    /// </summary>
    public enum PasswordStrength
    {
        VeryWeak,
        Weak,
        Medium,
        Strong,
        VeryStrong
    }

    /// <summary>
    /// Represents a validation warning for a credential.
    /// </summary>
    public sealed class ValidationWarning
    {
        public string CredentialTitle { get; set; } = "";
        public string WarningType { get; set; } = "";
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// Represents a duplicate credential detection result.
    /// </summary>
    public sealed class DuplicateInfo
    {
        public Credential NewCredential { get; set; } = new();
        public Credential? ExistingCredential { get; set; }
        public DuplicateMatchType MatchType { get; set; }
        public bool KeepNew { get; set; } = true;
    }

    public enum DuplicateMatchType
    {
        ExactMatch,      // Title, Username, and URL match
        PasswordMatch,   // Same password used
        UsernameUrlMatch // Same username and URL but different password
    }

    /// <summary>
    /// Represents import progress information.
    /// </summary>
    public sealed class ImportProgress
    {
        public int TotalItems { get; set; }
        public int ProcessedItems { get; set; }
        public int SuccessCount { get; set; }
        public int ErrorCount { get; set; }
        public string CurrentItem { get; set; } = "";
        public int PercentComplete => TotalItems > 0 ? (ProcessedItems * 100) / TotalItems : 0;
    }

    /// <summary>
    /// Provides import and export functionality for credentials in various formats.
    /// Supports CSV, KeePass XML 2.x, JSON, 1Password, Bitwarden, Proton Pass, KeeWeb, and KeePassXC formats.
    /// </summary>
    public sealed class ImportExportService
    {
        /// <summary>
        /// Detects the character encoding of a file by analyzing BOM and byte patterns.
        /// </summary>
        private Encoding DetectFileEncoding(string filePath)
        {
            try
            {
                var buffer = new byte[4];
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    fs.Read(buffer, 0, 4);
                }

                // Check for BOM (Byte Order Mark)
                if (buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
                    return Encoding.UTF8; // UTF-8 BOM

                if (buffer[0] == 0xFF && buffer[1] == 0xFE && buffer[2] == 0x00 && buffer[3] == 0x00)
                    return Encoding.UTF32; // UTF-32 LE

                if (buffer[0] == 0xFF && buffer[1] == 0xFE)
                    return Encoding.Unicode; // UTF-16 LE

                if (buffer[0] == 0xFE && buffer[1] == 0xFF)
                    return Encoding.BigEndianUnicode; // UTF-16 BE

                if (buffer[0] == 0x00 && buffer[1] == 0x00 && buffer[2] == 0xFE && buffer[3] == 0xFF)
                    return new UTF32Encoding(true, true); // UTF-32 BE

                // No BOM - try to detect from content
                var content = File.ReadAllBytes(filePath);

                // Check for UTF-8 without BOM
                if (IsValidUtf8(content))
                    return new UTF8Encoding(false);

                // Default to Latin-1 (ISO-8859-1) for compatibility
                return Encoding.GetEncoding("ISO-8859-1");
            }
            catch
            {
                return Encoding.UTF8; // Fallback to UTF-8
            }
        }

        /// <summary>
        /// Validates if byte array is valid UTF-8.
        /// </summary>
        private bool IsValidUtf8(byte[] bytes)
        {
            try
            {
                var decoder = Encoding.UTF8.GetDecoder();
                var chars = new char[bytes.Length];
                decoder.GetChars(bytes, 0, bytes.Length, chars, 0, true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Validates URL format.
        /// </summary>
        private bool IsValidUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return true; // Empty URL is acceptable

            // Allow URLs without protocol
            var urlToCheck = url;
            if (!url.Contains("://"))
            {
                urlToCheck = "https://" + url;
            }

            return Uri.TryCreate(urlToCheck, UriKind.Absolute, out var uriResult)
                && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        }

        /// <summary>
        /// Calculates password strength based on length, complexity, and common patterns.
        /// </summary>
        private PasswordStrength CalculatePasswordStrength(string? password)
        {
            if (string.IsNullOrEmpty(password)) return PasswordStrength.VeryWeak;

            var score = 0;

            // Length score
            if (password.Length >= 8) score++;
            if (password.Length >= 12) score++;
            if (password.Length >= 16) score++;

            // Complexity score
            if (password.Any(char.IsLower)) score++;
            if (password.Any(char.IsUpper)) score++;
            if (password.Any(char.IsDigit)) score++;
            if (password.Any(c => !char.IsLetterOrDigit(c))) score++; // Special chars

            // Common patterns (reduce score)
            var commonPasswords = new[] { "password", "123456", "qwerty", "abc123", "letmein", "welcome", "admin" };
            if (commonPasswords.Any(p => password.ToLower().Contains(p))) score -= 3;

            // Sequential patterns
            if (HasSequentialPattern(password)) score--;

            return score switch
            {
                <= 1 => PasswordStrength.VeryWeak,
                2 => PasswordStrength.Weak,
                3 or 4 => PasswordStrength.Medium,
                5 or 6 => PasswordStrength.Strong,
                _ => PasswordStrength.VeryStrong
            };
        }

        /// <summary>
        /// Checks for sequential patterns in password.
        /// </summary>
        private bool HasSequentialPattern(string password)
        {
            var sequences = new[] { "012", "123", "234", "345", "456", "567", "678", "789", "abc", "bcd", "cde" };
            return sequences.Any(seq => password.ToLower().Contains(seq));
        }

        /// <summary>
        /// Validates credential and returns warnings.
        /// </summary>
        private List<string> ValidateCredential(Credential credential)
        {
            var warnings = new List<string>();
            var title = credential.Title ?? "Untitled";

            // URL validation
            if (!IsValidUrl(credential.Url))
            {
                warnings.Add($"{title}: Invalid URL format '{credential.Url}'");
            }

            // Password strength
            var strength = CalculatePasswordStrength(credential.Password);
            if (strength == PasswordStrength.VeryWeak || strength == PasswordStrength.Weak)
            {
                warnings.Add($"{title}: Weak password detected (strength: {strength})");
            }

            // Expiry date
            if (credential.ExpiryUtc.HasValue)
            {
                var daysUntilExpiry = (credential.ExpiryUtc.Value - DateTimeOffset.UtcNow).Days;

                if (daysUntilExpiry < 0)
                {
                    warnings.Add($"{title}: Credential expired {Math.Abs(daysUntilExpiry)} days ago");
                }
                else if (daysUntilExpiry <= 30)
                {
                    warnings.Add($"{title}: Credential expires in {daysUntilExpiry} days");
                }
            }

            return warnings;
        }

        /// <summary>
        /// Attempts to automatically detect the format of an import file.
        /// Returns the detected format name or null if unable to determine.
        /// </summary>
        public async Task<string?> DetectFormatAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return null;
            if (!File.Exists(filePath)) return null;

            try
            {
                // Read first few lines for analysis
                var lines = await File.ReadAllLinesAsync(filePath, Encoding.UTF8);
                if (lines.Length == 0) return null;

                var firstLine = lines[0].Trim();

                // Check for XML (KeePass)
                if (firstLine.StartsWith("<?xml") || firstLine.StartsWith("<KeePassFile"))
                {
                    var content = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
                    if (content.Contains("<KeePassFile") && content.Contains("<Root>"))
                        return "KeePass XML";
                }

                // Check for JSON formats
                if (firstLine.StartsWith("{") || firstLine.StartsWith("["))
                {
                    var content = await File.ReadAllTextAsync(filePath, Encoding.UTF8);

                    // Bitwarden JSON has "encrypted" and "folders" properties
                    if (content.Contains("\"encrypted\"") && content.Contains("\"folders\""))
                        return "Bitwarden JSON";

                    // Proton Pass JSON has "vaults" array
                    if (content.Contains("\"vaults\"") && content.Contains("\"items\""))
                        return "Proton Pass JSON";

                    // Generic JSON as fallback
                    if (content.Contains("\"credentials\"") || content.Contains("\"password\""))
                        return "JSON";
                }

                // Check CSV formats by header
                if (lines.Length > 0)
                {
                    var header = firstLine.ToLower();

                    // 1Password CSV: title,username,password,url,notes,type,name,folder
                    if (header.Contains("title") && header.Contains("username") &&
                        header.Contains("password") && header.Contains("folder") &&
                        header.Contains("type"))
                        return "1Password CSV";

                    // Bitwarden CSV: folder,favorite,type,name,notes,fields,reprompt,login_uri,login_username,login_password,login_totp
                    if (header.Contains("login_uri") && header.Contains("login_username") &&
                        header.Contains("login_password") && header.Contains("folder"))
                        return "Bitwarden CSV";

                    // LastPass CSV: url,username,password,extra,name,grouping,fav
                    if (header.Contains("grouping") && header.Contains("extra") &&
                        header.Contains("url") && header.Contains("username") && header.Contains("password"))
                        return "LastPass CSV";

                    // Chrome/Edge CSV: name,url,username,password
                    if (header.Contains("name") && header.Contains("url") &&
                        header.Contains("username") && header.Contains("password") &&
                        !header.Contains("notes") && !header.Contains("title"))
                        return "Chrome CSV";

                    // Firefox CSV: url,username,password,httpRealm,formActionOrigin,guid,timeCreated,timeLastUsed,timePasswordChanged
                    if (header.Contains("httprealm") || header.Contains("formactionorigin") ||
                        header.Contains("timecreated"))
                        return "Firefox CSV";

                    // Generic CSV - our custom format
                    if (header.Contains("title") && header.Contains("username") &&
                        header.Contains("password") && header.Contains("notes") &&
                        header.Contains("group"))
                        return "CSV";
                }

                return null; // Unable to detect
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Exports credentials to CSV format.
        /// </summary>
        public async Task ExportToCsvAsync(List<Credential> credentials, string filePath)
        {
            if (credentials == null) throw new ArgumentNullException(nameof(credentials));
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path cannot be empty", nameof(filePath));

            var sb = new StringBuilder();
            // CSV Header
            sb.AppendLine("Title,Username,Password,URL,Notes,Group,Icon,Tags,Created,LastUpdated,Expiry");

            foreach (var cred in credentials)
            {
                sb.AppendLine(string.Join(",",
                    EscapeCsv(cred.Title),
                    EscapeCsv(cred.Username),
                    EscapeCsv(cred.Password),
                    EscapeCsv(cred.Url),
                    EscapeCsv(cred.Notes),
                    EscapeCsv(cred.Group),
                    EscapeCsv(cred.Icon),
                    EscapeCsv(string.Join(";", cred.Tags)),
                    cred.CreatedUtc.ToString("o"),
                    cred.LastUpdatedUtc.ToString("o"),
                    cred.ExpiryUtc?.ToString("o") ?? ""
                ));
            }

            await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>
        /// Imports credentials from CSV format.
        /// Expected columns: Title,Username,Password,URL,Notes,Group,Icon,Tags,Created,LastUpdated,Expiry
        /// </summary>
        public async Task<List<Credential>> ImportFromCsvAsync(string filePath, IProgress<ImportProgress>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path cannot be empty", nameof(filePath));
            if (!File.Exists(filePath)) throw new FileNotFoundException("CSV file not found", filePath);

            var credentials = new List<Credential>();

            // Auto-detect encoding
            var encoding = DetectFileEncoding(filePath);
            var lines = await File.ReadAllLinesAsync(filePath, encoding);

            if (lines.Length < 2) return credentials; // No data rows

            int totalLines = lines.Length - 1;
            int processed = 0;

            // Skip header row
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                try
                {
                    var parts = ParseCsvLine(line);
                    if (parts.Length < 6) continue; // Minimum required fields

                    var cred = new Credential
                    {
                        Title = parts.Length > 0 ? parts[0] : "",
                        Username = parts.Length > 1 ? parts[1] : "",
                        Password = parts.Length > 2 ? parts[2] : "",
                        Url = parts.Length > 3 ? parts[3] : "",
                        Notes = parts.Length > 4 ? parts[4] : "",
                        Group = parts.Length > 5 ? parts[5] : "",
                        Icon = parts.Length > 6 ? parts[6] : "🔑",
                        Tags = parts.Length > 7 && !string.IsNullOrEmpty(parts[7])
                            ? parts[7].Split(';', StringSplitOptions.RemoveEmptyEntries).ToList()
                            : new List<string>()
                    };

                    // Parse dates
                    if (parts.Length > 8 && DateTimeOffset.TryParse(parts[8], out var created))
                        cred.CreatedUtc = created;

                    if (parts.Length > 9 && DateTimeOffset.TryParse(parts[9], out var updated))
                        cred.LastUpdatedUtc = updated;

                    if (parts.Length > 10 && !string.IsNullOrEmpty(parts[10]) && DateTimeOffset.TryParse(parts[10], out var expiry))
                        cred.ExpiryUtc = expiry;

                    credentials.Add(cred);

                    processed++;
                    progress?.Report(new ImportProgress
                    {
                        TotalItems = totalLines,
                        ProcessedItems = processed,
                        SuccessCount = credentials.Count,
                        CurrentItem = cred.Title ?? cred.Username ?? "Unknown"
                    });
                }
                catch
                {
                    processed++;
                    continue;
                }
            }

            return credentials;
        }

        /// <summary>
        /// Imports large CSV files using streaming to avoid memory issues.
        /// Processes files with 1000+ credentials in chunks.
        /// </summary>
        public async Task<List<Credential>> ImportFromCsvStreamingAsync(
            string filePath,
            IProgress<ImportProgress>? progress = null,
            int chunkSize = 500)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path cannot be empty", nameof(filePath));
            if (!File.Exists(filePath)) throw new FileNotFoundException("CSV file not found", filePath);

            var credentials = new List<Credential>();
            var encoding = DetectFileEncoding(filePath);

            // Count total lines for progress
            int totalLines = 0;
            using (var reader = new StreamReader(filePath, encoding))
            {
                while (await reader.ReadLineAsync() != null)
                    totalLines++;
            }
            totalLines--; // Subtract header line

            int processed = 0;
            int lineNumber = 0;

            using (var reader = new StreamReader(filePath, encoding))
            {
                // Skip header
                await reader.ReadLineAsync();
                lineNumber++;

                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    lineNumber++;
                    var trimmedLine = line.Trim();
                    if (string.IsNullOrEmpty(trimmedLine)) continue;

                    try
                    {
                        var parts = ParseCsvLine(trimmedLine);
                        if (parts.Length < 6) continue;

                        var cred = new Credential
                        {
                            Title = parts.Length > 0 ? parts[0] : "",
                            Username = parts.Length > 1 ? parts[1] : "",
                            Password = parts.Length > 2 ? parts[2] : "",
                            Url = parts.Length > 3 ? parts[3] : "",
                            Notes = parts.Length > 4 ? parts[4] : "",
                            Group = parts.Length > 5 ? parts[5] : "",
                            Icon = parts.Length > 6 ? parts[6] : "🔑",
                            Tags = parts.Length > 7 && !string.IsNullOrEmpty(parts[7])
                                ? parts[7].Split(';', StringSplitOptions.RemoveEmptyEntries).ToList()
                                : new List<string>()
                        };

                        // Parse dates
                        if (parts.Length > 8 && DateTimeOffset.TryParse(parts[8], out var created))
                            cred.CreatedUtc = created;

                        if (parts.Length > 9 && DateTimeOffset.TryParse(parts[9], out var updated))
                            cred.LastUpdatedUtc = updated;

                        if (parts.Length > 10 && !string.IsNullOrEmpty(parts[10]) && DateTimeOffset.TryParse(parts[10], out var expiry))
                            cred.ExpiryUtc = expiry;

                        credentials.Add(cred);

                        processed++;

                        // Report progress and consider GC every chunk
                        if (processed % chunkSize == 0)
                        {
                            progress?.Report(new ImportProgress
                            {
                                TotalItems = totalLines,
                                ProcessedItems = processed,
                                SuccessCount = credentials.Count,
                                CurrentItem = cred.Title ?? cred.Username ?? "Unknown"
                            });

                            // Force garbage collection for large imports
                            if (processed % (chunkSize * 4) == 0)
                            {
                                GC.Collect();
                                GC.WaitForPendingFinalizers();
                            }
                        }
                    }
                    catch
                    {
                        processed++;
                        continue;
                    }
                }
            }

            // Final progress update
            progress?.Report(new ImportProgress
            {
                TotalItems = totalLines,
                ProcessedItems = processed,
                SuccessCount = credentials.Count,
                CurrentItem = "Complete"
            });

            return credentials;
        }

        /// <summary>
        /// Exports credentials to KeePass 2.x XML format.
        /// </summary>
        public async Task ExportToKeePassXmlAsync(List<Credential> credentials, string filePath, string databaseName = "PhantomVault Export")
        {
            if (credentials == null) throw new ArgumentNullException(nameof(credentials));
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path cannot be empty", nameof(filePath));

            var root = new XElement("KeePassFile",
                new XElement("Meta",
                    new XElement("DatabaseName", databaseName),
                    new XElement("Generator", "PhantomVault"),
                    new XElement("DatabaseDescription", "Exported from PhantomVault")
                ),
                new XElement("Root",
                    new XElement("Group",
                        new XElement("Name", "Root"),
                        new XElement("Notes", ""),
                        new XElement("IconID", "48"),
                        new XElement("Times",
                            new XElement("CreationTime", DateTime.UtcNow.ToString("o")),
                            new XElement("LastModificationTime", DateTime.UtcNow.ToString("o")),
                            new XElement("LastAccessTime", DateTime.UtcNow.ToString("o")),
                            new XElement("ExpiryTime", DateTime.UtcNow.AddYears(100).ToString("o")),
                            new XElement("Expires", "False"),
                            new XElement("UsageCount", "0"),
                            new XElement("LocationChanged", DateTime.UtcNow.ToString("o"))
                        ),
                        credentials.Select(c => CreateKeePassEntry(c))
                    )
                )
            );

            var doc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
            await using var stream = File.Create(filePath);
            await doc.SaveAsync(stream, SaveOptions.None, default);
        }

        /// <summary>
        /// Imports credentials from KeePass 2.x XML format.
        /// </summary>
        public async Task<List<Credential>> ImportFromKeePassXmlAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path cannot be empty", nameof(filePath));
            if (!File.Exists(filePath)) throw new FileNotFoundException("KeePass XML file not found", filePath);

            var credentials = new List<Credential>();
            var doc = await Task.Run(() => XDocument.Load(filePath));

            var entries = doc.Descendants("Entry");
            foreach (var entry in entries)
            {
                var cred = new Credential();

                // Parse String fields (KeePass stores most data in Key-Value pairs)
                foreach (var stringElement in entry.Elements("String"))
                {
                    var key = stringElement.Element("Key")?.Value ?? "";
                    var value = stringElement.Element("Value")?.Value ?? "";

                    switch (key)
                    {
                        case "Title":
                            cred.Title = value;
                            break;
                        case "UserName":
                            cred.Username = value;
                            break;
                        case "Password":
                            cred.Password = value;
                            break;
                        case "URL":
                            cred.Url = value;
                            break;
                        case "Notes":
                            cred.Notes = value;
                            break;
                    }
                }

                // Parse Times
                var times = entry.Element("Times");
                if (times != null)
                {
                    if (DateTime.TryParse(times.Element("CreationTime")?.Value, out var created))
                        cred.CreatedUtc = new DateTimeOffset(created, TimeSpan.Zero);

                    if (DateTime.TryParse(times.Element("LastModificationTime")?.Value, out var modified))
                        cred.LastUpdatedUtc = new DateTimeOffset(modified, TimeSpan.Zero);

                    var expires = times.Element("Expires")?.Value == "True";
                    if (expires && DateTime.TryParse(times.Element("ExpiryTime")?.Value, out var expiry))
                        cred.ExpiryUtc = new DateTimeOffset(expiry, TimeSpan.Zero);
                }

                // Get group from parent Group element
                var groupName = entry.Parent?.Element("Name")?.Value;
                if (!string.IsNullOrEmpty(groupName) && groupName != "Root")
                    cred.Group = groupName;

                cred.Icon = "🔑"; // Default icon

                if (!string.IsNullOrEmpty(cred.Title) || !string.IsNullOrEmpty(cred.Username))
                    credentials.Add(cred);
            }

            return credentials;
        }

        /// <summary>
        /// Exports credentials to JSON format.
        /// </summary>
        public async Task ExportToJsonAsync(List<Credential> credentials, string filePath)
        {
            if (credentials == null) throw new ArgumentNullException(nameof(credentials));
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path cannot be empty", nameof(filePath));

            var json = System.Text.Json.JsonSerializer.Serialize(credentials, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });

            await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
        }

        /// <summary>
        /// Imports credentials from JSON format.
        /// </summary>
        public async Task<List<Credential>> ImportFromJsonAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path cannot be empty", nameof(filePath));
            if (!File.Exists(filePath)) throw new FileNotFoundException("JSON file not found", filePath);

            var json = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            var credentials = System.Text.Json.JsonSerializer.Deserialize<List<Credential>>(json, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return credentials ?? new List<Credential>();
        }

        private XElement CreateKeePassEntry(Credential cred)
        {
            return new XElement("Entry",
                new XElement("String",
                    new XElement("Key", "Title"),
                    new XElement("Value", cred.Title)
                ),
                new XElement("String",
                    new XElement("Key", "UserName"),
                    new XElement("Value", cred.Username)
                ),
                new XElement("String",
                    new XElement("Key", "Password"),
                    new XElement("Value", cred.Password)
                ),
                new XElement("String",
                    new XElement("Key", "URL"),
                    new XElement("Value", cred.Url)
                ),
                new XElement("String",
                    new XElement("Key", "Notes"),
                    new XElement("Value", cred.Notes)
                ),
                new XElement("Times",
                    new XElement("CreationTime", cred.CreatedUtc.UtcDateTime.ToString("o")),
                    new XElement("LastModificationTime", cred.LastUpdatedUtc.UtcDateTime.ToString("o")),
                    new XElement("LastAccessTime", cred.LastUpdatedUtc.UtcDateTime.ToString("o")),
                    new XElement("ExpiryTime", (cred.ExpiryUtc ?? DateTimeOffset.UtcNow.AddYears(100)).UtcDateTime.ToString("o")),
                    new XElement("Expires", cred.ExpiryUtc.HasValue ? "True" : "False"),
                    new XElement("UsageCount", "0"),
                    new XElement("LocationChanged", cred.CreatedUtc.UtcDateTime.ToString("o"))
                )
            );
        }

        private string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "\"\"";

            if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return "\"" + value + "\"";
        }

        private string[] ParseCsvLine(string line)
        {
            var result = new List<string>();
            var inQuotes = false;
            var current = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++; // Skip next quote
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            result.Add(current.ToString());
            return result.ToArray();
        }

        /// <summary>
        /// Imports credentials from 1Password CSV export format.
        /// </summary>
        public async Task<List<Credential>> ImportFrom1PasswordCsvAsync(string filePath, IProgress<ImportProgress>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path cannot be empty", nameof(filePath));
            if (!File.Exists(filePath)) throw new FileNotFoundException("1Password CSV file not found", filePath);

            var credentials = new List<Credential>();
            var lines = await File.ReadAllLinesAsync(filePath, Encoding.UTF8);

            if (lines.Length < 2) return credentials;

            int totalLines = lines.Length - 1;
            int processed = 0;

            // 1Password CSV format: Title,Username,Password,URL,Notes,Type,Name,Folder
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                try
                {
                    var parts = ParseCsvLine(line);
                    if (parts.Length < 4) continue;

                    var cred = new Credential
                    {
                        Title = parts.Length > 0 ? parts[0] : "",
                        Username = parts.Length > 1 ? parts[1] : "",
                        Password = parts.Length > 2 ? parts[2] : "",
                        Url = parts.Length > 3 ? parts[3] : "",
                        Notes = parts.Length > 4 ? parts[4] : "",
                        Group = parts.Length > 7 ? parts[7] : "", // Folder
                        Icon = "🔑"
                    };

                    credentials.Add(cred);

                    processed++;
                    progress?.Report(new ImportProgress
                    {
                        TotalItems = totalLines,
                        ProcessedItems = processed,
                        SuccessCount = credentials.Count,
                        CurrentItem = cred.Title ?? cred.Username ?? "Unknown"
                    });
                }
                catch
                {
                    processed++;
                    continue;
                }
            }

            return credentials;
        }

        /// <summary>
        /// Imports credentials from Bitwarden JSON export format.
        /// </summary>
        public async Task<List<Credential>> ImportFromBitwardenJsonAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path cannot be empty", nameof(filePath));
            if (!File.Exists(filePath)) throw new FileNotFoundException("Bitwarden JSON file not found", filePath);

            var json = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            var doc = JsonDocument.Parse(json);
            var credentials = new List<Credential>();

            if (doc.RootElement.TryGetProperty("items", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (!item.TryGetProperty("type", out var typeEl) || typeEl.GetInt32() != 1) // 1 = Login
                        continue;

                    var cred = new Credential { Icon = "🔑" };

                    if (item.TryGetProperty("name", out var name))
                        cred.Title = name.GetString() ?? "";

                    if (item.TryGetProperty("login", out var login))
                    {
                        if (login.TryGetProperty("username", out var username))
                            cred.Username = username.GetString() ?? "";

                        if (login.TryGetProperty("password", out var password))
                            cred.Password = password.GetString() ?? "";

                        if (login.TryGetProperty("uris", out var uris) && uris.GetArrayLength() > 0)
                        {
                            var firstUri = uris[0];
                            if (firstUri.TryGetProperty("uri", out var uri))
                                cred.Url = uri.GetString() ?? "";
                        }
                    }

                    if (item.TryGetProperty("notes", out var notes))
                        cred.Notes = notes.GetString() ?? "";

                    if (item.TryGetProperty("folderId", out var folderIdEl) && doc.RootElement.TryGetProperty("folders", out var folders))
                    {
                        var folderId = folderIdEl.GetString();
                        foreach (var folder in folders.EnumerateArray())
                        {
                            if (folder.TryGetProperty("id", out var id) && id.GetString() == folderId)
                            {
                                if (folder.TryGetProperty("name", out var folderName))
                                    cred.Group = folderName.GetString() ?? "";
                                break;
                            }
                        }
                    }

                    credentials.Add(cred);
                }
            }

            return credentials;
        }

        /// <summary>
        /// Imports credentials from Bitwarden CSV export format with header-aware parsing.
        /// </summary>
        public async Task<List<Credential>> ImportFromBitwardenCsvAsync(string filePath, IProgress<ImportProgress>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path cannot be empty", nameof(filePath));
            if (!File.Exists(filePath)) throw new FileNotFoundException("Bitwarden CSV file not found", filePath);

            var credentials = new List<Credential>();
            var lines = await File.ReadAllLinesAsync(filePath, Encoding.UTF8);

            if (lines.Length < 2) return credentials;

            // Parse header to map column names to indices
            var headerParts = ParseCsvLine(lines[0]);
            var columnMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headerParts.Length; i++)
            {
                columnMap[headerParts[i].Trim()] = i;
            }

            // Helper to safely get column value
            string GetColumn(string[] parts, string columnName, string fallback = "")
            {
                if (columnMap.TryGetValue(columnName, out var index) && index < parts.Length)
                    return parts[index];
                return fallback;
            }

            int totalLines = lines.Length - 1;
            int processed = 0;

            // Parse data rows
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                try
                {
                    var parts = ParseCsvLine(line);

                    var cred = new Credential
                    {
                        Group = GetColumn(parts, "folder"),
                        Title = GetColumn(parts, "name"),
                        Notes = GetColumn(parts, "notes"),
                        Url = GetColumn(parts, "login_uri"),
                        Username = GetColumn(parts, "login_username"),
                        Password = GetColumn(parts, "login_password"),
                        Icon = "🔑"
                    };

                    // Only add if has at least title or username
                    if (!string.IsNullOrWhiteSpace(cred.Title) || !string.IsNullOrWhiteSpace(cred.Username))
                    {
                        credentials.Add(cred);
                    }

                    processed++;
                    progress?.Report(new ImportProgress
                    {
                        TotalItems = totalLines,
                        ProcessedItems = processed,
                        SuccessCount = credentials.Count,
                        CurrentItem = cred.Title ?? cred.Username ?? "Unknown"
                    });
                }
                catch
                {
                    // Skip malformed lines
                    processed++;
                    continue;
                }
            }

            return credentials;
        }

        /// <summary>
        /// Imports credentials from Proton Pass JSON export format.
        /// </summary>
        public async Task<List<Credential>> ImportFromProtonPassJsonAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path cannot be empty", nameof(filePath));
            if (!File.Exists(filePath)) throw new FileNotFoundException("Proton Pass JSON file not found", filePath);

            var json = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            var doc = JsonDocument.Parse(json);
            var credentials = new List<Credential>();

            if (doc.RootElement.TryGetProperty("vaults", out var vaults))
            {
                foreach (var vault in vaults.EnumerateObject())
                {
                    if (!vault.Value.TryGetProperty("items", out var items))
                        continue;

                    foreach (var item in items.EnumerateArray())
                    {
                        if (!item.TryGetProperty("data", out var data))
                            continue;

                        if (!data.TryGetProperty("type", out var typeEl) || typeEl.GetString() != "login")
                            continue;

                        var cred = new Credential { Icon = "🔑" };

                        if (data.TryGetProperty("metadata", out var metadata))
                        {
                            if (metadata.TryGetProperty("name", out var name))
                                cred.Title = name.GetString() ?? "";

                            if (metadata.TryGetProperty("note", out var note))
                                cred.Notes = note.GetString() ?? "";
                        }

                        if (data.TryGetProperty("content", out var content))
                        {
                            if (content.TryGetProperty("username", out var username))
                                cred.Username = username.GetString() ?? "";

                            if (content.TryGetProperty("password", out var password))
                                cred.Password = password.GetString() ?? "";

                            if (content.TryGetProperty("urls", out var urls) && urls.GetArrayLength() > 0)
                                cred.Url = urls[0].GetString() ?? "";
                        }

                        credentials.Add(cred);
                    }
                }
            }

            return credentials;
        }

        /// <summary>
        /// Imports credentials from LastPass CSV export format.
        /// </summary>
        public async Task<List<Credential>> ImportFromLastPassCsvAsync(string filePath, IProgress<ImportProgress>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path cannot be empty", nameof(filePath));
            if (!File.Exists(filePath)) throw new FileNotFoundException("LastPass CSV file not found", filePath);

            var credentials = new List<Credential>();
            var lines = await File.ReadAllLinesAsync(filePath, Encoding.UTF8);

            if (lines.Length < 2) return credentials;

            // Parse header
            var headerParts = ParseCsvLine(lines[0]);
            var columnMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headerParts.Length; i++)
            {
                columnMap[headerParts[i].Trim()] = i;
            }

            string GetColumn(string[] parts, string columnName, string fallback = "")
            {
                if (columnMap.TryGetValue(columnName, out var index) && index < parts.Length)
                    return parts[index];
                return fallback;
            }

            int totalLines = lines.Length - 1;
            int processed = 0;

            // LastPass CSV format: url,username,password,extra,name,grouping,fav
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                try
                {
                    var parts = ParseCsvLine(line);

                    var cred = new Credential
                    {
                        Url = GetColumn(parts, "url"),
                        Username = GetColumn(parts, "username"),
                        Password = GetColumn(parts, "password"),
                        Notes = GetColumn(parts, "extra"),
                        Title = GetColumn(parts, "name"),
                        Group = GetColumn(parts, "grouping"),
                        Icon = "🔑"
                    };

                    if (!string.IsNullOrWhiteSpace(cred.Title) || !string.IsNullOrWhiteSpace(cred.Username))
                    {
                        credentials.Add(cred);
                    }

                    processed++;
                    progress?.Report(new ImportProgress
                    {
                        TotalItems = totalLines,
                        ProcessedItems = processed,
                        SuccessCount = credentials.Count,
                        CurrentItem = cred.Title ?? cred.Username ?? "Unknown"
                    });
                }
                catch
                {
                    processed++;
                    continue;
                }
            }

            return credentials;
        }

        /// <summary>
        /// Imports credentials from Chrome/Edge CSV export format.
        /// </summary>
        public async Task<List<Credential>> ImportFromChromeCsvAsync(string filePath, IProgress<ImportProgress>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path cannot be empty", nameof(filePath));
            if (!File.Exists(filePath)) throw new FileNotFoundException("Chrome CSV file not found", filePath);

            var credentials = new List<Credential>();
            var lines = await File.ReadAllLinesAsync(filePath, Encoding.UTF8);

            if (lines.Length < 2) return credentials;

            // Parse header
            var headerParts = ParseCsvLine(lines[0]);
            var columnMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headerParts.Length; i++)
            {
                columnMap[headerParts[i].Trim()] = i;
            }

            string GetColumn(string[] parts, string columnName, string fallback = "")
            {
                if (columnMap.TryGetValue(columnName, out var index) && index < parts.Length)
                    return parts[index];
                return fallback;
            }

            int totalLines = lines.Length - 1;
            int processed = 0;

            // Chrome CSV format: name,url,username,password
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                try
                {
                    var parts = ParseCsvLine(line);

                    var cred = new Credential
                    {
                        Title = GetColumn(parts, "name"),
                        Url = GetColumn(parts, "url"),
                        Username = GetColumn(parts, "username"),
                        Password = GetColumn(parts, "password"),
                        Group = "Browser Passwords",
                        Icon = "🔑"
                    };

                    if (!string.IsNullOrWhiteSpace(cred.Title) || !string.IsNullOrWhiteSpace(cred.Username))
                    {
                        credentials.Add(cred);
                    }

                    processed++;
                    progress?.Report(new ImportProgress
                    {
                        TotalItems = totalLines,
                        ProcessedItems = processed,
                        SuccessCount = credentials.Count,
                        CurrentItem = cred.Title ?? cred.Url ?? "Unknown"
                    });
                }
                catch
                {
                    processed++;
                    continue;
                }
            }

            return credentials;
        }

        /// <summary>
        /// Imports credentials from Firefox CSV export format.
        /// </summary>
        public async Task<List<Credential>> ImportFromFirefoxCsvAsync(string filePath, IProgress<ImportProgress>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path cannot be empty", nameof(filePath));
            if (!File.Exists(filePath)) throw new FileNotFoundException("Firefox CSV file not found", filePath);

            var credentials = new List<Credential>();
            var lines = await File.ReadAllLinesAsync(filePath, Encoding.UTF8);

            if (lines.Length < 2) return credentials;

            // Parse header
            var headerParts = ParseCsvLine(lines[0]);
            var columnMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headerParts.Length; i++)
            {
                columnMap[headerParts[i].Trim()] = i;
            }

            string GetColumn(string[] parts, string columnName, string fallback = "")
            {
                if (columnMap.TryGetValue(columnName, out var index) && index < parts.Length)
                    return parts[index];
                return fallback;
            }

            int totalLines = lines.Length - 1;
            int processed = 0;

            // Firefox CSV format: url,username,password,httpRealm,formActionOrigin,guid,timeCreated,timeLastUsed,timePasswordChanged
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                try
                {
                    var parts = ParseCsvLine(line);

                    var url = GetColumn(parts, "url");
                    var cred = new Credential
                    {
                        Url = url,
                        Username = GetColumn(parts, "username"),
                        Password = GetColumn(parts, "password"),
                        Title = ExtractDomainFromUrl(url),
                        Group = "Browser Passwords",
                        Icon = "🔑"
                    };

                    if (!string.IsNullOrWhiteSpace(cred.Username))
                    {
                        credentials.Add(cred);
                    }

                    processed++;
                    progress?.Report(new ImportProgress
                    {
                        TotalItems = totalLines,
                        ProcessedItems = processed,
                        SuccessCount = credentials.Count,
                        CurrentItem = cred.Title ?? cred.Url ?? "Unknown"
                    });
                }
                catch
                {
                    processed++;
                    continue;
                }
            }

            return credentials;
        }

        /// <summary>
        /// Imports credentials with duplicate detection and error handling.
        /// </summary>
        public async Task<ImportResult> ImportWithDuplicateDetectionAsync(
            string filePath,
            string format,
            List<Credential> existingCredentials,
            IProgress<ImportProgress>? progress = null)
        {
            var result = new ImportResult();

            try
            {
                List<Credential> importedCredentials;

                // Check file size for streaming decision (CSV formats only)
                var fileInfo = new FileInfo(filePath);
                var isLargeFile = fileInfo.Length > 5_000_000; // 5MB threshold for streaming

                // Import based on format
                var formatLower = format.ToLowerInvariant();
                switch (formatLower)
                {
                    case "csv":
                        importedCredentials = isLargeFile
                            ? await ImportFromCsvStreamingAsync(filePath, progress)
                            : await ImportFromCsvAsync(filePath, progress);
                        break;
                    case "keepass xml":
                        importedCredentials = await ImportFromKeePassXmlAsync(filePath);
                        break;
                    case "json":
                        importedCredentials = await ImportFromJsonAsync(filePath);
                        break;
                    case "1password csv":
                        importedCredentials = await ImportFrom1PasswordCsvAsync(filePath, progress);
                        break;
                    case "bitwarden json":
                        importedCredentials = await ImportFromBitwardenJsonAsync(filePath);
                        break;
                    case "bitwarden csv":
                        importedCredentials = await ImportFromBitwardenCsvAsync(filePath, progress);
                        break;
                    case "proton pass json":
                        importedCredentials = await ImportFromProtonPassJsonAsync(filePath);
                        break;
                    case "lastpass csv":
                        importedCredentials = await ImportFromLastPassCsvAsync(filePath, progress);
                        break;
                    case "chrome csv":
                    case "edge csv":
                        importedCredentials = await ImportFromChromeCsvAsync(filePath, progress);
                        break;
                    case "firefox csv":
                        importedCredentials = await ImportFromFirefoxCsvAsync(filePath, progress);
                        break;
                    default:
                        var supportedFormats = string.Join(", ", new[]
                        {
                            "CSV", "KeePass XML", "JSON", "1Password CSV", "Bitwarden JSON",
                            "Bitwarden CSV", "Proton Pass JSON", "LastPass CSV",
                            "Chrome CSV", "Edge CSV", "Firefox CSV"
                        });
                        throw new NotSupportedException(
                            $"Format '{format}' is not supported.\n\nSupported formats:\n{supportedFormats}");
                }

                result.TotalProcessed = importedCredentials.Count;

                // Validate imported credentials and collect warnings
                var validCredentials = new List<Credential>();
                int weakPasswordCount = 0;
                int invalidUrlCount = 0;
                int expiredCount = 0;

                foreach (var cred in importedCredentials)
                {
                    if (string.IsNullOrWhiteSpace(cred.Title) && string.IsNullOrWhiteSpace(cred.Username))
                    {
                        result.Errors.Add($"Skipped credential with no title or username");
                        continue;
                    }

                    // Validate and collect warnings
                    var warnings = ValidateCredential(cred);
                    foreach (var warning in warnings)
                    {
                        result.Warnings.Add(warning);

                        // Count warning types
                        if (warning.Contains("Weak password") || warning.Contains("VeryWeak"))
                            weakPasswordCount++;
                        if (warning.Contains("Invalid URL"))
                            invalidUrlCount++;
                        if (warning.Contains("expired") || warning.Contains("expires in"))
                            expiredCount++;
                    }

                    validCredentials.Add(cred);
                }

                result.WeakPasswordCount = weakPasswordCount;
                result.InvalidUrlCount = invalidUrlCount;
                result.ExpiredCount = expiredCount;

                // Detect duplicates
                if (existingCredentials.Any())
                {
                    result.Duplicates = DetectDuplicates(validCredentials, existingCredentials);
                }

                result.SuccessfulCredentials = validCredentials;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Import failed: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Imports credentials from KeeWeb/KeePassXC KDBX database (requires password).
        /// </summary>
        /// <param name="filePath">Path to the .kdbx file.</param>
        /// <param name="password">Master password for the database.</param>
        /// <param name="keyfilePath">Optional keyfile path for additional security.</param>
        /// <param name="progress">Optional progress reporter (0-100).</param>
        /// <returns>List of imported credentials.</returns>
        public async Task<List<Credential>> ImportFromKeePassKdbxAsync(
            string filePath, 
            string password, 
            string? keyfilePath = null,
            IProgress<int>? progress = null)
        {
            var keepassService = new KeePassImportService();
            var result = await keepassService.ImportAsync(filePath, password, keyfilePath, progress);

            if (!result.IsSuccess)
            {
                throw new InvalidOperationException($"KeePass KDBX import failed: {result.Message}");
            }

            return result.Credentials;
        }

        /// <summary>
        /// Detects duplicate credentials between new imports and existing credentials.
        /// </summary>
        public List<DuplicateInfo> DetectDuplicates(List<Credential> newCredentials, List<Credential> existingCredentials)
        {
            var duplicates = new List<DuplicateInfo>();

            foreach (var newCred in newCredentials)
            {
                foreach (var existingCred in existingCredentials)
                {
                    // Exact match: same title, username, and URL
                    if (!string.IsNullOrEmpty(newCred.Title) &&
                        string.Equals(newCred.Title, existingCred.Title, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(newCred.Username, existingCred.Username, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(NormalizeUrl(newCred.Url), NormalizeUrl(existingCred.Url), StringComparison.OrdinalIgnoreCase))
                    {
                        duplicates.Add(new DuplicateInfo
                        {
                            NewCredential = newCred,
                            ExistingCredential = existingCred,
                            MatchType = DuplicateMatchType.ExactMatch,
                            KeepNew = false // Default to keeping existing for exact matches
                        });
                        break;
                    }
                    // Password reuse: same password on different accounts
                    else if (!string.IsNullOrEmpty(newCred.Password) &&
                             newCred.Password == existingCred.Password &&
                             !string.Equals(newCred.Title, existingCred.Title, StringComparison.OrdinalIgnoreCase))
                    {
                        duplicates.Add(new DuplicateInfo
                        {
                            NewCredential = newCred,
                            ExistingCredential = existingCred,
                            MatchType = DuplicateMatchType.PasswordMatch,
                            KeepNew = true // Import password reuse warnings
                        });
                        break;
                    }
                    // Username/URL match with different password (possible update)
                    else if (!string.IsNullOrEmpty(newCred.Username) &&
                             string.Equals(newCred.Username, existingCred.Username, StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(NormalizeUrl(newCred.Url), NormalizeUrl(existingCred.Url), StringComparison.OrdinalIgnoreCase) &&
                             newCred.Password != existingCred.Password)
                    {
                        duplicates.Add(new DuplicateInfo
                        {
                            NewCredential = newCred,
                            ExistingCredential = existingCred,
                            MatchType = DuplicateMatchType.UsernameUrlMatch,
                            KeepNew = true // Default to newer password
                        });
                        break;
                    }
                }
            }

            return duplicates;
        }

        /// <summary>
        /// Filters credentials based on duplicate resolution choices.
        /// </summary>
        public List<Credential> ApplyDuplicateResolution(List<Credential> newCredentials, List<DuplicateInfo> duplicates)
        {
            var duplicateNewCreds = new HashSet<Credential>(duplicates.Where(d => !d.KeepNew).Select(d => d.NewCredential));
            return newCredentials.Where(c => !duplicateNewCreds.Contains(c)).ToList();
        }

        private string NormalizeUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";

            // Remove protocol and www
            var normalized = url.ToLowerInvariant()
                .Replace("https://", "")
                .Replace("http://", "")
                .Replace("www.", "");

            // Remove trailing slash
            if (normalized.EndsWith("/"))
                normalized = normalized.Substring(0, normalized.Length - 1);

            return normalized;
        }

        private string ExtractDomainFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";

            try
            {
                // Remove protocol
                var domain = url.Replace("https://", "").Replace("http://", "").Replace("www.", "");

                // Get just the domain part (before first /)
                var slashIndex = domain.IndexOf('/');
                if (slashIndex > 0)
                    domain = domain.Substring(0, slashIndex);

                return domain;
            }
            catch
            {
                return url;
            }
        }
    }
}
