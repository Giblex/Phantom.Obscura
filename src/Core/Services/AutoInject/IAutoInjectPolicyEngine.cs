using PhantomVault.Core.Models.AutoInject;

namespace PhantomVault.Core.Services.AutoInject
{
    /// <summary>
    /// Manages auto-inject policies and determines appropriate behavior
    /// </summary>
    public interface IAutoInjectPolicyEngine
    {
        /// <summary>
        /// Gets the policy for the given context
        /// </summary>
        AutoInjectPolicy GetPolicyForContext(AutoInjectContext context);

        /// <summary>
        /// Checks if auto-inject is allowed for the given context
        /// </summary>
        bool IsAutoInjectAllowed(AutoInjectContext context, AutoInjectPolicy policy);

        /// <summary>
        /// Saves or updates a policy
        /// </summary>
        void SavePolicy(AutoInjectPolicy policy);

        /// <summary>
        /// Deletes a policy
        /// </summary>
        void DeletePolicy(string id);

        /// <summary>
        /// Gets all policies
        /// </summary>
        AutoInjectPolicy[] GetAllPolicies();
    }
}
