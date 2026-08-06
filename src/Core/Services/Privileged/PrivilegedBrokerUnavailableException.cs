using System;

namespace PhantomVault.Core.Services.Privileged
{
    /// <summary>
    /// Thrown when a privileged volume operation is requested but the process is
    /// neither elevated nor able to reach the privileged broker service. The UI
    /// catches this to offer a one-time "Enable privileged helper" install.
    /// </summary>
    public sealed class PrivilegedBrokerUnavailableException : Exception
    {
        public PrivilegedBrokerUnavailableException()
            : base("This action needs the Phantom Obscura privileged helper. Install it once to continue without an administrator prompt.")
        {
        }

        public PrivilegedBrokerUnavailableException(string message) : base(message)
        {
        }

        public PrivilegedBrokerUnavailableException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
