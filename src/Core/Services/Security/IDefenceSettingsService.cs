namespace PhantomVault.Core.Services.Security
{

    public interface IDefenceSettingsService
    {

        bool GetRuleEnabled(string ruleId);

        void SetRuleEnabled(string ruleId, bool enabled);
    }
}

