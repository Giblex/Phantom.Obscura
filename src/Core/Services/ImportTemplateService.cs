using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Represents a reusable import template with custom column mappings.
    /// </summary>
    public sealed class ImportTemplate
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Format { get; set; } = "CSV";
        public Dictionary<string, string> ColumnMappings { get; set; } = new();
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastUsedUtc { get; set; } = DateTimeOffset.UtcNow;
        public string CreatedBy { get; set; } = "";
        public bool IsShared { get; set; } = false;
        public int UseCount { get; set; } = 0;
    }

    /// <summary>
    /// Manages import templates for reusable column mappings and configurations.
    /// </summary>
    public sealed class ImportTemplateService
    {
        private readonly string _templatesFilePath;
        private List<ImportTemplate> _templates = new();

        public ImportTemplateService(string templatesDirectory)
        {
            _templatesFilePath = Path.Combine(templatesDirectory, "import_templates.json");
            LoadTemplates();
        }

        /// <summary>
        /// Creates a new import template.
        /// </summary>
        public async Task<ImportTemplate> CreateTemplateAsync(
            string name,
            string format,
            Dictionary<string, string> columnMappings,
            string description = "",
            string createdBy = "",
            bool isShared = false)
        {
            var template = new ImportTemplate
            {
                Name = name,
                Description = description,
                Format = format,
                ColumnMappings = new Dictionary<string, string>(columnMappings),
                CreatedBy = createdBy,
                IsShared = isShared
            };

            _templates.Add(template);
            await SaveTemplatesAsync();

            return template;
        }

        /// <summary>
        /// Gets all available templates.
        /// </summary>
        public List<ImportTemplate> GetAllTemplates()
        {
            return _templates.OrderByDescending(t => t.LastUsedUtc).ToList();
        }

        /// <summary>
        /// Gets templates for a specific format.
        /// </summary>
        public List<ImportTemplate> GetTemplatesByFormat(string format)
        {
            return _templates
                .Where(t => t.Format.Equals(format, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(t => t.LastUsedUtc)
                .ToList();
        }

        /// <summary>
        /// Gets a specific template by ID.
        /// </summary>
        public ImportTemplate? GetTemplate(string templateId)
        {
            return _templates.FirstOrDefault(t => t.Id == templateId);
        }

        /// <summary>
        /// Gets a template by name.
        /// </summary>
        public ImportTemplate? GetTemplateByName(string name)
        {
            return _templates.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Updates an existing template.
        /// </summary>
        public async Task<bool> UpdateTemplateAsync(
            string templateId,
            string? name = null,
            string? description = null,
            Dictionary<string, string>? columnMappings = null,
            bool? isShared = null)
        {
            var template = GetTemplate(templateId);
            if (template == null) return false;

            if (name != null) template.Name = name;
            if (description != null) template.Description = description;
            if (columnMappings != null) template.ColumnMappings = new Dictionary<string, string>(columnMappings);
            if (isShared.HasValue) template.IsShared = isShared.Value;

            await SaveTemplatesAsync();
            return true;
        }

        /// <summary>
        /// Records template usage (updates last used time and use count).
        /// </summary>
        public async Task RecordTemplateUsageAsync(string templateId)
        {
            var template = GetTemplate(templateId);
            if (template != null)
            {
                template.LastUsedUtc = DateTimeOffset.UtcNow;
                template.UseCount++;
                await SaveTemplatesAsync();
            }
        }

        /// <summary>
        /// Deletes a template.
        /// </summary>
        public async Task<bool> DeleteTemplateAsync(string templateId)
        {
            var template = GetTemplate(templateId);
            if (template == null) return false;

            _templates.Remove(template);
            await SaveTemplatesAsync();
            return true;
        }

        /// <summary>
        /// Exports a template to JSON file for sharing.
        /// </summary>
        public async Task<string> ExportTemplateAsync(string templateId, string exportPath)
        {
            var template = GetTemplate(templateId);
            if (template == null) throw new InvalidOperationException($"Template {templateId} not found");

            var json = JsonSerializer.Serialize(template, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            var fileName = $"{SanitizeFileName(template.Name)}_template.json";
            var fullPath = Path.Combine(exportPath, fileName);

            await File.WriteAllTextAsync(fullPath, json);
            return fullPath;
        }

        /// <summary>
        /// Imports a template from JSON file.
        /// </summary>
        public async Task<ImportTemplate> ImportTemplateAsync(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Template file not found", filePath);

            var json = await File.ReadAllTextAsync(filePath);
            var template = JsonSerializer.Deserialize<ImportTemplate>(json);

            if (template == null)
                throw new InvalidOperationException("Failed to deserialize template");

            // Generate new ID to avoid conflicts
            template.Id = Guid.NewGuid().ToString();
            template.CreatedUtc = DateTimeOffset.UtcNow;
            template.LastUsedUtc = DateTimeOffset.UtcNow;
            template.UseCount = 0;

            _templates.Add(template);
            await SaveTemplatesAsync();

            return template;
        }

        /// <summary>
        /// Gets template usage statistics.
        /// </summary>
        public TemplateStatistics GetStatistics()
        {
            return new TemplateStatistics
            {
                TotalTemplates = _templates.Count,
                SharedTemplates = _templates.Count(t => t.IsShared),
                MostUsedTemplate = _templates.OrderByDescending(t => t.UseCount).FirstOrDefault(),
                RecentlyUsedTemplate = _templates.OrderByDescending(t => t.LastUsedUtc).FirstOrDefault(),
                TotalUsage = _templates.Sum(t => t.UseCount)
            };
        }

        /// <summary>
        /// Creates predefined templates for common formats.
        /// </summary>
        public async Task CreateDefaultTemplatesAsync()
        {
            if (_templates.Any()) return; // Don't create if templates already exist

            // 1Password Template
            await CreateTemplateAsync(
                "1Password Standard",
                "1Password CSV",
                new Dictionary<string, string>
                {
                    { "Title", "title" },
                    { "Username", "username" },
                    { "Password", "password" },
                    { "URL", "url" },
                    { "Notes", "notes" },
                    { "Group", "folder" }
                },
                "Standard 1Password CSV export format",
                "System",
                isShared: true
            );

            // Bitwarden Template
            await CreateTemplateAsync(
                "Bitwarden Standard",
                "Bitwarden CSV",
                new Dictionary<string, string>
                {
                    { "Title", "name" },
                    { "Username", "login_username" },
                    { "Password", "login_password" },
                    { "URL", "login_uri" },
                    { "Notes", "notes" },
                    { "Group", "folder" }
                },
                "Standard Bitwarden CSV export format",
                "System",
                isShared: true
            );

            // LastPass Template
            await CreateTemplateAsync(
                "LastPass Standard",
                "LastPass CSV",
                new Dictionary<string, string>
                {
                    { "Title", "name" },
                    { "Username", "username" },
                    { "Password", "password" },
                    { "URL", "url" },
                    { "Notes", "extra" },
                    { "Group", "grouping" }
                },
                "Standard LastPass CSV export format",
                "System",
                isShared: true
            );
        }

        private void LoadTemplates()
        {
            try
            {
                if (File.Exists(_templatesFilePath))
                {
                    var json = File.ReadAllText(_templatesFilePath);
                    _templates = JsonSerializer.Deserialize<List<ImportTemplate>>(json) ?? new List<ImportTemplate>();
                }
            }
            catch
            {
                _templates = new List<ImportTemplate>();
            }
        }

        private async Task SaveTemplatesAsync()
        {
            try
            {
                var directory = Path.GetDirectoryName(_templatesFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(_templates, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                await File.WriteAllTextAsync(_templatesFilePath, json);
            }
            catch
            {
                // Log error but don't throw
            }
        }

        private string SanitizeFileName(string fileName)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Join("_", fileName.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
        }
    }

    /// <summary>
    /// Template usage statistics.
    /// </summary>
    public sealed class TemplateStatistics
    {
        public int TotalTemplates { get; set; }
        public int SharedTemplates { get; set; }
        public ImportTemplate? MostUsedTemplate { get; set; }
        public ImportTemplate? RecentlyUsedTemplate { get; set; }
        public int TotalUsage { get; set; }
    }
}
