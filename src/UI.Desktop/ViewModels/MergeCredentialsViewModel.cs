using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// ViewModel for individual merge item in the merge window.
    /// </summary>
    public class MergeItemViewModel : INotifyPropertyChanged
    {
        private bool _keepNew;
        private bool _keepExisting;

        public event PropertyChangedEventHandler? PropertyChanged;

        public int Index { get; set; }
        public Credential NewCredential { get; set; } = new();
        public Credential ExistingCredential { get; set; } = new();
        public DuplicateMatchType MatchType { get; set; }
        public bool KeepBoth { get; set; }

        public bool KeepNew
        {
            get => _keepNew;
            set
            {
                if (_keepNew != value)
                {
                    _keepNew = value;
                    if (value && _keepExisting)
                    {
                        _keepExisting = false;
                        OnPropertyChanged(nameof(KeepExisting));
                    }
                    OnPropertyChanged();
                }
            }
        }

        public bool KeepExisting
        {
            get => _keepExisting;
            set
            {
                if (_keepExisting != value)
                {
                    _keepExisting = value;
                    if (value && _keepNew)
                    {
                        _keepNew = false;
                        OnPropertyChanged(nameof(KeepNew));
                    }
                    OnPropertyChanged();
                }
            }
        }

        public string MatchTypeText => MatchType switch
        {
            DuplicateMatchType.ExactMatch => "🎯 Exact Match (Title, Username, URL)",
            DuplicateMatchType.PasswordMatch => "🔑 Same Password",
            DuplicateMatchType.UsernameUrlMatch => "👤 Same Username & URL",
            _ => "❓ Unknown"
        };

        public string NewPasswordPreview => MaskPassword(NewCredential.Password);
        public string ExistingPasswordPreview => MaskPassword(ExistingCredential.Password);

        public bool HasNewNotes => !string.IsNullOrWhiteSpace(NewCredential.Notes);
        public bool HasExistingNotes => !string.IsNullOrWhiteSpace(ExistingCredential.Notes);

        public bool HasNewTags => NewCredential.Tags?.Any() == true;
        public bool HasExistingTags => ExistingCredential.Tags?.Any() == true;

        public string NewTagsText => NewCredential.Tags != null ? string.Join(", ", NewCredential.Tags) : "";
        public string ExistingTagsText => ExistingCredential.Tags != null ? string.Join(", ", ExistingCredential.Tags) : "";

        public ICommand KeepBothCommand { get; }

        public MergeItemViewModel()
        {
            KeepBothCommand = new RelayCommand(ExecuteKeepBoth);
        }

        public void SelectNew()
        {
            KeepNew = true;
        }

        public void SelectExisting()
        {
            KeepExisting = true;
        }

        private void ExecuteKeepBoth()
        {
            KeepBoth = true;
            KeepNew = false;
            KeepExisting = false;
            OnPropertyChanged(nameof(KeepBoth));
        }

        private string MaskPassword(string? password)
        {
            if (string.IsNullOrEmpty(password))
                return "(empty)";

            return new string('●', Math.Min(password.Length, 20));
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// ViewModel for the Merge Credentials Window.
    /// Displays duplicates side-by-side and allows user to select which to keep.
    /// Default: Keep most recently created credential.
    /// </summary>
    public class MergeCredentialsViewModel : INotifyPropertyChanged
    {
        private readonly List<DuplicateInfo> _originalDuplicates;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<MergeItemViewModel> MergeItems { get; } = new();

        public int TotalDuplicates => MergeItems.Count;
        public int SelectedCount => MergeItems.Count(m => m.KeepNew || m.KeepExisting || m.KeepBoth);

        public bool IsMerged { get; private set; }
        public List<DuplicateInfo> ResolvedDuplicates { get; private set; } = new();

        public ICommand SelectAllNewCommand { get; }
        public ICommand SelectAllExistingCommand { get; }
        public ICommand MergeCommand { get; }
        public ICommand CancelCommand { get; }

        public MergeCredentialsViewModel(List<DuplicateInfo> duplicates)
        {
            _originalDuplicates = duplicates;

            SelectAllNewCommand = new RelayCommand(ExecuteSelectAllNew);
            SelectAllExistingCommand = new RelayCommand(ExecuteSelectAllExisting);
            MergeCommand = new RelayCommand(ExecuteMerge, CanExecuteMerge);
            CancelCommand = new RelayCommand(ExecuteCancel);

            InitializeMergeItems();
        }

        private void InitializeMergeItems()
        {
            int index = 1;
            foreach (var duplicate in _originalDuplicates)
            {
                var item = new MergeItemViewModel
                {
                    Index = index++,
                    NewCredential = duplicate.NewCredential,
                    ExistingCredential = duplicate.ExistingCredential ?? new Credential(),
                    MatchType = duplicate.MatchType
                };

                // Default: Select most recently created
                var newCreated = duplicate.NewCredential.CreatedUtc;
                var existingCreated = duplicate.ExistingCredential?.CreatedUtc ?? DateTimeOffset.MinValue;

                if (newCreated >= existingCreated)
                {
                    item.KeepNew = true;
                }
                else
                {
                    item.KeepExisting = true;
                }

                // Subscribe to property changes to update selected count
                item.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(MergeItemViewModel.KeepNew) ||
                        e.PropertyName == nameof(MergeItemViewModel.KeepExisting) ||
                        e.PropertyName == nameof(MergeItemViewModel.KeepBoth))
                    {
                        OnPropertyChanged(nameof(SelectedCount));
                    }
                };

                MergeItems.Add(item);
            }

            OnPropertyChanged(nameof(TotalDuplicates));
            OnPropertyChanged(nameof(SelectedCount));
        }

        private void ExecuteSelectAllNew()
        {
            foreach (var item in MergeItems)
            {
                item.KeepNew = true;
                item.KeepBoth = false;
            }
        }

        private void ExecuteSelectAllExisting()
        {
            foreach (var item in MergeItems)
            {
                item.KeepExisting = true;
                item.KeepBoth = false;
            }
        }

        private bool CanExecuteMerge()
        {
            return MergeItems.All(m => m.KeepNew || m.KeepExisting || m.KeepBoth);
        }

        private void ExecuteMerge()
        {
            ResolvedDuplicates.Clear();

            for (int i = 0; i < MergeItems.Count; i++)
            {
                var item = MergeItems[i];
                var originalDuplicate = _originalDuplicates[i];

                if (item.KeepBoth)
                {
                    // Keep both - don't add to resolved (both will be imported)
                    originalDuplicate.KeepNew = true;
                    // ExistingCredential stays in vault
                }
                else if (item.KeepNew)
                {
                    originalDuplicate.KeepNew = true;
                }
                else if (item.KeepExisting)
                {
                    originalDuplicate.KeepNew = false;
                }

                ResolvedDuplicates.Add(originalDuplicate);
            }

            IsMerged = true;
            CloseWindow();
        }

        private void ExecuteCancel()
        {
            IsMerged = false;
            CloseWindow();
        }

        private void CloseWindow()
        {
            // Window will be closed by caller
            OnPropertyChanged(nameof(IsMerged));
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Simple RelayCommand implementation for commands.
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public event EventHandler? CanExecuteChanged;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object? parameter) => _execute();

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
