using System;
using System.Reactive;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels.AutoFill
{
    /// <summary>
    /// ViewModel for the dialog shown when no stored credential matches the
    /// currently active login portal. Allows the user to add a new credential
    /// entry and, when Attestor is linked, hand off passkey creation for the portal.
    /// </summary>
    public sealed class NoMatchFoundViewModel : ReactiveObject
    {
        private readonly bool _attestorLinked;
        private readonly string _attestorStatus;

        public NoMatchFoundViewModel(string portalIdentifier, bool attestorLinked = false, string? attestorStatus = null)
        {
            PortalIdentifier = portalIdentifier;
            _attestorLinked = attestorLinked;
            _attestorStatus = attestorStatus ?? "PhantomAttestor is not currently available from this suite build.";

            CanCreatePasskey = attestorLinked;

            AddNewEntryCommand = ReactiveCommand.Create(OnAddNewEntry);
            CreatePasskeyCommand = ReactiveCommand.Create(OnCreatePasskey);
            CancelCommand = ReactiveCommand.Create(OnCancel);
        }

        /// <summary>Domain or app name of the unmatched portal.</summary>
        public string PortalIdentifier { get; }

        /// <summary>Human-readable message shown in the dialog body.</summary>
        public string Message =>
            CanCreatePasskey
                ? $"No saved credential found for \"{PortalIdentifier}\".\nYou can add one here or open the linked Attestor passkey flow."
                : $"No saved credential found for \"{PortalIdentifier}\".\nYou can add one here now.";

        public string AttestorStatus => _attestorStatus;

        /// <summary>True when Attestor is linked and passkey handoff is available.</summary>
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
