using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using PhantomVault.UI.Helpers;
using PhantomVault.UI.Services;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels
{

    public sealed class RecoveryCodeItemViewModel : ReactiveObject
    {
        private readonly Action _usedChanged;
        private bool _isUsed;
        private bool _isRevealed;

        public RecoveryCodeItemViewModel(int index, string code, bool isUsed, Action usedChanged)
        {
            Index = index;
            Code = code;
            _isUsed = isUsed;
            _usedChanged = usedChanged;
        }

        public int Index { get; }

        public string Code { get; }

        public string Ordinal => (Index + 1).ToString("00");

        public bool IsUsed
        {
            get => _isUsed;
            set
            {
                if (_isUsed == value)
                    return;

                this.RaiseAndSetIfChanged(ref _isUsed, value);
                this.RaisePropertyChanged(nameof(StatusText));
                _usedChanged();
            }
        }

        public bool IsRevealed
        {
            get => _isRevealed;
            set
            {
                if (_isRevealed == value)
                    return;

                this.RaiseAndSetIfChanged(ref _isRevealed, value);
                this.RaisePropertyChanged(nameof(DisplayCode));
            }
        }

        public string DisplayCode => IsRevealed ? Code : new string('•', Math.Min(Code.Length, 16));

        public string StatusText => IsUsed ? "used" : "unused";

        public ICommand ToggleRevealCommand => ReactiveCommand.Create(() => IsRevealed = !IsRevealed);
    }

    public sealed class EntrySectionViewModel : ReactiveObject, IDisposable
    {
        private readonly ResolvedSection _resolved;
        private readonly TotpService _totpService = new();
        private IDisposable? _totpSubscription;
        private Bitmap? _qrBitmap;
        private bool _isRevealed;
        private bool _isQrVisible;
        private string _currentTotpCode = string.Empty;
        private int _totpSecondsRemaining;
        private bool _disposed;

        public EntrySectionViewModel(ResolvedSection resolved)
        {
            _resolved = resolved ?? throw new ArgumentNullException(nameof(resolved));

            ToggleRevealCommand = ReactiveCommand.Create(() => IsRevealed = !IsRevealed);
            ToggleQrCommand = ReactiveCommand.Create(() => IsQrVisible = !IsQrVisible);
            CopyCommand = ReactiveCommand.Create(() => CopyRequested?.Invoke(CopyPayload, Label));
            CopyTotpCommand = ReactiveCommand.Create(() => CopyRequested?.Invoke(CurrentTotpCode, $"{Label} code"));
            CopyNextRecoveryCodeCommand = ReactiveCommand.Create(CopyNextRecoveryCode);
            OpenLinkedEntryCommand = ReactiveCommand.Create(() =>
            {
                if (!string.IsNullOrWhiteSpace(Section.LinkedEntryId))
                    OpenLinkedEntryRequested?.Invoke(Section.LinkedEntryId!);
            });

            if (IsRecoveryCodes)
            {
                var used = Section.GetUsedRecoveryCodeIndexes();
                for (var i = 0; i < _resolved.RecoveryCodes.Count; i++)
                {
                    RecoveryCodes.Add(new RecoveryCodeItemViewModel(
                        i, _resolved.RecoveryCodes[i], used.Contains(i), OnRecoveryCodeUsedChanged));
                }
            }

            if (IsTotp && !string.IsNullOrWhiteSpace(_resolved.Value))
            {
                _totpSubscription = TotpTicker.Subscribe(UpdateTotpCode);
            }
        }

        public EntrySection Section => _resolved.Section;

        public string SectionId => Section.Id;

        public string Label => _resolved.Label;

        public EntrySectionKind Kind => Section.Kind;

        public string KindDisplay => Kind switch
        {
            EntrySectionKind.Note => "Note",
            EntrySectionKind.PinCode => "PIN",
            EntrySectionKind.Totp => "TOTP",
            EntrySectionKind.RecoveryEmail => "Recovery email",
            EntrySectionKind.RecoveryCodes => "Recovery codes",
            EntrySectionKind.QrCode => "QR code",
            EntrySectionKind.SecurityQuestion => "Security question",
            _ => Kind.ToString()
        };

        public string KindGlyph => Kind switch
        {
            EntrySectionKind.Note => "🗒",
            EntrySectionKind.PinCode => "🔢",
            EntrySectionKind.Totp => "⏱",
            EntrySectionKind.RecoveryEmail => "✉",
            EntrySectionKind.RecoveryCodes => "🎟",
            EntrySectionKind.QrCode => "▦",
            EntrySectionKind.Url => "🔗",
            EntrySectionKind.Phone => "☎",
            EntrySectionKind.Address => "🏠",
            EntrySectionKind.Date => "📅",
            EntrySectionKind.SecurityQuestion => "❓",
            EntrySectionKind.Secret => "🔒",
            _ => "▪"
        };

        public bool IsLinked => Section.IsLinked;

        public bool IsBrokenLink => _resolved.IsBrokenLink;

        public string LinkedEntryTitle => _resolved.LinkedEntry?.Title ?? "(entry not found)";

        public string LinkSummary => IsLinked
            ? IsBrokenLink
                ? "Linked entry is missing from this vault"
                : $"Linked to \"{LinkedEntryTitle}\""
            : string.Empty;

        public bool IsSecret => Section.IsSecret;

        public bool IsNote => Kind == EntrySectionKind.Note;

        public bool IsTotp => Kind == EntrySectionKind.Totp;

        public bool IsPin => Kind == EntrySectionKind.PinCode;

        public bool IsRecoveryCodes => Kind == EntrySectionKind.RecoveryCodes;

        public bool IsRecoveryEmail => Kind == EntrySectionKind.RecoveryEmail;

        public bool IsQrSection => Kind == EntrySectionKind.QrCode;

        public bool IsPlainValue => !IsTotp && !IsRecoveryCodes && !IsQrSection && !IsNote;

        public bool HasValue => !string.IsNullOrWhiteSpace(_resolved.Value);

        public bool CanShowQr => !string.IsNullOrWhiteSpace(_resolved.QrPayload);

        public string SecurityQuestion => Section.GetMeta(EntrySection.MetaSecurityQuestion) ?? string.Empty;

        public bool HasSecurityQuestion => !string.IsNullOrWhiteSpace(SecurityQuestion);

        public ObservableCollection<RecoveryCodeItemViewModel> RecoveryCodes { get; } = new();

        public string RecoveryCodesSummary
        {
            get
            {
                var total = RecoveryCodes.Count;
                var used = RecoveryCodes.Count(c => c.IsUsed);
                return total == 0 ? "No codes stored" : $"{total - used} of {total} unused";
            }
        }

        public bool IsRevealed
        {
            get => _isRevealed;
            set
            {
                if (_isRevealed == value)
                    return;

                this.RaiseAndSetIfChanged(ref _isRevealed, value);
                this.RaisePropertyChanged(nameof(DisplayValue));
                this.RaisePropertyChanged(nameof(RevealGlyph));
            }
        }

        public string RevealGlyph => IsRevealed ? "🙈" : "👁";

        public bool IsQrVisible
        {
            get => _isQrVisible;
            set
            {
                if (_isQrVisible == value)
                    return;

                this.RaiseAndSetIfChanged(ref _isQrVisible, value);

                if (value && _qrBitmap == null)
                {
                    _qrBitmap = QrCodeRenderer.Render(_resolved.QrPayload);
                    this.RaisePropertyChanged(nameof(QrImage));
                    this.RaisePropertyChanged(nameof(HasQrImage));
                }
            }
        }

        public Bitmap? QrImage => _qrBitmap;

        public bool HasQrImage => _qrBitmap != null;

        public string RawValue => _resolved.Value;

        public string DisplayValue
        {
            get
            {
                if (IsBrokenLink)
                    return "(linked entry not found)";

                if (!HasValue)
                    return "(empty)";

                if (!IsSecret || IsRevealed)
                    return _resolved.Value;

                return IsPin
                    ? new string('•', Math.Min(_resolved.Value.Length, PinLengthRange.Max))
                    : new string('•', Math.Min(_resolved.Value.Length, 24));
            }
        }

        public string CopyPayload => IsTotp ? CurrentTotpCode : _resolved.Value;

        public string CurrentTotpCode
        {
            get => _currentTotpCode;
            private set
            {
                // RaiseAndSetIfChanged returns the new value, not a changed flag, so
                // the equality check has to be explicit.
                if (string.Equals(_currentTotpCode, value, StringComparison.Ordinal)) return;
                this.RaiseAndSetIfChanged(ref _currentTotpCode, value);
                this.RaisePropertyChanged(nameof(FormattedTotpCode));
            }
        }

        public string FormattedTotpCode
        {
            get
            {
                if (string.IsNullOrEmpty(CurrentTotpCode))
                    return "------";

                return CurrentTotpCode.Length == 6
                    ? $"{CurrentTotpCode[..3]} {CurrentTotpCode[3..]}"
                    : CurrentTotpCode;
            }
        }

        public int TotpSecondsRemaining
        {
            get => _totpSecondsRemaining;
            private set
            {
                // RaiseAndSetIfChanged returns the new value, not a changed flag.
                if (_totpSecondsRemaining == value) return;
                this.RaiseAndSetIfChanged(ref _totpSecondsRemaining, value);
                this.RaisePropertyChanged(nameof(TotpProgress));
                this.RaisePropertyChanged(nameof(TotpCountdownText));
            }
        }

        public double TotpProgress => _resolved.TotpPeriod <= 0
            ? 0
            : Math.Clamp(TotpSecondsRemaining / (double)_resolved.TotpPeriod * 100d, 0d, 100d);

        public string TotpCountdownText => $"{TotpSecondsRemaining}s";

        public ICommand ToggleRevealCommand { get; }

        public ICommand ToggleQrCommand { get; }

        public ICommand CopyCommand { get; }

        public ICommand CopyTotpCommand { get; }

        public ICommand OpenLinkedEntryCommand { get; }

        public ICommand CopyNextRecoveryCodeCommand { get; }

        public bool HasUnusedRecoveryCodes => RecoveryCodes.Any(c => !c.IsUsed);

        /// <summary>
        /// Copies the first code not yet marked used and marks it used, which is the
        /// actual workflow — you burn recovery codes one at a time, in order.
        /// </summary>
        private void CopyNextRecoveryCode()
        {
            var next = RecoveryCodes.FirstOrDefault(c => !c.IsUsed);
            if (next == null)
            {
                CopyRequested?.Invoke(string.Empty, $"{Label} — no unused codes left");
                return;
            }

            CopyRequested?.Invoke(next.Code, $"{Label} code {next.Ordinal}");
            next.IsUsed = true;
        }

        public event Action<string, string>? CopyRequested;

        public event Action<string>? OpenLinkedEntryRequested;

        public event Action<EntrySection>? SectionChanged;

        private void OnRecoveryCodeUsedChanged()
        {
            Section.SetUsedRecoveryCodeIndexes(RecoveryCodes.Where(c => c.IsUsed).Select(c => c.Index));
            Section.LastUpdatedUtc = DateTimeOffset.UtcNow;
            this.RaisePropertyChanged(nameof(RecoveryCodesSummary));
            this.RaisePropertyChanged(nameof(HasUnusedRecoveryCodes));
            SectionChanged?.Invoke(Section);
        }

        private void UpdateTotpCode()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_resolved.Value))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        CurrentTotpCode = string.Empty;
                        TotpSecondsRemaining = 0;
                    });
                    return;
                }

                var code = _totpService.GenerateCode(
                    _resolved.Value,
                    DateTimeOffset.UtcNow,
                    _resolved.TotpDigits,
                    _resolved.TotpPeriod);

                var period = _resolved.TotpPeriod;
                var remaining = period - (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() % period);

                Dispatcher.UIThread.Post(() =>
                {
                    CurrentTotpCode = code;
                    TotpSecondsRemaining = remaining;
                });
            }
            catch
            {
                Dispatcher.UIThread.Post(() =>
                {
                    CurrentTotpCode = string.Empty;
                    TotpSecondsRemaining = 0;
                });
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _totpSubscription?.Dispose();
            _totpSubscription = null;
            _qrBitmap?.Dispose();
            _qrBitmap = null;
        }
    }
}
