using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels
{

    public sealed class LinkCandidateViewModel
    {
        public LinkCandidateViewModel(Credential credential)
        {
            Id = credential.Id;
            Title = string.IsNullOrWhiteSpace(credential.Title) ? "(untitled)" : credential.Title;
            EntryType = credential.EntryType;
            Subtitle = BuildSubtitle(credential);
        }

        public string Id { get; }

        public string Title { get; }

        public string Subtitle { get; }

        public EntryType EntryType { get; }

        public string Display => string.IsNullOrWhiteSpace(Subtitle) ? Title : $"{Title} — {Subtitle}";

        private static string BuildSubtitle(Credential credential) => credential.EntryType switch
        {
            EntryType.TotpGenerator => credential.TotpIssuer,
            EntryType.PinCode => credential.PinLabel,
            EntryType.Contact => credential.ContactEmail,
            _ => credential.Username
        };
    }

    public sealed class SectionEditorItemViewModel : ReactiveObject
    {
        private readonly EntrySection _section;
        private readonly Action<SectionEditorItemViewModel> _remove;
        private readonly Action<SectionEditorItemViewModel, int> _move;
        private LinkCandidateViewModel? _selectedLinkCandidate;
        private bool _isLinkMode;

        public SectionEditorItemViewModel(
            EntrySection section,
            IReadOnlyList<LinkCandidateViewModel> linkCandidates,
            Action<SectionEditorItemViewModel> remove,
            Action<SectionEditorItemViewModel, int> move)
        {
            _section = section ?? throw new ArgumentNullException(nameof(section));
            _remove = remove;
            _move = move;

            foreach (var candidate in linkCandidates)
            {
                LinkCandidates.Add(candidate);
            }

            _isLinkMode = section.IsLinked;
            _selectedLinkCandidate = LinkCandidates.FirstOrDefault(c =>
                string.Equals(c.Id, section.LinkedEntryId, StringComparison.Ordinal));

            RemoveCommand = ReactiveCommand.Create(() => _remove(this));
            MoveUpCommand = ReactiveCommand.Create(() => _move(this, -1));
            MoveDownCommand = ReactiveCommand.Create(() => _move(this, 1));
        }

        public EntrySection Section => _section;

        public static IReadOnlyList<EntrySectionKind> AllKinds { get; } =
            Enum.GetValues<EntrySectionKind>().ToList();

        public ObservableCollection<LinkCandidateViewModel> LinkCandidates { get; } = new();

        public EntrySectionKind Kind
        {
            get => _section.Kind;
            set
            {
                if (_section.Kind == value)
                    return;

                _section.Kind = value;

                if (string.IsNullOrWhiteSpace(_section.Label) ||
                    AllKinds.Any(k => string.Equals(_section.Label, EntrySection.DefaultLabel(k), StringComparison.Ordinal)))
                {
                    _section.Label = EntrySection.DefaultLabel(value);
                    this.RaisePropertyChanged(nameof(Label));
                }

                _section.IsSecret = EntrySection.KindDefaultsToSecret(value);

                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(IsSecret));
                this.RaisePropertyChanged(nameof(IsMultiline));
                this.RaisePropertyChanged(nameof(IsPinKind));
                this.RaisePropertyChanged(nameof(IsTotpKind));
                this.RaisePropertyChanged(nameof(IsSecurityQuestionKind));
                this.RaisePropertyChanged(nameof(ValueWatermark));
                this.RaisePropertyChanged(nameof(ValidationMessage));
                this.RaisePropertyChanged(nameof(HasValidationMessage));
            }
        }

        public string Label
        {
            get => _section.Label;
            set
            {
                if (string.Equals(_section.Label, value, StringComparison.Ordinal))
                    return;

                _section.Label = value ?? string.Empty;
                this.RaisePropertyChanged();
            }
        }

        public string Value
        {
            get => _section.Value;
            set
            {
                if (string.Equals(_section.Value, value, StringComparison.Ordinal))
                    return;

                _section.Value = value ?? string.Empty;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(ValidationMessage));
                this.RaisePropertyChanged(nameof(HasValidationMessage));
            }
        }

        public bool IsSecret
        {
            get => _section.IsSecret;
            set
            {
                if (_section.IsSecret == value)
                    return;

                _section.IsSecret = value;
                this.RaisePropertyChanged();
            }
        }

        public bool IsLinkMode
        {
            get => _isLinkMode;
            set
            {
                if (_isLinkMode == value)
                    return;

                this.RaiseAndSetIfChanged(ref _isLinkMode, value);

                if (!value)
                {
                    _section.LinkedEntryId = null;
                    _selectedLinkCandidate = null;
                    this.RaisePropertyChanged(nameof(SelectedLinkCandidate));
                }
                else if (_selectedLinkCandidate != null)
                {
                    _section.LinkedEntryId = _selectedLinkCandidate.Id;
                }

                this.RaisePropertyChanged(nameof(IsInlineMode));
                this.RaisePropertyChanged(nameof(ValidationMessage));
                this.RaisePropertyChanged(nameof(HasValidationMessage));
            }
        }

        public bool IsInlineMode => !IsLinkMode;

        public LinkCandidateViewModel? SelectedLinkCandidate
        {
            get => _selectedLinkCandidate;
            set
            {
                if (ReferenceEquals(_selectedLinkCandidate, value))
                    return;

                this.RaiseAndSetIfChanged(ref _selectedLinkCandidate, value);
                _section.LinkedEntryId = IsLinkMode ? value?.Id : null;

                if (value != null && string.IsNullOrWhiteSpace(_section.Label))
                {
                    _section.Label = value.Title;
                    this.RaisePropertyChanged(nameof(Label));
                }

                this.RaisePropertyChanged(nameof(ValidationMessage));
                this.RaisePropertyChanged(nameof(HasValidationMessage));
            }
        }

        public bool IsPinKind => Kind == EntrySectionKind.PinCode;

        public bool IsTotpKind => Kind == EntrySectionKind.Totp;

        public bool IsSecurityQuestionKind => Kind == EntrySectionKind.SecurityQuestion;

        public bool IsMultiline => Kind is EntrySectionKind.Note or EntrySectionKind.RecoveryCodes or EntrySectionKind.Address;

        public string ValueWatermark => Kind switch
        {
            EntrySectionKind.Note => "Anything you want to keep alongside this entry...",
            EntrySectionKind.PinCode => "Digits only",
            EntrySectionKind.Totp => "Base32 secret (e.g. JBSWY3DPEHPK3PXP)",
            EntrySectionKind.RecoveryEmail => "recovery@example.com",
            EntrySectionKind.RecoveryCodes => "One code per line",
            EntrySectionKind.QrCode => "Text or URL to encode as a QR code",
            EntrySectionKind.Url => "https://...",
            EntrySectionKind.Phone => "+61 400 000 000",
            EntrySectionKind.SecurityQuestion => "The answer to the question above",
            _ => "Value"
        };

        public IReadOnlyList<int> PinLengthOptions => PinLengthRange.All;

        public int PinLength
        {
            get
            {
                var stored = _section.GetMetaInt(EntrySection.MetaPinLength, 0);
                if (stored <= 0)
                    stored = string.IsNullOrEmpty(_section.Value) ? 4 : _section.Value.Length;

                return PinLengthRange.Clamp(stored);
            }
            set
            {
                var clamped = PinLengthRange.Clamp(value);
                if (PinLength == clamped)
                    return;

                _section.SetMeta(EntrySection.MetaPinLength, clamped.ToString());

                if (_section.Value.Length > clamped)
                {
                    _section.Value = _section.Value[..clamped];
                    this.RaisePropertyChanged(nameof(Value));
                }

                this.RaisePropertyChanged();
            }
        }

        public string TotpIssuer
        {
            get => _section.GetMeta(EntrySection.MetaTotpIssuer) ?? string.Empty;
            set
            {
                _section.SetMeta(EntrySection.MetaTotpIssuer, value);
                this.RaisePropertyChanged();
            }
        }

        public string TotpAccount
        {
            get => _section.GetMeta(EntrySection.MetaTotpAccount) ?? string.Empty;
            set
            {
                _section.SetMeta(EntrySection.MetaTotpAccount, value);
                this.RaisePropertyChanged();
            }
        }

        public int TotpDigits
        {
            get => _section.GetMetaInt(EntrySection.MetaTotpDigits, 6);
            set
            {
                _section.SetMeta(EntrySection.MetaTotpDigits, Math.Clamp(value, 6, 8).ToString());
                this.RaisePropertyChanged();
            }
        }

        public int TotpPeriod
        {
            get => _section.GetMetaInt(EntrySection.MetaTotpPeriod, 30);
            set
            {
                _section.SetMeta(EntrySection.MetaTotpPeriod, Math.Clamp(value, 15, 120).ToString());
                this.RaisePropertyChanged();
            }
        }

        public IReadOnlyList<int> TotpDigitOptions { get; } = new[] { 6, 7, 8 };

        public IReadOnlyList<int> TotpPeriodOptions { get; } = new[] { 30, 60 };

        public string SecurityQuestion
        {
            get => _section.GetMeta(EntrySection.MetaSecurityQuestion) ?? string.Empty;
            set
            {
                _section.SetMeta(EntrySection.MetaSecurityQuestion, value);
                this.RaisePropertyChanged();
            }
        }

        public string ValidationMessage
        {
            get
            {
                if (IsLinkMode && SelectedLinkCandidate == null)
                    return "Pick an entry to link, or switch this section back to a stored value.";

                var issues = EntrySectionService.Validate(_section);
                return issues.Count == 0 ? string.Empty : string.Join(" ", issues);
            }
        }

        public bool HasValidationMessage => !string.IsNullOrEmpty(ValidationMessage);

        public ICommand RemoveCommand { get; }

        public ICommand MoveUpCommand { get; }

        public ICommand MoveDownCommand { get; }

        public void Refresh()
        {
            this.RaisePropertyChanged(nameof(ValidationMessage));
            this.RaisePropertyChanged(nameof(HasValidationMessage));
        }
    }
}
