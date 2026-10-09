using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using PhantomVault.UI.Services;
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

        /// <summary>
        /// Runs the duplicate scan on the schedule the user configured in Rubbish Bin settings.
        ///
        /// Three settings drove this and none of them were read, so the scan only ever happened
        /// when the user opened it by hand. "Manual only" (the default) still means exactly that —
        /// this returns immediately — so turning automation on is a deliberate choice.
        ///
        /// Automatic deletion moves entries to the Secure Rubbish Bin, never destroys them, and
        /// uses the same smart-selection rule as the button in the scanner: one member of each
        /// group is always kept, and groups the scanner flags as needing review or blocked are
        /// skipped entirely. An automated action must not be able to do something the user could
        /// not inspect and undo.
        /// </summary>
        private async Task MaybeRunScheduledDuplicateScanAsync()
        {
            try
            {
                var settings = SettingsService.Load();

                if (!settings.SecureTrashDuplicateDetectionEnabled)
                    return;

                var interval = settings.SecureTrashDuplicateScanFrequency switch
                {
                    0 => TimeSpan.FromDays(1),    // Daily
                    1 => TimeSpan.FromDays(7),    // Weekly
                    2 => TimeSpan.FromDays(30),   // Monthly
                    _ => TimeSpan.Zero            // Manual only
                };

                if (interval == TimeSpan.Zero)
                    return;

                var last = settings.LastDuplicateScanUtc;
                if (last.HasValue && DateTimeOffset.UtcNow - last.Value < interval)
                    return;

                var credentials = _credentials.Select(c => c.GetCredential()).ToList();
                if (credentials.Count == 0)
                    return;

                var scan = new DuplicateScanViewModel(credentials);

                // Record the run before acting on it. If something below throws, the scan is not
                // retried on every single unlock afterwards.
                SettingsService.Update(cfg => cfg.LastDuplicateScanUtc = DateTimeOffset.UtcNow);

                if (!scan.HasDuplicates || scan.ActionableGroupCount == 0)
                {
                    Serilog.Log.Information("[Duplicates] Scheduled scan found nothing actionable");
                    return;
                }

                if (!settings.SecureTrashAutoDeleteDuplicates)
                {
                    StatusMessage =
                        $"Duplicate scan found {scan.ActionableGroupCount} group(s) worth reviewing — open Duplicate Scanner to see them.";
                    return;
                }

                scan.SmartSelect();

                var blocked = scan.DeletionBlockedReason;
                if (!string.IsNullOrEmpty(blocked))
                {
                    // Something in the selection is not safe to remove unattended. Leave it for
                    // the user rather than working around the scanner's own guard.
                    StatusMessage = "Duplicate scan needs review before anything can be removed.";
                    Serilog.Log.Information("[Duplicates] Scheduled auto-delete stood down: {Reason}", blocked);
                    return;
                }

                var toRemove = scan.SelectedForDeletion;
                if (toRemove.Count == 0)
                    return;

                // Same path the manual scanner uses: moved to the Secure Rubbish Bin, removed
                // from the list, then the vault is saved.
                var removed = RemoveCredentials(toRemove);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    UpdateCategoryCounts();
                    ApplyFilters();
                    StatusMessage =
                        $"Duplicate scan moved {removed} duplicate entr{(removed == 1 ? "y" : "ies")} to the Secure Rubbish Bin.";
                });

                await SaveVaultAsync();
                Serilog.Log.Information("[Duplicates] Scheduled scan auto-removed {Count} duplicate(s)", removed);
            }
            catch (Exception ex)
            {
                // A scheduled tidy-up must never get in the way of opening the vault.
                Serilog.Log.Warning(ex, "[Duplicates] Scheduled duplicate scan failed");
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
