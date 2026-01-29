using System.Threading.Tasks;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Implement on viewmodels which should be automatically reset when an error dialog is shown.
    /// The dialog service will call <see cref="ResetAfterErrorAsync"/> after the error dialog is dismissed.
    /// </summary>
    public interface IResettableOnError
    {
        /// <summary>
        /// Reset UI state after an error so the flow can continue (for example resume USB detection).
        /// </summary>
        Task ResetAfterErrorAsync();
    }
}
