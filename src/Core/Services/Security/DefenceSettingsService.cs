using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PhantomVault.Core.Services.Security
{

    public sealed class DefenceSettingsService : IDefenceSettingsService
    {
        private readonly IReadOnlyList<DefenceRule> _rules;
        private readonly string _configFilePath;
        private Dictionary<string, bool> _ruleStates;

        public DefenceSettingsService(IReadOnlyList<DefenceRule> rules)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));

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

            if (_ruleStates.TryGetValue(ruleId, out var enabled))
                return enabled;

            return true;
        }

        public void SetRuleEnabled(string ruleId, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(ruleId))
                return;

            _ruleStates[ruleId] = enabled;

            var rule = _rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule != null)
            {
                rule.IsEnabled = enabled;
            }

            SaveRuleStates();
        }

        private Dictionary<string, bool> LoadRuleStates()
        {
            try
            {
                if (!File.Exists(_configFilePath))
                {

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

                foreach (var rule in _rules)
                {
                    if (states.TryGetValue(rule.Id, out var enabled))
                    {
                        rule.IsEnabled = enabled;
                    }
                    else
                    {

                        rule.IsEnabled = true;
                        states[rule.Id] = true;
                    }
                }

                return states;
            }
            catch
            {

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

            }
        }
    }
}

