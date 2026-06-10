using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels
{

    public class AddCategoryDialogViewModel : ReactiveObject
    {
        private readonly VaultViewModel? _vaultViewModel;
        private ObservableCollection<string> _sourceCategories = new();
        private string? _selectedSourceCategory;
        private string _newCategoryName = string.Empty;
        private string _validationError = string.Empty;
        private ObservableCollection<CredentialViewModel> _sourceEntries = new();
        private ObservableCollection<CredentialViewModel> _destinationEntries = new();

        public ObservableCollection<string> SourceCategories
        {
            get => _sourceCategories;
            set => this.RaiseAndSetIfChanged(ref _sourceCategories, value);
        }

        public string? SelectedSourceCategory
        {
            get => _selectedSourceCategory;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedSourceCategory, value);
                LoadSourceEntries();
            }
        }

        public string NewCategoryName
        {
            get => _newCategoryName;
            set
            {
                this.RaiseAndSetIfChanged(ref _newCategoryName, value);

                if (!string.IsNullOrWhiteSpace(value) && !string.IsNullOrEmpty(ValidationError))
                {
                    ValidationError = string.Empty;
                }
            }
        }

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

