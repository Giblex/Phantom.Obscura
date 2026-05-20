using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using PhantomVault.Core.Models;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels
{
    public sealed class DuplicateScanViewModel : ReactiveObject
    {
        private const string MissingIdReason = "Missing stable vault id.";
        private const string MissingTitleReason = "Missing title metadata.";
        private const string MissingLocatorReason = "Missing account locator metadata.";
        private const string MissingTimestampReason = "Missing or invalid timestamp metadata.";
        internal const string AmbiguousSmartSelectionReason = "Ambiguous best item: metadata score and update time are tied.";

        private string _summary = "Scanning vault...";
        private string _reviewStatus = string.Empty;
        private bool _hasDuplicates;
        private int _selectedCount;
        private int _scannedCredentialCount;
        private int _actionableGroupCount;
        private int _blockedCandidateCount;
        private int _actionableDuplicateCount;

        public ObservableCollection<DuplicateGroupItem> Groups { get; } = new();
        public ObservableCollection<DuplicateIssueItem> BlockedItems { get; } = new();

        public string Summary
        {
            get => _summary;
            private set => this.RaiseAndSetIfChanged(ref _summary, value);
        }

        public string ReviewStatus
        {
            get => _reviewStatus;
            private set => this.RaiseAndSetIfChanged(ref _reviewStatus, value);
        }

        public bool HasDuplicates
        {
            get => _hasDuplicates;
            private set => this.RaiseAndSetIfChanged(ref _hasDuplicates, value);
        }

        public int ScannedCredentialCount
        {
            get => _scannedCredentialCount;
            private set => this.RaiseAndSetIfChanged(ref _scannedCredentialCount, value);
        }

        public int ActionableGroupCount
        {
            get => _actionableGroupCount;
            private set => this.RaiseAndSetIfChanged(ref _actionableGroupCount, value);
        }

        public int BlockedCandidateCount
        {
            get => _blockedCandidateCount;
            private set => this.RaiseAndSetIfChanged(ref _blockedCandidateCount, value);
        }

        public int ActionableDuplicateCount
        {
            get => _actionableDuplicateCount;
            private set => this.RaiseAndSetIfChanged(ref _actionableDuplicateCount, value);
        }

        public string ScanDisposition => HasDuplicates
            ? "Manual review required before anything is moved"
            : "No duplicate removal is currently actionable";

        public bool HasBlockedItems => BlockedItems.Count > 0 || Groups.Any(g => g.IsBlocked);
        public int SelectedCount
        {
            get => _selectedCount;
            private set
            {
                this.RaiseAndSetIfChanged(ref _selectedCount, value);
                this.RaisePropertyChanged(nameof(CanDeleteSelected));
            }
        }

        public bool CanDeleteSelected => SelectedCount > 0;

        public ICommand SmartSelectCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand ClearSelectionCommand { get; }
        public ICommand DeleteSelectedCommand { get; }
        public ICommand CancelCommand { get; }

        public event Action<List<Credential>>? DeleteRequested;
        public event Action? CloseRequested;

        public DuplicateScanViewModel(IEnumerable<Credential> credentials)
        {
            Scan(credentials ?? throw new ArgumentNullException(nameof(credentials)));

            SmartSelectCommand = ReactiveCommand.Create(SmartSelect);
            SelectAllCommand = ReactiveCommand.Create(() => ToggleAll(true));
            ClearSelectionCommand = ReactiveCommand.Create(() => ToggleAll(false));
            DeleteSelectedCommand = ReactiveCommand.Create(DeleteSelected);
            CancelCommand = ReactiveCommand.Create(() => CloseRequested?.Invoke());

            RecalculateSelection();
        }

        private void Scan(IEnumerable<Credential> credentials)
        {
            var credentialList = credentials.ToList();
            ScannedCredentialCount = credentialList.Count;

            var keyed = new List<(Credential Credential, DuplicateCandidateKey Key)>();
            var duplicateIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var credential in credentialList)
            {
                var issues = ValidateCandidate(credential);
                if (issues.Count > 0)
                {
                    BlockedItems.Add(new DuplicateIssueItem(credential, string.Join(" ", issues)));
                    continue;
                }

                if (!duplicateIds.Add(credential.Id))
                {
                    BlockedItems.Add(new DuplicateIssueItem(credential, "Duplicate vault id. Deletion is blocked until ids are repaired."));
                    continue;
                }

                keyed.Add((credential, DuplicateCandidateKey.From(credential)));
            }

            foreach (var group in keyed.GroupBy(k => k.Key).Where(g => g.Count() > 1))
            {
                var candidates = group
                    .Select(k => new DuplicateEntryItem(k.Credential, OnEntrySelectionChanged))
                    .OrderBy(e => e.Credential.LastUpdatedUtc)
                    .ThenBy(e => e.Credential.Id, StringComparer.Ordinal)
                    .ToList();

                var item = DuplicateGroupItem.Create(group.Key, candidates);
                Groups.Add(item);
            }

            HasDuplicates = Groups.Count > 0;
            this.RaisePropertyChanged(nameof(HasBlockedItems));
            this.RaisePropertyChanged(nameof(ScanDisposition));

            var actionableGroups = Groups.Count(g => !g.IsBlocked);
            var blockedGroups = Groups.Count(g => g.IsBlocked);
            var duplicateCount = Groups.Where(g => !g.IsBlocked).Sum(g => Math.Max(0, g.Entries.Count - 1));
            ActionableGroupCount = actionableGroups;
            BlockedCandidateCount = blockedGroups + BlockedItems.Count;
            ActionableDuplicateCount = duplicateCount;

            Summary = HasDuplicates
                ? $"Found {duplicateCount} actionable duplicate item(s) across {actionableGroups} group(s). {blockedGroups + BlockedItems.Count} candidate set(s) are blocked until metadata is complete."
                : BlockedItems.Count > 0
                    ? $"No actionable duplicates found. {BlockedItems.Count} item(s) have incomplete metadata and were blocked."
                    : "No duplicate credentials found in the vault.";
        }

        public static IReadOnlyList<string> ValidateCandidate(Credential credential)
        {
            var issues = new List<string>();
            if (credential == null)
            {
                issues.Add("Missing credential.");
                return issues;
            }

            if (string.IsNullOrWhiteSpace(credential.Id))
            {
                issues.Add(MissingIdReason);
            }

            if (string.IsNullOrWhiteSpace(credential.Title))
            {
                issues.Add(MissingTitleReason);
            }

            if (!HasCompleteTimestamp(credential.CreatedUtc) ||
                !HasCompleteTimestamp(credential.LastUpdatedUtc) ||
                credential.LastUpdatedUtc < credential.CreatedUtc)
            {
                issues.Add(MissingTimestampReason);
            }

            if (!HasLocator(credential))
            {
                issues.Add(MissingLocatorReason);
            }

            return issues;
        }

        public static string BuildCandidateKey(Credential credential)
            => DuplicateCandidateKey.From(credential).Display;

        public static int GetInformationScore(Credential credential)
            => DuplicateEntryItem.CalculateInformationScore(credential);

        private static bool HasCompleteTimestamp(DateTimeOffset value)
            => value != default && value.UtcDateTime.Year > 2000;

        private static bool HasLocator(Credential credential)
        {
            return credential.EntryType switch
            {
                EntryType.Password => HasAny(credential.Username, credential.Url),
                EntryType.WiFi => HasAny(credential.WiFiSSID, credential.WiFiBSSID),
                EntryType.Identity => HasAny(credential.IdNumber, credential.IdCardNumber),
                EntryType.ApiKey => HasAny(credential.ApiEndpoint, credential.ApiDocumentationUrl, credential.Username),
                EntryType.Contact => HasAny(credential.ContactEmail, credential.ContactPhone, credential.ContactFullName),
                EntryType.CreditCard => HasAny(credential.CardholderName, credential.CardType),
                EntryType.BankAccount => HasAny(credential.BankName, credential.BankAccountType),
                EntryType.TotpGenerator => HasAny(credential.TotpIssuer, credential.TotpAccountName, credential.Username),
                EntryType.PinCode => HasAny(credential.PinIssuer, credential.PinLabel),
                _ => HasAny(credential.Username, credential.Url)
            };
        }

        private static bool HasAny(params string?[] values)
            => values.Any(v => !string.IsNullOrWhiteSpace(v));

        private void SmartSelect()
        {
            foreach (var group in Groups)
            {
                foreach (var entry in group.Entries)
                {
                    entry.IsSelected = false;
                }

                if (group.IsBlocked)
                {
                    continue;
                }

                var best = group.Entries
                    .OrderByDescending(e => e.InformationScore)
                    .ThenByDescending(e => e.Credential.LastUpdatedUtc)
                    .ThenBy(e => e.Credential.Id, StringComparer.Ordinal)
                    .First();

                foreach (var entry in group.Entries)
                {
                    entry.IsSelected = !ReferenceEquals(entry, best);
                    entry.SelectionReason = entry.IsSelected
                        ? SmartDeleteReason(entry, best)
                        : "Kept by smart selection.";
                }
            }

            RecalculateSelection();
            ReviewStatus = "Smart selection chose older or less-detailed duplicate items. Review and override before sending anything to the secure bin.";
        }

        private static string SmartDeleteReason(DuplicateEntryItem entry, DuplicateEntryItem best)
        {
            if (entry.InformationScore < best.InformationScore)
            {
                return $"Selected: less metadata ({entry.InformationScore} vs {best.InformationScore}).";
            }

            if (entry.Credential.LastUpdatedUtc < best.Credential.LastUpdatedUtc)
            {
                return "Selected: older last update timestamp.";
            }

            return "Selected: duplicate of the retained item.";
        }

        private void ToggleAll(bool value)
        {
            foreach (var group in Groups.Where(g => !g.IsBlocked))
            {
                var keep = value ? group.Entries.OrderByDescending(e => e.InformationScore).ThenByDescending(e => e.Credential.LastUpdatedUtc).First() : null;
                foreach (var entry in group.Entries)
                {
                    entry.IsSelected = value && !ReferenceEquals(entry, keep);
                    entry.SelectionReason = entry.IsSelected ? "Selected manually." : string.Empty;
                }
            }

            RecalculateSelection();
            ReviewStatus = value
                ? "All actionable groups selected with one retained item per group."
                : string.Empty;
        }

        private void DeleteSelected()
        {
            var selected = Groups.SelectMany(g => g.Entries).Where(e => e.IsSelected).ToList();
            if (selected.Count == 0)
            {
                ReviewStatus = "No duplicate items selected.";
                return;
            }

            var validationError = ValidateDeletionSelection(selected);
            if (!string.IsNullOrEmpty(validationError))
            {
                ReviewStatus = validationError;
                return;
            }

            DeleteRequested?.Invoke(selected.Select(e => e.Credential).ToList());
        }

        private string? ValidateDeletionSelection(IReadOnlyCollection<DuplicateEntryItem> selected)
        {
            foreach (var group in Groups)
            {
                var selectedInGroup = group.Entries.Count(e => e.IsSelected);
                if (selectedInGroup == 0)
                {
                    continue;
                }

                if (group.IsBlocked)
                {
                    return $"Deletion blocked for {group.DisplayName}: {group.BlockReason}";
                }

                if (selectedInGroup >= group.Entries.Count)
                {
                    return $"Deletion blocked for {group.DisplayName}: at least one item must remain.";
                }

                if (group.Entries.Where(e => e.IsSelected).Any(e => ValidateCandidate(e.Credential).Count > 0))
                {
                    return $"Deletion blocked for {group.DisplayName}: selected item metadata is incomplete.";
                }
            }

            var selectedIds = selected.Select(e => e.Credential.Id).ToList();
            if (selectedIds.Count != selectedIds.Distinct(StringComparer.Ordinal).Count())
            {
                return "Deletion blocked: selected items contain non-unique ids.";
            }

            return null;
        }

        private void OnEntrySelectionChanged()
        {
            RecalculateSelection();
        }

        private void RecalculateSelection()
        {
            SelectedCount = Groups.SelectMany(g => g.Entries).Count(e => e.IsSelected);
        }

        private sealed record DuplicateCandidateKey(
            EntryType EntryType,
            string Title,
            string Username,
            string Url,
            string TypeSpecificLocator)
        {
            public string Display => $"{EntryType} | {Title} | {Username} | {Url} | {TypeSpecificLocator}";

            public static DuplicateCandidateKey From(Credential credential)
            {
                return new DuplicateCandidateKey(
                    credential.EntryType,
                    Normalize(credential.Title),
                    Normalize(credential.Username),
                    Normalize(credential.Url),
                    Normalize(BuildTypeSpecificLocator(credential)));
            }

            private static string BuildTypeSpecificLocator(Credential credential)
            {
                return credential.EntryType switch
                {
                    EntryType.WiFi => First(credential.WiFiSSID, credential.WiFiBSSID),
                    EntryType.Identity => First(credential.IdDocumentType, credential.IdNumber, credential.IdCardNumber),
                    EntryType.ApiKey => First(credential.ApiEndpoint, credential.ApiEnvironment, credential.ApiDocumentationUrl),
                    EntryType.Contact => First(credential.ContactEmail, credential.ContactPhone, credential.ContactFullName),
                    EntryType.CreditCard => First(credential.CardholderName, credential.CardType),
                    EntryType.BankAccount => First(credential.BankName, credential.BankAccountType),
                    EntryType.TotpGenerator => First(credential.TotpIssuer, credential.TotpAccountName),
                    EntryType.PinCode => First(credential.PinIssuer, credential.PinLabel),
                    _ => string.Empty
                };
            }

            private static string First(params string?[] values)
                => string.Join("|", values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()));

            private static string Normalize(string? value)
                => (value ?? string.Empty).Trim().ToUpperInvariant();
        }
    }

    public sealed class DuplicateGroupItem : ReactiveObject
    {
        private DuplicateGroupItem(string key, List<DuplicateEntryItem> entries)
        {
            Key = key;
            foreach (var entry in entries)
            {
                Entries.Add(entry);
            }

            var first = Entries.First();
            DisplayName = $"{first.Title} / {first.Username}";
            KeyMetadata = key;

            BlockReason = DetermineBlockReason(Entries);
            IsBlocked = !string.IsNullOrEmpty(BlockReason);
        }

        public string Key { get; }
        public string DisplayName { get; }
        public string KeyMetadata { get; }
        public bool IsBlocked { get; }
        public string BlockReason { get; }
        public ObservableCollection<DuplicateEntryItem> Entries { get; } = new();

        public static DuplicateGroupItem Create(object key, List<DuplicateEntryItem> entries)
        {
            if (entries == null || entries.Count < 2)
            {
                throw new ArgumentException("Duplicate groups require at least two entries.", nameof(entries));
            }

            var keyDisplay = key?.ToString() ?? string.Empty;
            if (key is not null && key.GetType().GetProperty("Display")?.GetValue(key) is string display)
            {
                keyDisplay = display;
            }

            return new DuplicateGroupItem(keyDisplay, entries);
        }

        private static string DetermineBlockReason(IEnumerable<DuplicateEntryItem> entries)
        {
            var ordered = entries
                .OrderByDescending(e => e.InformationScore)
                .ThenByDescending(e => e.Credential.LastUpdatedUtc)
                .ToList();

            var first = ordered[0];
            var second = ordered[1];
            if (first.InformationScore == second.InformationScore &&
                first.Credential.LastUpdatedUtc == second.Credential.LastUpdatedUtc)
            {
                return DuplicateScanViewModel.AmbiguousSmartSelectionReason;
            }

            return string.Empty;
        }
    }

    public sealed class DuplicateEntryItem : ReactiveObject
    {
        private readonly Action _selectionChanged;
        private bool _isSelected;
        private string _selectionReason = string.Empty;

        public DuplicateEntryItem(Credential credential, Action selectionChanged)
        {
            Credential = credential ?? throw new ArgumentNullException(nameof(credential));
            _selectionChanged = selectionChanged;
        }

        public Credential Credential { get; }
        public string Id => Credential.Id;
        public string Title => EmptyAsPlaceholder(Credential.Title);
        public string Username => EmptyAsPlaceholder(Credential.Username);
        public string Url => EmptyAsPlaceholder(Credential.Url);
        public string Category => EmptyAsPlaceholder(Credential.Category);
        public string EntryType => Credential.EntryType.ToString();
        public string CreatedDisplay => Credential.CreatedUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
        public string LastUpdatedDisplay => Credential.LastUpdatedUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
        public string InformationScoreDisplay => InformationScore.ToString();
        public int InformationScore => CalculateInformationScore(Credential);
        public string MetadataSummary => $"Type {EntryType} | Info {InformationScore} | Created {CreatedDisplay} | Updated {LastUpdatedDisplay} | Id {Id}";
        public string Notes => string.IsNullOrWhiteSpace(Credential.Notes) ? "(no notes)" : Credential.Notes.Length > 80 ? Credential.Notes.Substring(0, 77) + "..." : Credential.Notes;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _isSelected, value))
                {
                    _selectionChanged();
                }
            }
        }

        public string SelectionReason
        {
            get => _selectionReason;
            set => this.RaiseAndSetIfChanged(ref _selectionReason, value);
        }

        public static int CalculateInformationScore(Credential credential)
        {
            var score = 0;
            Count(credential.Title);
            Count(credential.Username);
            Count(credential.Url);
            Count(credential.Notes);
            Count(credential.Category);
            Count(credential.Icon);
            Count(credential.IconColor);
            Count(credential.AutoTypeSequence);
            if (credential.IsFavorite) score++;
            if (credential.IsPasskey) score++;
            if (credential.ExpiryUtc.HasValue) score++;
            if (credential.Tags?.Count > 0) score += Math.Min(credential.Tags.Count, 5);
            if (credential.CustomFields?.Count > 0) score += Math.Min(credential.CustomFields.Count, 5);

            Count(credential.WiFiSSID);
            Count(credential.WiFiSecurityType);
            Count(credential.WiFiBSSID);
            Count(credential.IdDocumentType);
            Count(credential.IdNumber);
            Count(credential.IdCardNumber);
            Count(credential.IdIssuingCountry);
            Count(credential.IdIssuingState);
            if (credential.IdIssueDate.HasValue) score++;
            if (credential.IdExpiryDate.HasValue) score++;
            Count(credential.ApiEndpoint);
            Count(credential.ApiEnvironment);
            Count(credential.ApiDocumentationUrl);
            Count(credential.ContactFullName);
            Count(credential.ContactEmail);
            Count(credential.ContactPhone);
            Count(credential.ContactAddress);
            Count(credential.ContactCompany);
            Count(credential.ContactJobTitle);
            Count(credential.CardholderName);
            Count(credential.CardType);
            Count(credential.CardExpiryMonth);
            Count(credential.CardExpiryYear);
            Count(credential.CardBillingAddress);
            Count(credential.BankName);
            Count(credential.BankAccountType);
            Count(credential.BankBranchCode);
            Count(credential.BankBranchAddress);
            Count(credential.TotpIssuer);
            Count(credential.TotpAccountName);
            Count(credential.TotpAlgorithm);
            Count(credential.PinLabel);
            Count(credential.PinCategory);
            Count(credential.PinIssuer);

            return score;

            void Count(string? value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    score++;
                }
            }
        }

        private static string EmptyAsPlaceholder(string? value)
            => string.IsNullOrWhiteSpace(value) ? "(missing)" : value.Trim();
    }

    public sealed class DuplicateIssueItem
    {
        public DuplicateIssueItem(Credential? credential, string reason)
        {
            Title = string.IsNullOrWhiteSpace(credential?.Title) ? "(missing title)" : credential!.Title;
            Id = string.IsNullOrWhiteSpace(credential?.Id) ? "(missing id)" : credential!.Id;
            Reason = reason;
        }

        public string Title { get; }
        public string Id { get; }
        public string Reason { get; }
    }
}
