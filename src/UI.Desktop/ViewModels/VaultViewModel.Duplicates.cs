using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using PhantomVault.UI.Views;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels
{

    public sealed partial class VaultViewModel
    {
        private readonly DuplicateConsolidationService _consolidationService = new();

        public ReactiveCommand<Unit, Unit> OpenDuplicateScanCommand { get; private set; } = null!;

        private void InitializeDuplicateAndSectionSupport()
        {
            OpenDuplicateScanCommand = ReactiveCommand.CreateFromTask(OpenDuplicateScanAsync);

            CredentialViewModel.LinkedEntryResolver = FindCredentialById;
            CredentialViewModel.SectionCopyHandler = (value, label) =>
                _ = CopySectionValueAsync(value, label);
            CredentialViewModel.SectionOpenLinkedHandler = SelectCredentialById;
            CredentialViewModel.SectionPersistHandler = changed => _ = SaveVaultAsync();

            AddEditCredentialViewModel.VaultEntriesProvider = () =>
                _credentials.Select(c => c.GetCredential()).ToList();
        }

        private Credential? FindCredentialById(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;

            return _credentials
                .Select(c => c.GetCredential())
                .FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));
        }

        private void SelectCredentialById(string id)
        {
            var match = _credentials.FirstOrDefault(c =>
                string.Equals(c.GetCredential().Id, id, StringComparison.Ordinal));

            if (match == null)
            {
                StatusMessage = "The linked entry is no longer in this vault.";
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                SelectedCredential = match;
                StatusMessage = $"Opened linked entry: {match.Title}";
            });
        }

        private async Task CopySectionValueAsync(string value, string label)
        {
            if (string.IsNullOrEmpty(value))
            {
                StatusMessage = "Nothing to copy";
                return;
            }

            try
            {
                var clipboard = TopLevel.GetTopLevel(_ownerWindow)?.Clipboard;
                if (clipboard == null)
                {
                    StatusMessage = "Clipboard unavailable";
                    return;
                }

                await clipboard.SetTextAsync(value);
                _clipboardGuard?.RegisterCopy(label);
                StatusMessage = $"Copied: {label}";
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "[Duplicates] Failed to copy a credential value.");
                StatusMessage = "The value could not be copied. Confirm clipboard access is allowed and try again.";
            }
        }

        private async Task OpenDuplicateScanAsync()
        {
            var credentials = _credentials.Select(c => c.GetCredential()).ToList();

            if (credentials.Count == 0)
            {
                await _dialogService.ShowInfoAsync(
                    "Duplicate Scanner",
                    "There are no entries in this vault to scan.",
                    _ownerWindow);
                return;
            }

            var scanViewModel = new DuplicateScanViewModel(credentials);
            var window = new DuplicateScanWindow(scanViewModel);

            scanViewModel.DeleteRequested += duplicates =>
                _ = HandleDuplicateDeletionAsync(duplicates, window);

            scanViewModel.ConsolidateRequested += plans =>
                _ = HandleDuplicateConsolidationAsync(plans, window);

            if (_ownerWindow != null)
            {
                await window.ShowDialog(_ownerWindow);
            }
            else
            {
                window.Show();
            }
        }

        private async Task HandleDuplicateDeletionAsync(List<Credential> duplicates, Window scanWindow)
        {
            if (duplicates == null || duplicates.Count == 0)
                return;

            var confirmed = await _dialogService.ShowConfirmationAsync(
                "Remove Duplicates",
                $"Move {duplicates.Count} duplicate entr{(duplicates.Count == 1 ? "y" : "ies")} to the Secure Rubbish Bin?",
                scanWindow);

            if (!confirmed)
                return;

            var removed = RemoveCredentials(duplicates);

            Dispatcher.UIThread.Post(() =>
            {
                UpdateCategoryCounts();
                ApplyFilters();
                StatusMessage = $"Moved {removed} duplicate entr{(removed == 1 ? "y" : "ies")} to the Secure Rubbish Bin";
                scanWindow.Close();
            });

            await SaveVaultAsync();
        }

        private async Task HandleDuplicateConsolidationAsync(List<ConsolidationPlan> plans, Window scanWindow)
        {
            if (plans == null || plans.Count == 0)
                return;

            var results = new List<ConsolidationResult>();
            foreach (var plan in plans)
            {
                results.Add(_consolidationService.Consolidate(plan.AllMembers, plan.Primary.Id));
            }

            var absorbedCount = results.Sum(r => r.Absorbed.Count);
            var conflicts = results.SelectMany(r => r.Conflicts).ToList();

            var message =
                $"Consolidate {absorbedCount + results.Count} entr{(absorbedCount + results.Count == 1 ? "y" : "ies")} into {results.Count} " +
                $"entr{(results.Count == 1 ? "y" : "ies")}?\n\n" +
                "Every field, note, tag, custom field and section from the absorbed copies is folded into the retained entry. " +
                $"The {absorbedCount} absorbed cop{(absorbedCount == 1 ? "y" : "ies")} then move to the Secure Rubbish Bin.";

            if (conflicts.Count > 0)
            {
                var preview = string.Join("\n", conflicts.Take(8).Select(c => "  • " + c.Describe()));
                message += $"\n\n{conflicts.Count} field conflict(s) will keep the retained entry's value:\n{preview}";

                if (conflicts.Count > 8)
                    message += $"\n  ...and {conflicts.Count - 8} more.";

                var preserved = results.Sum(r => r.Consolidated.Sections
                    .Count(s => s.Label.EndsWith("(from merged copy)", StringComparison.Ordinal)));

                if (preserved > 0)
                {
                    message += $"\n\nNothing is lost: {preserved} conflicting secret(s) are kept on the retained entry " +
                               "as hidden sections labelled \"(from merged copy)\".";
                }
            }

            var confirmed = await _dialogService.ShowConfirmationAsync("Consolidate Duplicates", message, scanWindow);
            if (!confirmed)
                return;

            foreach (var result in results)
            {
                ApplyConsolidation(result);
            }

            var removed = RemoveCredentials(results.SelectMany(r => r.Absorbed).ToList());

            Dispatcher.UIThread.Post(() =>
            {
                UpdateCategoryCounts();
                ApplyFilters();
                StatusMessage = conflicts.Count > 0
                    ? $"Consolidated into {results.Count} entr{(results.Count == 1 ? "y" : "ies")}; {removed} absorbed cop{(removed == 1 ? "y" : "ies")} binned, {conflicts.Count} conflict(s) resolved in favour of the retained entry"
                    : $"Consolidated into {results.Count} entr{(results.Count == 1 ? "y" : "ies")}; {removed} absorbed cop{(removed == 1 ? "y" : "ies")} binned";
                scanWindow.Close();
            });

            await SaveVaultAsync();
        }

        private void ApplyConsolidation(ConsolidationResult result)
        {
            var target = _credentials.FirstOrDefault(c =>
                string.Equals(c.GetCredential().Id, result.Consolidated.Id, StringComparison.Ordinal));

            if (target == null)
                return;

            var live = target.GetCredential();
            CopyConsolidatedInto(result.Consolidated, live);

            Dispatcher.UIThread.Post(() =>
            {
                target.Refresh();
            });
        }

        private static void CopyConsolidatedInto(Credential source, Credential destination)
        {
            // Keep the live object identity the UI is bound to, but take every value from
            // the consolidated result. Credential.CopyValuesFrom is the single place that
            // knows the full field list.
            destination.CopyValuesFrom(source);
        }

        private int RemoveCredentials(List<Credential> toRemove)
        {
            var removed = 0;

            foreach (var credential in toRemove)
            {
                var match = _credentials.FirstOrDefault(c =>
                    string.Equals(c.GetCredential().Id, credential.Id, StringComparison.Ordinal));

                if (match == null)
                    continue;

                try
                {
                    _secureTrashService.MoveToTrash(match.GetCredential());
                }
                catch
                {

                }

                Dispatcher.UIThread.Post(() => _credentials.Remove(match));
                removed++;
            }

            return removed;
        }
    }
}
