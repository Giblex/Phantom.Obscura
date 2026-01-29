using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Default implementation of IDefenceSettingsService that persists rule enable/disable
    /// state to a JSON configuration file in the user's local application data folder.
    /// </summary>
    public sealed class DefenceSettingsService : IDefenceSettingsService
    {
        private readonly IReadOnlyList<DefenceRule> _rules;
        private readonly string _configFilePath;
        private Dictionary<string, bool> _ruleStates;

        public DefenceSettingsService(IReadOnlyList<DefenceRule> rules)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));

            // Store config in local app data
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(appDataPath, "PhantomVault");
            Directory.CreateDirectory(appFolder);
            _configFilePath = Path.Combine(appFolder, "defence-rules.json");

            _ruleStates = LoadRuleStates();
        }

        public bool GetRuleEnabled(string ruleId)
        {
            if (string.IsNullOrWhiteSpace(ruleId))
                return false;

            // Check persisted state first
            if (_ruleStates.TryGetValue(ruleId, out var enabled))
                return enabled;

            // Default to enabled if not found in persisted state
            return true;
        }

        public void SetRuleEnabled(string ruleId, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(ruleId))
                return;

            // Update in-memory state
            _ruleStates[ruleId] = enabled;

            // Update the rule object itself
            var rule = _rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule != null)
            {
                rule.IsEnabled = enabled;
            }

            // Persist to disk
            SaveRuleStates();
        }

        private Dictionary<string, bool> LoadRuleStates()
        {
            try
            {
                if (!File.Exists(_configFilePath))
                {
                    // Initialize with default enabled state for all rules
                    var defaultStates = new Dictionary<string, bool>();
                    foreach (var rule in _rules)
                    {
                        defaultStates[rule.Id] = true;
                        rule.IsEnabled = true;
                    }
                    return defaultStates;
                }

                var json = File.ReadAllText(_configFilePath);
                var states = JsonSerializer.Deserialize<Dictionary<string, bool>>(json) 
                             ?? new Dictionary<string, bool>();

                // Apply loaded states to rules
                foreach (var rule in _rules)
                {
                    if (states.TryGetValue(rule.Id, out var enabled))
                    {
                        rule.IsEnabled = enabled;
                    }
                    else
                    {
                        // Default to enabled
                        rule.IsEnabled = true;
                        states[rule.Id] = true;
                    }
                }

                return states;
            }
            catch
            {
                // On any error, return default enabled state
                var defaultStates = new Dictionary<string, bool>();
                foreach (var rule in _rules)
                {
                    defaultStates[rule.Id] = true;
                    rule.IsEnabled = true;
                }
                return defaultStates;
            }
        }

        private void SaveRuleStates()
        {
            try
            {
                var json = JsonSerializer.Serialize(_ruleStates, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                File.WriteAllText(_configFilePath, json);
            }
            catch
            {
                // Silently fail - settings won't persist but won't crash the app
            }
        }
    }
}
