using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services.Autofill;

namespace PhantomVault.UI.ViewModels.Autofill
{
    /// <summary>
    /// ViewModel for the autofill mini-window that displays credential suggestions.
    /// </summary>
    public sealed class AutofillMiniWindowViewModel : ReactiveObject
    {
        private readonly AutofillSuggestionProvider _suggestionProvider;
        private readonly PasswordCaptureService _captureService;
        private string _currentUrl = string.Empty;
        private string _currentDomain = string.Empty;
        private FormFieldInfo? _targetField;
        private AutofillTab _selectedTab = AutofillTab.Suggestions;
        private CredentialSuggestion? _selectedSuggestion;
        private bool _isVisible;
        private double _positionX;
        private double _positionY;
        private string _searchFilter = string.Empty;
        private bool _hasNewPasswordCapture;
        private PasswordCaptureEventArgs? _pendingCapture;
        private PasswordChangeEventArgs? _pendingChange;

        public AutofillMiniWindowViewModel(
            AutofillSuggestionProvider suggestionProvider,
            PasswordCaptureService captureService)
        {
            _suggestionProvider = suggestionProvider ?? throw new ArgumentNullException(nameof(suggestionProvider));
            _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));

            Suggestions = new ObservableCollection<CredentialSuggestion>();
            FilteredSuggestions = new ObservableCollection<CredentialSuggestion>();

            // Commands
            SelectSuggestionCommand = ReactiveCommand.Create<CredentialSuggestion>(SelectSuggestion);
            CloseWindowCommand = ReactiveCommand.Create(CloseWindow);
            SwitchToSuggestionsTabCommand = ReactiveCommand.Create(() => { SelectedTab = AutofillTab.Suggestions; });
            SwitchToCaptureTabCommand = ReactiveCommand.Create(() => { SelectedTab = AutofillTab.Capture; });
            SaveCapturedPasswordCommand = ReactiveCommand.CreateFromTask(SaveCapturedPasswordAsync);
            UpdatePasswordCommand = ReactiveCommand.CreateFromTask(UpdatePasswordAsync);
            IgnoreCaptureCommand = ReactiveCommand.Create(IgnoreCapture);

            // Subscribe to password capture events
            _captureService.PasswordCaptured += OnPasswordCaptured;
            _captureService.PasswordChanged += OnPasswordChanged;

