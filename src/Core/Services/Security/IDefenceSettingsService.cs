namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Service for managing Defence Engine rule enable/disable state.
    /// Provides a simple abstraction for toggling defence rules on/off
    /// and persisting those preferences.
    /// </summary>
    public interface IDefenceSettingsService
    {
        /// <summary>
        /// Checks if a defence rule with the given ID is currently enabled.
        /// </summary>
        /// <param name="ruleId">The unique identifier of the rule.</param>
        /// <returns>True if the rule is enabled, false otherwise.</returns>
        bool GetRuleEnabled(string ruleId);

        /// <summary>
        /// Enables or disables a defence rule with the given ID and persists the change.
        /// </summary>
        /// <param name="ruleId">The unique identifier of the rule.</param>
        /// <param name="enabled">True to enable the rule, false to disable it.</param>
        void SetRuleEnabled(string ruleId, bool enabled);
    }
}
