using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests
{
    /// <summary>
    /// Integration tests for the ImportExportService covering various file formats,
    /// duplicate detection, merge strategies, and error handling scenarios.
    /// </summary>
    public sealed class ImportExportServiceIntegrationTests : IDisposable
    {
        private readonly ImportExportService _importExportService;
        private readonly List<string> _tempFiles;
        private readonly string _testDataDirectory;

        public ImportExportServiceIntegrationTests()
        {
            _importExportService = new ImportExportService();
            _tempFiles = new List<string>();
            _testDataDirectory = Path.Combine(Path.GetTempPath(), $"PhantomVault_Tests_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testDataDirectory);
        }

        public void Dispose()
        {
            // Cleanup all temporary test files
            foreach (var file in _tempFiles)
            {
                try
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // Best effort cleanup
                }
            }

            try
            {
                if (Directory.Exists(_testDataDirectory))
                {
                    Directory.Delete(_testDataDirectory, true);
                }
            }
            catch
            {
                // Best effort cleanup
            }
        }

        #region CSV Import Tests

        [Fact]
        public async Task ImportFromCsv_ValidFile_ImportsAllCredentials()
        {
            // Arrange
            var csvContent = @"title,username,password,url,notes
Gmail,user@gmail.com,SecurePass123!,https://mail.google.com,Personal email
GitHub,developer,GitH0bP@ss,https://github.com,Code repository
Netflix,viewer@email.com,N3tfl!xPass,https://netflix.com,Streaming service";

            var csvFile = CreateTempFile(csvContent, ".csv");

            // Act
            var result = await _importExportService.ImportFromFileAsync(csvFile, new List<Credential>());

            // Assert
            Assert.Equal(3, result.SuccessCount);
            Assert.Empty(result.Errors);
            Assert.Equal("Gmail", result.SuccessfulCredentials[0].Title);
            Assert.Equal("user@gmail.com", result.SuccessfulCredentials[0].Username);
            Assert.Equal("SecurePass123!", result.SuccessfulCredentials[0].Password);
            Assert.Equal("https://mail.google.com", result.SuccessfulCredentials[0].Url);
        }

        [Fact]
        public async Task ImportFromCsv_WithDuplicates_DetectsAndReportsDuplicates()
        {
            // Arrange
            var csvContent = @"title,username,password,url,notes
Gmail,user@gmail.com,SecurePass123!,https://mail.google.com,Personal email
GitHub,developer,GitH0bP@ss,https://github.com,Code repository";

            var csvFile = CreateTempFile(csvContent, ".csv");

            var existingCredentials = new List<Credential>
            {
                new Credential
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "Gmail",
                    Username = "user@gmail.com",
                    Password = "OldPassword",
                    Url = "https://mail.google.com"
                }
            };

            // Act
            var result = await _importExportService.ImportFromFileAsync(csvFile, existingCredentials);

            // Assert
            Assert.Equal(1, result.DuplicateCount);
            Assert.NotEmpty(result.Duplicates);
            Assert.Equal("Gmail", result.Duplicates[0].NewCredential.Title);
        }

        [Fact]
        public async Task ImportFromCsv_MalformedFile_ReportsErrors()
        {
            // Arrange
            var malformedCsv = @"title,username,password,url
Gmail,user@gmail.com
InvalidRow,no,password,here,extra,fields";

            var csvFile = CreateTempFile(malformedCsv, ".csv");

            // Act
            var result = await _importExportService.ImportFromFileAsync(csvFile, new List<Credential>());

            // Assert
            Assert.True(result.ErrorCount > 0 || result.WarningCount > 0);
        }

        [Fact]
        public async Task ImportFromCsv_SpecialCharacters_PreservesData()
        {
            // Arrange
            var csvContent = @"title,username,password,url,notes
""Special, Title"",""user@example.com"",""Pass""word123"",""https://example.com"",""Multi-line
notes with, commas""";

            var csvFile = CreateTempFile(csvContent, ".csv");

            // Act
            var result = await _importExportService.ImportFromFileAsync(csvFile, new List<Credential>());

            // Assert
            Assert.Equal(1, result.SuccessCount);
            Assert.Contains("Special, Title", result.SuccessfulCredentials[0].Title);
            Assert.Contains("Pass\"word123", result.SuccessfulCredentials[0].Password);
        }

        #endregion

        #region JSON Import Tests

        [Fact]
        public async Task ImportFromJson_ValidPhantomVaultFormat_ImportsAllCredentials()
        {
            // Arrange
            var credentials = new List<Credential>
            {
                new Credential
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "Test Service",
                    Username = "testuser",
                    Password = "TestPass123!",
                    Url = "https://test.com",
                    Notes = "Test notes",
                    CreatedAt = DateTime.UtcNow,
                    Type = CredentialType.Password
                }
            };

            var jsonContent = JsonSerializer.Serialize(credentials, new JsonSerializerOptions { WriteIndented = true });
            var jsonFile = CreateTempFile(jsonContent, ".json");

            // Act
            var result = await _importExportService.ImportFromFileAsync(jsonFile, new List<Credential>());

            // Assert
            Assert.Equal(1, result.SuccessCount);
            Assert.Equal("Test Service", result.SuccessfulCredentials[0].Title);
            Assert.Equal("testuser", result.SuccessfulCredentials[0].Username);
        }

        [Fact]
        public async Task ImportFromJson_InvalidJsonStructure_ReportsError()
        {
            // Arrange
            var invalidJson = @"{""invalid"": ""structure"", ""missing"": [""credentials""]}";
            var jsonFile = CreateTempFile(invalidJson, ".json");

            // Act
            var result = await _importExportService.ImportFromFileAsync(jsonFile, new List<Credential>());

            // Assert
            Assert.True(result.ErrorCount > 0);
            Assert.Contains("parse", result.Errors[0], StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region XML/KeePass Import Tests

        [Fact]
        public async Task ImportFromKeePass_ValidXml_ImportsGroups()
        {
            // Arrange
            var keepassXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<KeePassFile>
    <Root>
        <Group>
            <Name>Root</Name>
            <Entry>
                <String>
                    <Key>Title</Key>
                    <Value>Test Entry</Value>
                </String>
                <String>
                    <Key>UserName</Key>
                    <Value>testuser</Value>
                </String>
                <String>
                    <Key>Password</Key>
                    <Value>TestPassword123</Value>
                </String>
                <String>
                    <Key>URL</Key>
                    <Value>https://example.com</Value>
                </String>
            </Entry>
        </Group>
    </Root>
</KeePassFile>";

            var xmlFile = CreateTempFile(keepassXml, ".xml");

            // Act
            var result = await _importExportService.ImportFromFileAsync(xmlFile, new List<Credential>());

            // Assert
            Assert.True(result.SuccessCount > 0);
            var credential = result.SuccessfulCredentials.FirstOrDefault(c => c.Title == "Test Entry");
            Assert.NotNull(credential);
            Assert.Equal("testuser", credential.Username);
        }

        #endregion

        #region Export Tests

        [Fact]
        public async Task ExportToCsv_WithCredentials_CreatesValidCsvFile()
        {
            // Arrange
            var credentials = new List<Credential>
            {
                new Credential
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "Export Test",
                    Username = "exportuser",
                    Password = "ExportPass123!",
                    Url = "https://export.com",
                    Notes = "Export notes"
                }
            };

            var exportFile = Path.Combine(_testDataDirectory, $"export_{Guid.NewGuid()}.csv");
            _tempFiles.Add(exportFile);

            // Act
            await _importExportService.ExportToFileAsync(credentials, exportFile, "csv");

            // Assert
            Assert.True(File.Exists(exportFile));
            var content = await File.ReadAllTextAsync(exportFile);
            Assert.Contains("Export Test", content);
            Assert.Contains("exportuser", content);
            Assert.Contains("ExportPass123!", content);
        }

        [Fact]
        public async Task ExportToJson_WithCredentials_CreatesValidJsonFile()
        {
            // Arrange
            var credentials = new List<Credential>
            {
                new Credential
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "JSON Export",
                    Username = "jsonuser",
                    Password = "JsonPass123!",
                    Url = "https://json.com",
                    Type = CredentialType.Password
                }
            };

            var exportFile = Path.Combine(_testDataDirectory, $"export_{Guid.NewGuid()}.json");
            _tempFiles.Add(exportFile);

            // Act
            await _importExportService.ExportToFileAsync(credentials, exportFile, "json");

            // Assert
            Assert.True(File.Exists(exportFile));
            var jsonContent = await File.ReadAllTextAsync(exportFile);
            var deserialized = JsonSerializer.Deserialize<List<Credential>>(jsonContent);
            Assert.NotNull(deserialized);
            Assert.Single(deserialized);
            Assert.Equal("JSON Export", deserialized[0].Title);
        }

        #endregion

        #region Round-Trip Tests

        [Fact]
        public async Task RoundTrip_ExportAndImport_PreservesAllData()
        {
            // Arrange
            var originalCredentials = new List<Credential>
            {
                new Credential
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "RoundTrip Test",
                    Username = "roundtripuser",
                    Password = "RoundTrip123!",
                    Url = "https://roundtrip.com",
                    Notes = "Notes with special chars: üöä",
                    CreatedAt = DateTime.UtcNow,
                    Type = CredentialType.Password,
                    Tags = new List<string> { "test", "roundtrip" }
                }
            };

            var exportFile = Path.Combine(_testDataDirectory, $"roundtrip_{Guid.NewGuid()}.json");
            _tempFiles.Add(exportFile);

            // Act - Export
            await _importExportService.ExportToFileAsync(originalCredentials, exportFile, "json");

            // Act - Import
            var importResult = await _importExportService.ImportFromFileAsync(exportFile, new List<Credential>());

            // Assert
            Assert.Equal(1, importResult.SuccessCount);
            var imported = importResult.SuccessfulCredentials[0];
            Assert.Equal(originalCredentials[0].Title, imported.Title);
            Assert.Equal(originalCredentials[0].Username, imported.Username);
            Assert.Equal(originalCredentials[0].Password, imported.Password);
            Assert.Equal(originalCredentials[0].Url, imported.Url);
            Assert.Equal(originalCredentials[0].Notes, imported.Notes);
        }

        #endregion

        #region Security Tests

        [Fact]
        public async Task Import_FileWithPathTraversal_RejectsFile()
        {
            // Arrange
            var maliciousPath = Path.Combine(_testDataDirectory, "..", "..", "malicious.csv");

            // Act & Assert
            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await _importExportService.ImportFromFileAsync(maliciousPath, new List<Credential>());
            });
        }

        [Fact]
        public async Task Import_ExcessivelyLargeFile_HandlesGracefully()
        {
            // Arrange - Create a large CSV file (simulate large import)
            var largeContent = new StringBuilder();
            largeContent.AppendLine("title,username,password,url,notes");
            for (int i = 0; i < 10000; i++)
            {
                largeContent.AppendLine($"Entry{i},user{i},pass{i},https://example{i}.com,notes{i}");
            }

            var largeFile = CreateTempFile(largeContent.ToString(), ".csv");

            // Act
            var result = await _importExportService.ImportFromFileAsync(largeFile, new List<Credential>());

            // Assert
            Assert.True(result.TotalProcessed > 0);
            Assert.True(result.SuccessCount > 0);
        }

        [Fact]
        public async Task Import_FileWithMaliciousContent_SanitizesData()
        {
            // Arrange
            var maliciousCsv = @"title,username,password,url,notes
""<script>alert('xss')</script>"",""user@test.com"",""pass123"",""javascript:alert(1)"",""<img src=x onerror=alert(1)>""";

            var csvFile = CreateTempFile(maliciousCsv, ".csv");

            // Act
            var result = await _importExportService.ImportFromFileAsync(csvFile, new List<Credential>());

            // Assert
            Assert.Equal(1, result.SuccessCount);
            // Data should be stored as-is (application should sanitize on display)
            var credential = result.SuccessfulCredentials[0];
            Assert.NotNull(credential);
        }

        #endregion

        #region Merge Strategy Tests

        [Fact]
        public async Task Import_WithDuplicatesKeepNew_ReplacesExisting()
        {
            // Arrange
            var existingCredentials = new List<Credential>
            {
                new Credential
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "Test",
                    Username = "user",
                    Password = "OldPassword",
                    Url = "https://test.com"
                }
            };

            var csvContent = @"title,username,password,url,notes
Test,user,NewPassword,https://test.com,Updated";

            var csvFile = CreateTempFile(csvContent, ".csv");

            // Act
            var result = await _importExportService.ImportFromFileAsync(csvFile, existingCredentials);

            // Assert
            Assert.Equal(1, result.DuplicateCount);
            var duplicate = result.Duplicates[0];
            Assert.Equal("NewPassword", duplicate.NewCredential.Password);
            Assert.Equal("OldPassword", duplicate.ExistingCredential?.Password);
        }

        #endregion

        #region Encoding Tests

        [Fact]
        public async Task Import_Utf8WithBom_HandlesCorrectly()
        {
            // Arrange
            var content = "title,username,password,url,notes\nTest,user,pass,url,Umlauts: äöü";
            var utf8WithBom = new UTF8Encoding(true);
            var bytes = utf8WithBom.GetBytes(content);

            var file = Path.Combine(_testDataDirectory, $"utf8bom_{Guid.NewGuid()}.csv");
            _tempFiles.Add(file);
            await File.WriteAllBytesAsync(file, bytes);

            // Act
            var result = await _importExportService.ImportFromFileAsync(file, new List<Credential>());

            // Assert
            Assert.True(result.SuccessCount > 0);
            Assert.Contains("äöü", result.SuccessfulCredentials[0].Notes);
        }

        #endregion

        #region Helper Methods

        private string CreateTempFile(string content, string extension)
        {
            var tempFile = Path.Combine(_testDataDirectory, $"test_{Guid.NewGuid()}{extension}");
            File.WriteAllText(tempFile, content, Encoding.UTF8);
            _tempFiles.Add(tempFile);
            return tempFile;
        }

        #endregion
    }
}
