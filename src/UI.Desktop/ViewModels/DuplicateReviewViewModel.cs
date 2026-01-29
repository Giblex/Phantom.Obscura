using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Avalonia.Controls;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the duplicate credential review dialog that allows users to resolve conflicts
    /// when importing credentials that match existing entries by username, URL, or title.
    /// </summary>
    public class DuplicateReviewViewModel : ReactiveObject
    {
        private Window? _ownerWindow;
        private readonly List<DuplicateInfo> _duplicates;

        public ObservableCollection<DuplicateItemViewModel> DuplicateItems { get; }

        public string DuplicateSummary =>
            $"Found {_duplicates.Count} duplicate credential(s). Review each match and choose which version to keep.";

        public ICommand KeepExistingCommand { get; }
        public ICommand KeepNewCommand { get; }
        public ICommand KeepBothCommand { get; }
        public ICommand KeepAllExistingCommand { get; }
        public ICommand KeepAllNewCommand { get; }
        public ICommand ApplyCommand { get; }
        public ICommand CancelCommand { get; }

        public bool DialogResult { get; private set; }
        public Dictionary<DuplicateInfo, DuplicateChoice> UserChoices { get; }

        public DuplicateReviewViewModel(List<DuplicateInfo> duplicates)
        {
            _duplicates = duplicates ?? new List<DuplicateInfo>();
            UserChoices = new Dictionary<DuplicateInfo, DuplicateChoice>();

            DuplicateItems = new ObservableCollection<DuplicateItemViewModel>(
                _duplicates.Select(d => new DuplicateItemViewModel(d))
            );

            KeepExistingCommand = ReactiveCommand.Create<DuplicateItemViewModel>(KeepExisting);
            KeepNewCommand = ReactiveCommand.Create<DuplicateItemViewModel>(KeepNew);
            KeepBothCommand = ReactiveCommand.Create<DuplicateItemViewModel>(KeepBoth);
            KeepAllExistingCommand = ReactiveCommand.Create(KeepAllExisting);
            KeepAllNewCommand = ReactiveCommand.Create(KeepAllNew);
            ApplyCommand = ReactiveCommand.Create(Apply);
            CancelCommand = ReactiveCommand.Create(Cancel);
        }

        public void SetOwnerWindow(Window window) => _ownerWindow = window;

        private void KeepExisting(DuplicateItemViewModel item)
        {
            UserChoices[item.DuplicateInfo] = DuplicateChoice.KeepExisting;
            item.Choice = DuplicateChoice.KeepExisting;
        }

        private void KeepNew(DuplicateItemViewModel item)
        {
            UserChoices[item.DuplicateInfo] = DuplicateChoice.KeepNew;
            item.Choice = DuplicateChoice.KeepNew;
        }

        private void KeepBoth(DuplicateItemViewModel item)
        {
            UserChoices[item.DuplicateInfo] = DuplicateChoice.KeepBoth;
            item.Choice = DuplicateChoice.KeepBoth;
        }

        private void KeepAllExisting()
        {
            foreach (var item in DuplicateItems)
            {
                UserChoices[item.DuplicateInfo] = DuplicateChoice.KeepExisting;
                item.Choice = DuplicateChoice.KeepExisting;
            }
        }

        private void KeepAllNew()
        {
            foreach (var item in DuplicateItems)
            {
                UserChoices[item.DuplicateInfo] = DuplicateChoice.KeepNew;
                item.Choice = DuplicateChoice.KeepNew;
            }
        }

        private void Apply()
        {
            // Set default choices for any duplicates without explicit user choice
            foreach (var dup in _duplicates)
            {
                if (!UserChoices.ContainsKey(dup))
                {
                    // Smart default: keep stronger/newer password
                    UserChoices[dup] = DetermineSmartChoice(dup);
                }
            }

            DialogResult = true;
            _ownerWindow?.Close();
        }

        private void Cancel()
        {
            DialogResult = false;
            _ownerWindow?.Close();
        }

        private DuplicateChoice DetermineSmartChoice(DuplicateInfo duplicate)
        {
            // Same logic as ImportExportService.ApplyDuplicateResolution
            var newPassword = duplicate.NewCredential?.Password ?? string.Empty;
            var existingPassword = duplicate.ExistingCredential?.Password ?? string.Empty;

            // If passwords are identical, keep existing
            if (newPassword == existingPassword)
                return DuplicateChoice.KeepExisting;

            // Keep the stronger password
            if (newPassword.Length > existingPassword.Length)
                return DuplicateChoice.KeepNew;

            return DuplicateChoice.KeepExisting;
        }
    }

    /// <summary>
    /// Represents a single duplicate credential pair with comparison details for the review UI.
    /// </summary>
    public class DuplicateItemViewModel : ReactiveObject
    {
        private DuplicateChoice _choice = DuplicateChoice.None;

        public DuplicateInfo DuplicateInfo { get; }

        public string MatchTypeIcon => DuplicateInfo.MatchType switch
        {
            DuplicateMatchType.ExactMatch => "🎯",
            DuplicateMatchType.PasswordMatch => "🔑",
            DuplicateMatchType.UsernameUrlMatch => "🌐",
            _ => "❓"
        };

        public string MatchTypeText => DuplicateInfo.MatchType switch
        {
            DuplicateMatchType.ExactMatch => "Exact Match (Same username, password, and URL)",
            DuplicateMatchType.PasswordMatch => "Password Match (Same username and password)",
            DuplicateMatchType.UsernameUrlMatch => "Username/URL Match (Same username and URL)",
            _ => "Unknown Match"
        };

        public string ExistingTitle => DuplicateInfo.ExistingCredential?.Title ?? "Untitled";
        public string ExistingUsername => DuplicateInfo.ExistingCredential?.Username ?? "(no username)";
        public string ExistingPasswordPreview => MaskPassword(DuplicateInfo.ExistingCredential?.Password);
        public string ExistingUrl => DuplicateInfo.ExistingCredential?.Url ?? "(no URL)";
        public string ExistingModified =>
            $"Modified: {DuplicateInfo.ExistingCredential?.LastUpdatedUtc.ToString("MMM dd, yyyy") ?? "Unknown"}";

        public string NewTitle => DuplicateInfo.NewCredential?.Title ?? "Untitled";
        public string NewUsername => DuplicateInfo.NewCredential?.Username ?? "(no username)";
        public string NewPasswordPreview => MaskPassword(DuplicateInfo.NewCredential?.Password);
        public string NewUrl => DuplicateInfo.NewCredential?.Url ?? "(no URL)";

        public DuplicateChoice Choice
        {
            get => _choice;
            set
            {
                this.RaiseAndSetIfChanged(ref _choice, value);
                this.RaisePropertyChanged(nameof(ChoiceIndicator));
            }
        }

        public string ChoiceIndicator => Choice switch
        {
            DuplicateChoice.KeepExisting => "✅ Keeping Existing",
            DuplicateChoice.KeepNew => "✅ Keeping New",
            DuplicateChoice.KeepBoth => "✅ Keeping Both",
            _ => "⚠️ No choice made"
        };

        public DuplicateItemViewModel(DuplicateInfo duplicateInfo)
        {
            DuplicateInfo = duplicateInfo;
        }

        private string MaskPassword(string? password)
        {
            if (string.IsNullOrEmpty(password)) return "(no password)";
            if (password.Length <= 4) return new string('●', password.Length);
            return password.Substring(0, 2) + new string('●', Math.Min(8, password.Length - 2)) + "... (" + password.Length + " chars)";
        }
    }

    public enum DuplicateChoice
    {
        None,
        KeepExisting,
        KeepNew,
        KeepBoth
    }
}
