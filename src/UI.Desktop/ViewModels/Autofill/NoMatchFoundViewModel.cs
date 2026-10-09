using System;
using System.Reactive;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels.AutoFill
{

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
            GeneratePasswordCommand = ReactiveCommand.Create(OnGeneratePassword);
            CancelCommand = ReactiveCommand.Create(OnCancel);
        }

        public string PortalIdentifier { get; }

        public string Message =>
            CanCreatePasskey
                ? $"No saved credential found for \"{PortalIdentifier}\".\nYou can add one here or open the linked Attestor passkey flow."
                : $"No saved credential found for \"{PortalIdentifier}\".\nYou can add one here now.";

        public string AttestorStatus => _attestorStatus;

        public bool CanCreatePasskey { get; }

        public ReactiveCommand<Unit, Unit> AddNewEntryCommand { get; }
        public ReactiveCommand<Unit, Unit> CreatePasskeyCommand { get; }

        /// <summary>
        /// Generate a strong password, type it into the field that triggered this, and save it.
        /// The common case when signing up for something new, and previously the user had to go
        /// and do it by hand in another window.
        /// </summary>
        public ReactiveCommand<Unit, Unit> GeneratePasswordCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }

        public event EventHandler<NoMatchResult>? ResultChosen;

        private void OnAddNewEntry() => ResultChosen?.Invoke(this, NoMatchResult.AddNewEntry);
        private void OnCreatePasskey() => ResultChosen?.Invoke(this, NoMatchResult.CreatePasskey);
        private void OnGeneratePassword() => ResultChosen?.Invoke(this, NoMatchResult.GeneratePassword);
        private void OnCancel() => ResultChosen?.Invoke(this, NoMatchResult.Cancel);
    }

    public enum NoMatchResult
    {
        AddNewEntry,
        CreatePasskey,
        GeneratePassword,
        Cancel
    }
}