            // Subscribe to search filter changes
            this.WhenAnyValue(x => x.SearchFilter)
                .Subscribe(_ => ApplyFilter());
        }

        public ObservableCollection<CredentialSuggestion> Suggestions { get; }
        public ObservableCollection<CredentialSuggestion> FilteredSuggestions { get; }

        public string CurrentUrl
        {
            get => _currentUrl;
            set => this.RaiseAndSetIfChanged(ref _currentUrl, value);
        }

        public string CurrentDomain
        {
            get => _currentDomain;
            set => this.RaiseAndSetIfChanged(ref _currentDomain, value);
        }

        public AutofillTab SelectedTab
        {
            get => _selectedTab;
            set => this.RaiseAndSetIfChanged(ref _selectedTab, value);
        }

        public CredentialSuggestion? SelectedSuggestion
        {
            get => _selectedSuggestion;
            set => this.RaiseAndSetIfChanged(ref _selectedSuggestion, value);
        }

        public bool IsVisible
        {
            get => _isVisible;
            set => this.RaiseAndSetIfChanged(ref _isVisible, value);
        }

        public double PositionX
        {
            get => _positionX;
            set => this.RaiseAndSetIfChanged(ref _positionX, value);
        }

        public double PositionY
        {
            get => _positionY;
            set => this.RaiseAndSetIfChanged(ref _positionY, value);
        }

        public string SearchFilter
        {
            get => _searchFilter;
            set => this.RaiseAndSetIfChanged(ref _searchFilter, value);
        }

        public bool HasNewPasswordCapture
        {
            get => _hasNewPasswordCapture;
            private set => this.RaiseAndSetIfChanged(ref _hasNewPasswordCapture, value);
        }

        public PasswordCaptureEventArgs? PendingCapture
        {
            get => _pendingCapture;
            private set => this.RaiseAndSetIfChanged(ref _pendingCapture, value);
        }

        public PasswordChangeEventArgs? PendingChange
        {
            get => _pendingChange;
            private set => this.RaiseAndSetIfChanged(ref _pendingChange, value);
        }

        public bool IsSuggestionsTabActive => SelectedTab == AutofillTab.Suggestions;
        public bool IsCaptureTabActive => SelectedTab == AutofillTab.Capture;
        public bool HasSuggestions => FilteredSuggestions.Any();
        public string CaptureMessage => PendingCapture != null 
            ? $"Save password for {PendingCapture.Username} at {PendingCapture.Domain}?"
            : PendingChange != null
                ? $"Update password for {PendingChange.ExistingCredential.Username} at {PendingChange.Domain}?"
                : string.Empty;

        // Commands
        public ReactiveCommand<CredentialSuggestion, Unit> SelectSuggestionCommand { get; }
        public ReactiveCommand<Unit, Unit> CloseWindowCommand { get; }
        public ReactiveCommand<Unit, Unit> SwitchToSuggestionsTabCommand { get; }
        public ReactiveCommand<Unit, Unit> SwitchToCaptureTabCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveCapturedPasswordCommand { get; }
        public ReactiveCommand<Unit, Unit> UpdatePasswordCommand { get; }
        public ReactiveCommand<Unit, Unit> IgnoreCaptureCommand { get; }

        /// <summary>
        /// Event raised when a credential is selected for autofill.
        /// </summary>
        public event EventHandler<CredentialSelectedEventArgs>? CredentialSelected;

        /// <summary>
        /// Shows the autofill window at the specified field position with relevant suggestions.
        /// </summary>
        public async Task ShowForFieldAsync(string url, FormFieldInfo field, FormFieldType fieldType)
        {
            _targetField = field;
            CurrentUrl = url;
            CurrentDomain = ExtractDomain(url);

            // Position window near the input field
            PositionX = field.BoundingBox.X;
            PositionY = field.BoundingBox.Y + field.BoundingBox.Height + 5;

            // Load suggestions
            await LoadSuggestionsAsync(fieldType);

            IsVisible = true;
            SelectedTab = AutofillTab.Suggestions;
        }

        /// <summary>
        /// Hides the autofill window.
        /// </summary>
        public void Hide()
        {
            IsVisible = false;
            Suggestions.Clear();
            FilteredSuggestions.Clear();
            SearchFilter = string.Empty;
        }

        private async Task LoadSuggestionsAsync(FormFieldType fieldType)
        {
            Suggestions.Clear();

            List<CredentialSuggestion> suggestions;

            if (fieldType == FormFieldType.Username || fieldType == FormFieldType.Email)
            {
                suggestions = await _suggestionProvider.GetSuggestionsForUsernameAsync(CurrentUrl, SearchFilter);
            }
            else
            {
                suggestions = await _suggestionProvider.GetSuggestionsForDomainAsync(CurrentUrl);
            }

            foreach (var suggestion in suggestions.Take(10)) // Limit to top 10
            {
                Suggestions.Add(suggestion);
            }

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            FilteredSuggestions.Clear();

            var filtered = string.IsNullOrWhiteSpace(SearchFilter)
                ? Suggestions
                : Suggestions.Where(s =>
                    s.Credential.Title.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase) ||
                    s.Credential.Username.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase));

            foreach (var suggestion in filtered)
            {
                FilteredSuggestions.Add(suggestion);
            }

            this.RaisePropertyChanged(nameof(HasSuggestions));
        }

        private void SelectSuggestion(CredentialSuggestion suggestion)
        {
            SelectedSuggestion = suggestion;
            CredentialSelected?.Invoke(this, new CredentialSelectedEventArgs
            {
                Credential = suggestion.Credential,
                TargetField = _targetField
            });
            Hide();
        }

        private void CloseWindow()
        {
            Hide();
        }

        private void OnPasswordCaptured(object? sender, PasswordCaptureEventArgs e)
        {
            PendingCapture = e;
            PendingChange = null;
            HasNewPasswordCapture = true;
            SelectedTab = AutofillTab.Capture;
            
            this.RaisePropertyChanged(nameof(CaptureMessage));
            this.RaisePropertyChanged(nameof(IsCaptureTabActive));
        }

        private void OnPasswordChanged(object? sender, PasswordChangeEventArgs e)
        {
            PendingChange = e;
            PendingCapture = null;
            HasNewPasswordCapture = true;
            SelectedTab = AutofillTab.Capture;
            
            this.RaisePropertyChanged(nameof(CaptureMessage));
            this.RaisePropertyChanged(nameof(IsCaptureTabActive));
        }

        private async Task SaveCapturedPasswordAsync()
        {
            if (PendingCapture == null) return;

            try
            {
                await _captureService.SaveCapturedPasswordAsync(PendingCapture);
                PendingCapture = null;
                HasNewPasswordCapture = false;
                this.RaisePropertyChanged(nameof(CaptureMessage));
            }
            catch (Exception ex)
            {
                // Handle error (could raise an event for UI notification)
                System.Diagnostics.Debug.WriteLine($"Failed to save captured password: {ex.Message}");
            }
        }

        private async Task UpdatePasswordAsync()
        {
            if (PendingChange == null) return;

            try
            {
                await _captureService.UpdateCredentialPasswordAsync(
                    PendingChange.ExistingCredential,
                    PendingChange.NewPassword);
                
                PendingChange = null;
                HasNewPasswordCapture = false;
                this.RaisePropertyChanged(nameof(CaptureMessage));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to update password: {ex.Message}");
            }
        }

        private void IgnoreCapture()
        {
            PendingCapture = null;
            PendingChange = null;
            HasNewPasswordCapture = false;
            SelectedTab = AutofillTab.Suggestions;
            this.RaisePropertyChanged(nameof(CaptureMessage));
        }

        private string ExtractDomain(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;

            try
            {
                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    url = "https://" + url;
                }

                var uri = new Uri(url);
                return uri.Host;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    public enum AutofillTab
    {
        Suggestions,
        Capture
    }

    public sealed class CredentialSelectedEventArgs : EventArgs
    {
        public Credential Credential { get; set; } = null!;
        public FormFieldInfo? TargetField { get; set; }
    }
}
