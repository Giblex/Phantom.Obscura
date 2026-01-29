using System;
using System.Reactive;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// View model for the welcome/front page. Contains a command to
    /// proceed to the main dashboard. The view triggers the event on
    /// behalf of the view model so that application navigation can be
    /// handled in code-behind.
    /// </summary>
    public sealed class FrontPageViewModel : ReactiveObject
    {
        public FrontPageViewModel()
        {
            ContinueCommand = ReactiveCommand.Create(() => { OnContinueRequested?.Invoke(); });
        }

        public ReactiveCommand<Unit, Unit> ContinueCommand { get; }

        /// <summary>
        /// Raised when the user chooses to continue to the main dashboard.
        /// </summary>
        public event Action? OnContinueRequested;
    }
}