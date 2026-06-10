using PhantomVault.Core.Models.AutoInject;

namespace PhantomVault.Core.Services.AutoInject
{

    public interface IAutoInjectPolicyEngine
    {

        AutoInjectPolicy GetPolicyForContext(AutoInjectContext context);

        bool IsAutoInjectAllowed(AutoInjectContext context, AutoInjectPolicy policy);

        void SavePolicy(AutoInjectPolicy policy);

        void DeletePolicy(string id);

        AutoInjectPolicy[] GetAllPolicies();
    }
}

