using System;
using System.Reactive;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels.AutoFill
{
    /// <summary>
    /// ViewModel for the dialog shown when no stored credential matches the
    /// currently active login portal. Allows the user to add a new credential
    /// entry or create a passkey for the portal.
    /// </summary>
    public sealed class NoMatchFoundViewModel : ReactiveObject
    {
        private readonly bool _attestorLinked;

        public NoMatchFoundViewModel(string portalIdentifier, bool attestorLinked = false)
        {
            PortalIdentifier = portalIdentifier;
            _attestorLinked = attestorLinked;

            CanCreatePasskey = attestorLinked;

            AddNewEntryCommand = ReactiveCommand.Create(OnAddNewEntry);
            CreatePasskeyCommand = ReactiveCommand.Create(OnCreatePasskey);
            CancelCommand = ReactiveCommand.Create(OnCancel);
        }

        /// <summary>Domain or app name of the unmatched portal.</summary>
        public string PortalIdentifier { get; }

        /// <summary>Human-readable message shown in the dialog body.</summary>
        public string Message =>
            $"No saved credential found for \"{PortalIdentifier}\".\nWould you like to add one now?";

        /// <summary>True when Attestor is linked and passkey creation is available.</summary>
        public bool CanCreatePasskey { get; }

        public ReactiveCommand<Unit, Unit> AddNewEntryCommand { get; }
        public ReactiveCommand<Unit, Unit> CreatePasskeyCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }

        /// <summary>Raised when the user has made a choice. Subscribers should close the window.</summary>
        public event EventHandler<NoMatchResult>? ResultChosen;

        private void OnAddNewEntry() => ResultChosen?.Invoke(this, NoMatchResult.AddNewEntry);
        private void OnCreatePasskey() => ResultChosen?.Invoke(this, NoMatchResult.CreatePasskey);
        private void OnCancel() => ResultChosen?.Invoke(this, NoMatchResult.Cancel);
    }

    public enum NoMatchResult
    {
        AddNewEntry,
        CreatePasskey,
        Cancel
    }
}
