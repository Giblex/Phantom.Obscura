using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PhantomVault.Android.ViewModels
{
    /// <summary>
    /// Base ViewModel providing common busy/error state tracking.
    /// </summary>
    public abstract partial class BaseViewModel : ObservableObject
    {
        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string? _errorMessage;

        [ObservableProperty]
        private string? _statusMessage;

        protected void ClearMessages()
        {
            ErrorMessage = null;
            StatusMessage = null;
        }

        protected async Task RunSafeAsync(Func<Task> action, string? busyMessage = null)
        {
            if (IsBusy) return;
            ClearMessages();
            IsBusy = true;
            StatusMessage = busyMessage;
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsBusy = false;
                StatusMessage = null;
            }
        }
    }
}
