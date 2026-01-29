namespace PhantomVault.Core.Services.Security
{
    /// <summary>
    /// Central defence engine that monitors security threats and executes automated responses.
    /// Acts as the security brain of the application, coordinating defensive actions across subsystems.
    /// </summary>
    public interface IDefenceEngine
    {
        /// <summary>
        /// Reports a security threat to the defence engine for evaluation and response.
        /// The engine will check configured rules and execute appropriate defensive actions.
        /// </summary>
        /// <param name="threat">The threat event to process.</param>
        void RaiseThreat(ThreatEvent threat);
    }
}
