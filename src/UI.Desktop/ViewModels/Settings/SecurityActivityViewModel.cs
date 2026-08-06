using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ReactiveUI;
using PhantomVault.Core.Services;
using Serilog;

namespace PhantomVault.UI.ViewModels.Settings
{
    /// <summary>
    /// Read-only, local-only Security Activity viewer. Surfaces the hash-chained
    /// <c>vault.audit</c> log (unlocks, mounts, locks, USB events) for the active vault so a
    /// user can audit exactly what happened on their device — and detect tampering via the
    /// chain-integrity check. Nothing here ever leaves the machine.
    /// </summary>
    public sealed class SecurityActivityViewModel : ReactiveObject
    {
        private readonly string? _auditPath;
        private readonly AuditService _auditService;

        private bool _isChainValid = true;
        private string _statusSummary = "No activity recorded yet.";
        private bool _isEmpty = true;

        public SecurityActivityViewModel() : this(null, new AuditService()) { }

        public SecurityActivityViewModel(string? auditPath, AuditService auditService)
        {
            _auditPath = auditPath;
            _auditService = auditService ?? new AuditService();

            RefreshCommand = ReactiveCommand.Create(Refresh);
            Refresh();
        }

        public ObservableCollection<SecurityActivityEntry> Entries { get; } = new();

        public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

        public bool IsChainValid
        {
            get => _isChainValid;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isChainValid, value);
                this.RaisePropertyChanged(nameof(StatusBrush));
            }
        }

        /// <summary>Accent when the chain is intact, warning colour when tamper is detected.</summary>
        public IBrush StatusBrush =>
            ResolveBrush(_isChainValid ? "AccentBrush" : "WarningBrush", _isChainValid ? Colors.MediumSpringGreen : Colors.OrangeRed);

        private static IBrush ResolveBrush(string key, Color fallback)
        {
            if (Application.Current?.TryFindResource(key, out var res) == true && res is IBrush brush)
                return brush;
            return new SolidColorBrush(fallback);
        }

        public string StatusSummary
        {
            get => _statusSummary;
            private set => this.RaiseAndSetIfChanged(ref _statusSummary, value);
        }

        public bool IsEmpty
        {
            get => _isEmpty;
            private set => this.RaiseAndSetIfChanged(ref _isEmpty, value);
        }

        public void Refresh()
        {
            Entries.Clear();

            if (string.IsNullOrWhiteSpace(_auditPath))
            {
                IsChainValid = true;
                IsEmpty = true;
                StatusSummary = "Activity log is not available for this vault.";
                return;
            }

            try
            {
                var result = _auditService.ReadEntries(_auditPath);

                // Newest first for display.
                foreach (var e in result.Entries.AsEnumerable().Reverse())
                    Entries.Add(SecurityActivityEntry.From(e));

                IsChainValid = result.ChainValid;
                IsEmpty = Entries.Count == 0;

                if (!result.ChainValid)
                    StatusSummary = $"⚠ Integrity check FAILED — the log may have been tampered with. {result.Error}";
                else if (IsEmpty)
                    StatusSummary = "No activity recorded yet.";
                else
                    StatusSummary = $"✓ Integrity verified — {Entries.Count} event(s), chain intact.";
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load security activity log");
                IsChainValid = false;
                IsEmpty = true;
                StatusSummary = "Could not read the activity log.";
            }
        }
    }

    /// <summary>Display projection of a single audit entry.</summary>
    public sealed class SecurityActivityEntry
    {
        public DateTimeOffset Timestamp { get; init; }
        public string TimeDisplay { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string CategoryDisplay { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public string Glyph { get; init; } = "•";

        public static SecurityActivityEntry From(AuditService.AuditEntry e)
        {
            string category = e.Category ?? string.Empty;
            return new SecurityActivityEntry
            {
                Timestamp = e.Timestamp,
                TimeDisplay = e.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                Category = category,
                CategoryDisplay = Pretty(category),
                Message = e.Message ?? string.Empty,
                Glyph = GlyphFor(category)
            };
        }

        private static string Pretty(string category) => category.ToLowerInvariant() switch
        {
            "unlock" => "Vault unlocked",
            "lock" => "Vault locked",
            "mount" => "Drive mounted",
            "unmount" => "Drive unmounted",
            "provision" => "Vault created",
            "import" => "Data imported",
            "export" => "Data exported",
            "usb-removed" => "USB removed",
            "usb-inserted" => "USB inserted",
            "" => "Event",
            _ => char.ToUpperInvariant(category[0]) + category.Substring(1)
        };

        private static string GlyphFor(string category) => category.ToLowerInvariant() switch
        {
            "unlock" => "🔓",
            "lock" => "🔒",
            "mount" => "💾",
            "unmount" => "⏏",
            "provision" => "✨",
            "import" => "⬇",
            "export" => "⬆",
            "usb-removed" => "🔌",
            "usb-inserted" => "🔌",
            _ => "•"
        };
    }
}
