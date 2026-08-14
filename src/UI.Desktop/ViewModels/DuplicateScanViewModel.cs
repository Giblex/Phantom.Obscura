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
        public ObservableCollection<DuplicateSiteGroup> SiteGroups { get; } = new();
        public ObservableCollection<DuplicateIssueItem> BlockedItems { get; } = new();

        /// <summary>
        /// Organises the flat group list into one card per website, the way a password
        /// manager's site list reads. Entries with no website (PINs, bank accounts) fall
        /// into a trailing "Other entries" card rather than being hidden.
        /// </summary>
        private void BuildSiteGroups()
        {
            SiteGroups.Clear();

            var bySite = Groups
                .GroupBy(g => g.SiteFamily, StringComparer.Ordinal)
                .Select(g => new DuplicateSiteGroup(
                    g.Key,
                    string.IsNullOrEmpty(g.Key) ? "Other entries" : g.First().SiteDisplayName,
                    g.OrderBy(x => x.Strength).ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToList()))

                .OrderBy(s => string.IsNullOrEmpty(s.SiteFamily) ? 1 : 0)
                .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase);

            foreach (var site in bySite)
            {
                SiteGroups.Add(site);
            }

            this.RaisePropertyChanged(nameof(SiteCount));
            this.RaisePropertyChanged(nameof(SiteSummary));
        }

        public int SiteCount => SiteGroups.Count(s => s.HasSite);

        public string SiteSummary => SiteCount == 0
            ? string.Empty
            : SiteCount == 1
                ? "Across 1 website"
                : $"Across {SiteCount} websites";

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
        public ICommand ConsolidateSelectedCommand { get; }
        public ICommand CancelCommand { get; }

        public event Action<List<Credential>>? DeleteRequested;
        public event Action<List<ConsolidationPlan>>? ConsolidateRequested;
        public event Action? CloseRequested;

        public DuplicateScanViewModel(IEnumerable<Credential> credentials)
        {
            Scan(credentials ?? throw new ArgumentNullException(nameof(credentials)));

            SmartSelectCommand = ReactiveCommand.Create(SmartSelect);
            SelectAllCommand = ReactiveCommand.Create(() => ToggleAll(true));
            ClearSelectionCommand = ReactiveCommand.Create(() => ToggleAll(false));
            DeleteSelectedCommand = ReactiveCommand.Create(DeleteSelected);
            ConsolidateSelectedCommand = ReactiveCommand.Create(ConsolidateSelected);
            CancelCommand = ReactiveCommand.Create(() => CloseRequested?.Invoke());

            RecalculateSelection();
        }

        private void Scan(IEnumerable<Credential> credentials)
        {
            var credentialList = credentials.ToList();
            ScannedCredentialCount = credentialList.Count;

            var keyed = new List<(Credential Credential, DuplicateKey Key)>();
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

                var key = DuplicateMatchKeyBuilder.Build(credential);
                if (!key.IsUsable)
                {
                    BlockedItems.Add(new DuplicateIssueItem(credential, MissingLocatorReason));
                    continue;
                }

                keyed.Add((credential, key));
            }

            var grouped = keyed
                .GroupBy(k => k.Key)
                .Where(g => g.Count() > 1)
                .Select(g => new
                {
                    g.Key,
                    Members = g.Select(k => k.Credential).ToList(),
                    Strength = DuplicateMatchKeyBuilder.DetermineStrength(g.Select(k => k.Credential).ToList())
                })

                .OrderBy(g => g.Strength)
                .ThenBy(g => g.Key.Display, StringComparer.Ordinal);

            foreach (var group in grouped)
            {
                var candidates = group.Members
                    .Select(c => new DuplicateEntryItem(c, OnEntrySelectionChanged))
                    .OrderBy(e => e.Credential.LastUpdatedUtc)
                    .ThenBy(e => e.Credential.Id, StringComparer.Ordinal)
                    .ToList();

                Groups.Add(DuplicateGroupItem.Create(group.Key, candidates, group.Strength));
            }

            BuildSiteGroups();

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
            => DuplicateMatchKeyBuilder.Build(credential).Display;

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

                if (group.NeedsReview)
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

            var skipped = Groups.Count(g => !g.IsBlocked && g.NeedsReview);
            ReviewStatus = skipped == 0
                ? "Smart selection chose older or less-detailed duplicate items. Review and override before sending anything to the secure bin."
                : $"Smart selection chose older or less-detailed duplicate items in the exact and strong matches. {skipped} likely-match group(s) were left untouched for you to decide on.";
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

        private void ConsolidateSelected()
        {
            var selected = Groups.SelectMany(g => g.Entries).Where(e => e.IsSelected).ToList();
            if (selected.Count == 0)
            {
                ReviewStatus = "Select the duplicate items you want folded into the retained entry.";
                return;
            }

            var validationError = ValidateDeletionSelection(selected);
            if (!string.IsNullOrEmpty(validationError))
            {
                ReviewStatus = validationError;
                return;
            }

            var plans = new List<ConsolidationPlan>();

            foreach (var group in Groups)
            {
                if (group.IsBlocked)
                {
                    continue;
                }

                var absorbed = group.Entries.Where(e => e.IsSelected).ToList();
                if (absorbed.Count == 0)
                {
                    continue;
                }

                var keepers = group.Entries.Where(e => !e.IsSelected).ToList();
                if (keepers.Count == 0)
                {

                    ReviewStatus = $"Consolidation blocked for {group.DisplayName}: at least one item must remain unselected to receive the merged data.";
                    return;
                }

                var primary = keepers
                    .OrderByDescending(e => e.InformationScore)
                    .ThenByDescending(e => e.Credential.LastUpdatedUtc)
                    .ThenBy(e => e.Credential.Id, StringComparer.Ordinal)
                    .First();

                plans.Add(new ConsolidationPlan(
                    group.DisplayName,
                    primary.Credential,
                    absorbed.Select(e => e.Credential).ToList()));
            }

            if (plans.Count == 0)
            {
                ReviewStatus = "Nothing to consolidate in the current selection.";
                return;
            }

            ConsolidateRequested?.Invoke(plans);
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

    }

    public sealed class DuplicateGroupItem : ReactiveObject
    {
        private DuplicateGroupItem(
            string key,
            List<DuplicateEntryItem> entries,
            DuplicateMatchStrength strength,
            string siteFamily,
            string siteDisplayName)
        {
            Key = key;
            Strength = strength;
            SiteFamily = siteFamily;
            SiteDisplayName = siteDisplayName;
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
        public DuplicateMatchStrength Strength { get; }
        public string SiteFamily { get; }
        public string SiteDisplayName { get; }
        public ObservableCollection<DuplicateEntryItem> Entries { get; } = new();

        public string AccountLabel
        {
            get
            {
                var account = Entries.FirstOrDefault()?.Username ?? string.Empty;
                return string.IsNullOrWhiteSpace(account) || account == "(missing)"
                    ? "No account name"
                    : account;
            }
        }

        public string CopiesLabel => Entries.Count == 2
            ? "2 copies"
            : $"{Entries.Count} copies";

        public string StrengthLabel => Strength switch
        {
            DuplicateMatchStrength.Exact => "EXACT",
            DuplicateMatchStrength.Strong => "STRONG",
            _ => "LIKELY"
        };

        public string StrengthDescription => DuplicateMatchKeyBuilder.DescribeStrength(Strength);

        public bool NeedsReview => Strength == DuplicateMatchStrength.Likely;

        public static DuplicateGroupItem Create(
            DuplicateKey key,
            List<DuplicateEntryItem> entries,
            DuplicateMatchStrength strength)
        {
            if (entries == null || entries.Count < 2)
            {
                throw new ArgumentException("Duplicate groups require at least two entries.", nameof(entries));
            }

            return new DuplicateGroupItem(
                key?.Display ?? string.Empty,
                entries,
                strength,
                key?.SiteFamily ?? string.Empty,
                key?.SiteDisplayName ?? string.Empty);
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

        /// <summary>
        /// Delegates to the same scorer the merge uses, so the entry the UI shows as
        /// richest is the entry consolidation would pick as primary. Keeping two copies of
        /// this logic let them drift — the UI one never counted sections.
        /// </summary>
        public static int CalculateInformationScore(Credential credential)
            => DuplicateConsolidationService.InformationScore(credential);

        private static string EmptyAsPlaceholder(string? value)
            => string.IsNullOrWhiteSpace(value) ? "(missing)" : value.Trim();
    }

    /// <summary>
    /// One website's worth of duplicate groups, so the review list is organised by site
    /// rather than presenting every group as an unrelated row.
    /// </summary>
    public sealed class DuplicateSiteGroup
    {
        public DuplicateSiteGroup(string siteFamily, string displayName, List<DuplicateGroupItem> groups)
        {
            SiteFamily = siteFamily;
            DisplayName = displayName;
            Groups = new ObservableCollection<DuplicateGroupItem>(groups);
        }

        public string SiteFamily { get; }

        public string DisplayName { get; }

        public bool HasSite => !string.IsNullOrEmpty(SiteFamily);

        public ObservableCollection<DuplicateGroupItem> Groups { get; }

        public int AccountCount => Groups.Count;

        public int DuplicateCount => Groups.Sum(g => Math.Max(0, g.Entries.Count - 1));

        public string Summary => AccountCount == 1
            ? $"1 account · {DuplicateCount} duplicate{(DuplicateCount == 1 ? string.Empty : "s")}"
            : $"{AccountCount} accounts · {DuplicateCount} duplicate{(DuplicateCount == 1 ? string.Empty : "s")}";
    }

    public sealed class ConsolidationPlan
    {
        public ConsolidationPlan(string groupName, Credential primary, List<Credential> absorbed)
        {
            GroupName = groupName;
            Primary = primary ?? throw new ArgumentNullException(nameof(primary));
            Absorbed = absorbed ?? throw new ArgumentNullException(nameof(absorbed));
        }

        public string GroupName { get; }

        public Credential Primary { get; }

        public List<Credential> Absorbed { get; }

        public List<Credential> AllMembers
        {
            get
            {
                var all = new List<Credential> { Primary };
                all.AddRange(Absorbed);
                return all;
            }
        }
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

