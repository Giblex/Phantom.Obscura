using System;
using System.Reactive;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels
{

    public sealed class FrontPageViewModel : ReactiveObject
    {
        public FrontPageViewModel()
        {
            ContinueCommand = ReactiveCommand.Create(() => { OnContinueRequested?.Invoke(); });
        }

        public ReactiveCommand<Unit, Unit> ContinueCommand { get; }

        public event Action? OnContinueRequested;
    }
}

