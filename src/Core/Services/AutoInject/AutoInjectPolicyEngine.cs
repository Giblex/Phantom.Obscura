using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using PhantomVault.Core.Models.AutoInject;

namespace PhantomVault.Core.Services.AutoInject
{
    /// <summary>
    /// Implementation of policy engine with file-based persistence
    /// </summary>
    public class AutoInjectPolicyEngine : IAutoInjectPolicyEngine
    {
        private readonly string _policiesPath;
        private readonly List<AutoInjectPolicy> _policies = new();
        private readonly object _lock = new();

        public AutoInjectPolicyEngine(string dataDirectory)
        {
            _policiesPath = Path.Combine(dataDirectory, "autoinject_policies.json");
            LoadPolicies();
        }

        public AutoInjectPolicy GetPolicyForContext(AutoInjectContext context)
        {
            lock (_lock)
            {
                // Try to find exact domain match first
                if (!string.IsNullOrEmpty(context.Domain))
                {
                    var exactMatch = _policies.FirstOrDefault(p =>
                        p.DomainPattern.Equals(context.Domain, StringComparison.OrdinalIgnoreCase));
                    if (exactMatch != null)
                        return exactMatch;

                    // Try wildcard matches
                    var wildcardMatch = _policies.FirstOrDefault(p =>
                        IsWildcardMatch(p.DomainPattern, context.Domain));
                    if (wildcardMatch != null)
                        return wildcardMatch;
                }

                // Try process name match
                if (!string.IsNullOrEmpty(context.ProcessName))
                {
                    var processMatch = _policies.FirstOrDefault(p =>
                        !string.IsNullOrEmpty(p.ProcessPattern) &&
                        IsWildcardMatch(p.ProcessPattern, context.ProcessName));
                    if (processMatch != null)
                        return processMatch;
                }

                // Return default policy (prompt)
                return new AutoInjectPolicy
                {
                    DomainPattern = "*",
                    Behavior = AutoInjectBehavior.Prompt
                };
            }
        }

        public bool IsAutoInjectAllowed(AutoInjectContext context, AutoInjectPolicy policy)
        {
            // Check behavior
            if (policy.Behavior == AutoInjectBehavior.Never)
                return false;

            // Check machine restrictions
            if (policy.AllowedMachines.Count > 0 &&
                !string.IsNullOrEmpty(context.MachineFingerprint))
            {
                if (!policy.AllowedMachines.Contains(context.MachineFingerprint))
                    return false;
            }

            // Check time restrictions
            if (policy.TimeRestriction != null)
            {
                var now = DateTime.Now;
                var currentHour = now.Hour;
                var currentDay = (int)now.DayOfWeek;

                if (currentHour < policy.TimeRestriction.StartHour ||
                    currentHour > policy.TimeRestriction.EndHour)
                    return false;

                if (policy.TimeRestriction.AllowedDays.Count > 0 &&
                    !policy.TimeRestriction.AllowedDays.Contains(currentDay))
                    return false;
            }

            return true;
        }

        public void SavePolicy(AutoInjectPolicy policy)
        {
            lock (_lock)
            {
                policy.ModifiedAt = DateTime.UtcNow;

                var existing = _policies.FirstOrDefault(p => p.Id == policy.Id);
                if (existing != null)
                {
                    _policies.Remove(existing);
                }

                _policies.Add(policy);
                SavePolicies();
            }
        }

        public void DeletePolicy(string id)
        {
            lock (_lock)
            {
                _policies.RemoveAll(p => p.Id.ToString() == id);
                SavePolicies();
            }
        }

        public AutoInjectPolicy[] GetAllPolicies()
        {
            lock (_lock)
            {
                return _policies.ToArray();
            }
        }

        private bool IsWildcardMatch(string pattern, string input)
        {
            if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(input))
                return false;

            // Convert wildcard pattern to regex
            var regexPattern = "^" + Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";

            return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase);
        }

        private void LoadPolicies()
        {
            try
            {
                if (File.Exists(_policiesPath))
                {
                    var json = File.ReadAllText(_policiesPath);
                    var loaded = JsonSerializer.Deserialize<List<AutoInjectPolicy>>(json);
                    if (loaded != null)
                    {
                        _policies.AddRange(loaded);
                    }
                }
            }
            catch
            {
                // Ignore errors on load, start with empty policies
            }
        }

        private void SavePolicies()
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                var json = JsonSerializer.Serialize(_policies, options);

                var directory = Path.GetDirectoryName(_policiesPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(_policiesPath, json);
            }
            catch
            {
                // Log error in production
            }
        }
    }
}
