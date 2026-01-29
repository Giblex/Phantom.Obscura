using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the Add Category dialog that allows users to create new credential categories
    /// and optionally move existing credentials from other categories.
    /// </summary>
    public class AddCategoryDialogViewModel : ReactiveObject
    {
        private readonly VaultViewModel? _vaultViewModel;
        private ObservableCollection<string> _sourceCategories = new();
        private string? _selectedSourceCategory;
        private string _newCategoryName = string.Empty;
        private string _validationError = string.Empty;
        private ObservableCollection<CredentialViewModel> _sourceEntries = new();
        private ObservableCollection<CredentialViewModel> _destinationEntries = new();

        /// <summary>
        /// Gets or sets the list of existing category names available for moving credentials.
        /// </summary>
        public ObservableCollection<string> SourceCategories
        {
            get => _sourceCategories;
            set => this.RaiseAndSetIfChanged(ref _sourceCategories, value);
        }

        /// <summary>
        /// Gets or sets the currently selected source category. When set, loads credentials from that category.
        /// </summary>
        public string? SelectedSourceCategory
        {
            get => _selectedSourceCategory;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedSourceCategory, value);
                LoadSourceEntries();
            }
        }

        /// <summary>
        /// Gets or sets the name for the new category to be created.
        /// </summary>
        public string NewCategoryName
        {
            get => _newCategoryName;
            set
            {
                this.RaiseAndSetIfChanged(ref _newCategoryName, value);
                // Clear validation error when user types
                if (!string.IsNullOrWhiteSpace(value) && !string.IsNullOrEmpty(ValidationError))
                {
                    ValidationError = string.Empty;
                }
            }
        }

        /// <summary>
        /// Gets or sets the validation error message displayed when category name is invalid.
        /// </summary>
        public string ValidationError
        {
            get => _validationError;
            set => this.RaiseAndSetIfChanged(ref _validationError, value);
        }

        public ObservableCollection<CredentialViewModel> SourceEntries
        {
            get => _sourceEntries;
            set => this.RaiseAndSetIfChanged(ref _sourceEntries, value);
        }

        public ObservableCollection<CredentialViewModel> DestinationEntries
        {
            get => _destinationEntries;
            set => this.RaiseAndSetIfChanged(ref _destinationEntries, value);
        }

        public string SourceColumnHeader => SelectedSourceCategory != null
            ? $"Entries in '{SelectedSourceCategory}'"
            : "Select a category first";

        public ReactiveCommand<Unit, Unit> ApplyCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }

        public event Action? DialogClosed;
        public event Action<string, CredentialViewModel[]>? CategoryCreated;

        public AddCategoryDialogViewModel(VaultViewModel? vaultViewModel = null)
        {
            _vaultViewModel = vaultViewModel;
            ApplyCommand = ReactiveCommand.CreateFromTask(ApplyAsync);
            CancelCommand = ReactiveCommand.Create(Cancel);

            this.WhenAnyValue(x => x.SelectedSourceCategory)
                .Subscribe(_ => this.RaisePropertyChanged(nameof(SourceColumnHeader)));
        }

        public void Initialize(VaultViewModel vaultViewModel, string newCategoryName)
        {
            // Get all category names except "Deleted"
            SourceCategories = new ObservableCollection<string>(
                vaultViewModel.Categories
                    .Where(c => !c.Name.Equals("Deleted", StringComparison.OrdinalIgnoreCase))
                    .Select(c => c.Name)
            );

            NewCategoryName = newCategoryName;
        }

        private void LoadSourceEntries()
        {
            SourceEntries.Clear();
            if (SelectedSourceCategory == null || _vaultViewModel == null) return;

            // Get all credentials in the selected category
            var allCredentials = _vaultViewModel.FilteredCredentials
                .Where(c => c.Group?.Equals(SelectedSourceCategory, StringComparison.OrdinalIgnoreCase) == true);

            foreach (var entry in allCredentials)
            {
                if (!DestinationEntries.Contains(entry))
                {
                    SourceEntries.Add(entry);
                }
            }
        }

        public void MoveToDestination(object entryObj)
        {
            if (entryObj is not CredentialViewModel entry) return;
            if (SourceEntries.Contains(entry))
            {
                SourceEntries.Remove(entry);
                DestinationEntries.Add(entry);
            }
        }

        public void MoveToSource(object entryObj)
        {
            if (entryObj is not CredentialViewModel entry) return;
            if (DestinationEntries.Contains(entry))
            {
                DestinationEntries.Remove(entry);
                SourceEntries.Add(entry);
            }
        }

        private async Task ApplyAsync()
        {
            if (string.IsNullOrWhiteSpace(NewCategoryName))
            {
                ValidationError = "Category name is required.";
                return;
            }

            var entriesToMove = DestinationEntries.ToArray();
            CategoryCreated?.Invoke(NewCategoryName, entriesToMove);
            DialogClosed?.Invoke();
            await Task.CompletedTask;
        }

        private void Cancel()
        {
            DialogClosed?.Invoke();
        }
    }
}
