using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services.Autofill;
using PhantomVault.UI.Views.Autofill;
using PhantomVault.UI.ViewModels.Autofill;
using PhantomVault.UI.Controls;

namespace PhantomVault.UI.Services
{

    public sealed class AutofillCoordinator : IDisposable
    {
        private readonly FormFieldDetector _fieldDetector;
        private readonly AutofillSuggestionProvider _suggestionProvider;
        private readonly PasswordCaptureService _captureService;
        private readonly ICredentialRepository _credentialRepository;

        private AutofillMiniWindow? _miniWindow;
        private AutofillMiniWindowViewModel? _miniWindowViewModel;
        private PasswordCaptureToast? _captureToast;
        private bool _isEnabled = true;

        public AutofillCoordinator(
            FormFieldDetector fieldDetector,
            AutofillSuggestionProvider suggestionProvider,
            PasswordCaptureService captureService,
            ICredentialRepository credentialRepository)
        {
            _fieldDetector = fieldDetector ?? throw new ArgumentNullException(nameof(fieldDetector));
            _suggestionProvider = suggestionProvider ?? throw new ArgumentNullException(nameof(suggestionProvider));
            _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
            _credentialRepository = credentialRepository ?? throw new ArgumentNullException(nameof(credentialRepository));

            _captureService.PasswordCaptured += OnPasswordCaptured;
            _captureService.PasswordChanged += OnPasswordChanged;
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set => _isEnabled = value;
        }

        public event EventHandler<AutofillRequestedEventArgs>? AutofillRequested;

        public async Task HandleFormDetectionAsync(string url, List<FormFieldInfo> fields)
        {
            if (!IsEnabled || fields.Count == 0)
                return;

            var result = _fieldDetector.DetectLoginForm(fields);

            if (result.FormType == FormType.Login ||
                result.FormType == FormType.Registration ||
                result.FormType == FormType.PasswordChange)
            {

                var targetField = result.UsernameFields.FirstOrDefault()
                    ?? result.EmailFields.FirstOrDefault()
                    ?? result.PasswordFields.FirstOrDefault();

                if (targetField != null)
                {
                    await ShowMiniWindowForFieldAsync(url, targetField);
                }
            }
        }

        public async Task HandleFormSubmissionAsync(string url, List<FormFieldInfo> fields, Dictionary<string, string> fieldValues)
        {
            if (!IsEnabled)
                return;

            var result = _fieldDetector.DetectLoginForm(fields);
            await _captureService.DetectPasswordSubmissionAsync(url, result, fieldValues);
        }

        public async Task ShowMiniWindowForFieldAsync(string url, FormFieldInfo field)
        {
            if (!IsEnabled)
                return;

            EnsureMiniWindowCreated();

            if (_miniWindow != null && _miniWindowViewModel != null)
            {
                var fieldType = _fieldDetector.DetectFieldType(field);
                await _miniWindowViewModel.ShowForFieldAsync(url, field, fieldType);
                _miniWindow.PositionNearField(field.BoundingBox.X, field.BoundingBox.Y + field.BoundingBox.Height + 5);
                _miniWindow.Show();
            }
        }

        public void HideMiniWindow()
        {
            _miniWindow?.Hide();
            _miniWindowViewModel?.Hide();
        }

        public async Task<List<CredentialSuggestion>> GetSuggestionsForUrlAsync(string url)
        {
            return await _suggestionProvider.GetSuggestionsForDomainAsync(url);
        }

        public void FillCredential(Credential credential, FormFieldInfo? targetField)
        {
            AutofillRequested?.Invoke(this, new AutofillRequestedEventArgs
            {
                Credential = credential,
                TargetField = targetField
            });
        }

        private void EnsureMiniWindowCreated()
        {
            if (_miniWindow == null)
            {
                _miniWindowViewModel = new AutofillMiniWindowViewModel(_suggestionProvider, _captureService);
                _miniWindowViewModel.CredentialSelected += OnCredentialSelected;

                _miniWindow = new AutofillMiniWindow
                {
                    DataContext = _miniWindowViewModel
                };
            }
        }

        private void OnCredentialSelected(object? sender, CredentialSelectedEventArgs e)
        {
            FillCredential(e.Credential, e.TargetField);
        }

        private void OnPasswordCaptured(object? sender, PasswordCaptureEventArgs e)
        {
            if (!IsEnabled)
                return;

            EnsureCaptureToastCreated();

            if (_captureToast != null)
            {
                _captureToast.ShowNewPasswordCapture(e);
            }
        }

        private void OnPasswordChanged(object? sender, PasswordChangeEventArgs e)
        {
            if (!IsEnabled)
                return;

            EnsureCaptureToastCreated();

            if (_captureToast != null)
            {
                _captureToast.ShowPasswordChange(e);
            }
        }

        private void EnsureCaptureToastCreated()
        {
            if (_captureToast == null)
            {
                _captureToast = new PasswordCaptureToast();
                _captureToast.ActionClicked += async (s, e) =>
                {

                    if (_miniWindowViewModel?.PendingCapture != null)
                    {
                        await _captureService.SaveCapturedPasswordAsync(_miniWindowViewModel.PendingCapture);
                    }
                    else if (_miniWindowViewModel?.PendingChange != null)
                    {
                        await _captureService.UpdateCredentialPasswordAsync(
                            _miniWindowViewModel.PendingChange.ExistingCredential,
                            _miniWindowViewModel.PendingChange.NewPassword);
                    }
                };
            }
        }

        public void Dispose()
        {
            _captureService.PasswordCaptured -= OnPasswordCaptured;
            _captureService.PasswordChanged -= OnPasswordChanged;

            if (_miniWindowViewModel != null)
            {
                _miniWindowViewModel.CredentialSelected -= OnCredentialSelected;
            }

            _miniWindow?.Close();
            _miniWindow = null;
            _miniWindowViewModel = null;
            _captureToast = null;
        }
    }

    public sealed class AutofillRequestedEventArgs : EventArgs
    {
        public Credential Credential { get; set; } = null!;
        public FormFieldInfo? TargetField { get; set; }
    }
}

