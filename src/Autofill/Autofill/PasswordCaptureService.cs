using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PhantomVault.Core.Models;

namespace PhantomVault.Core.Services.Autofill
{
    /// <summary>
    /// Captures new passwords during registration/login and prompts to save or update vault entries.
    /// Detects password changes and offers to update existing credentials.
    /// </summary>
    public sealed class PasswordCaptureService
    {
        private readonly ICredentialRepository _repository;
        private readonly Dictionary<string, PendingCapture> _pendingCaptures = new();

        public PasswordCaptureService(ICredentialRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        /// <summary>
        /// Event raised when a new password is detected and ready to be saved.
        /// </summary>
        public event EventHandler<PasswordCaptureEventArgs>? PasswordCaptured;

        /// <summary>
        /// Event raised when a password change is detected for an existing credential.
        /// </summary>
        public event EventHandler<PasswordChangeEventArgs>? PasswordChanged;

        /// <summary>
        /// Detects and captures password submission from a login/registration form.
        /// </summary>
        public async Task DetectPasswordSubmissionAsync(string url, LoginFormDetectionResult formData, Dictionary<string, string> fieldValues)
        {
            if (formData == null || fieldValues == null)
                return;

            var domain = ExtractDomain(url);
            if (string.IsNullOrEmpty(domain))
                return;

            // Extract username and password
            var username = ExtractUsername(formData, fieldValues);
            var password = ExtractPassword(formData, fieldValues);

            if (string.IsNullOrWhiteSpace(password))
                return;

            // Check if this is a registration form (has password confirmation)
            if (formData.FormType == FormType.Registration)
            {
                var confirmPassword = ExtractConfirmPassword(formData, fieldValues);
                if (password != confirmPassword)
                    return; // Passwords don't match, don't capture

                // This is a new account registration
                OnPasswordCaptured(new PasswordCaptureEventArgs
                {
                    Url = url,
                    Domain = domain,
                    Username = username,
                    Password = password,
                    CaptureType = CaptureType.Registration
                });
            }
            else if (formData.FormType == FormType.Login)
            {
                // Check if we have an existing credential for this domain + username
                var existing = await FindExistingCredentialAsync(domain, username);

                if (existing != null && existing.Password != password)
                {
                    // Password has changed
                    OnPasswordChanged(new PasswordChangeEventArgs
                    {
                        ExistingCredential = existing,
                        NewPassword = password,
                        Url = url,
                        Domain = domain
                    });
                }
                else if (existing == null)
                {
                    // New login captured
                    OnPasswordCaptured(new PasswordCaptureEventArgs
                    {
                        Url = url,
                        Domain = domain,
                        Username = username,
                        Password = password,
                        CaptureType = CaptureType.NewLogin
                    });
                }
            }
            else if (formData.FormType == FormType.PasswordChange)
            {
                // Password change form detected
                var existing = await FindExistingCredentialAsync(domain, username);
                if (existing != null)
                {
                    OnPasswordChanged(new PasswordChangeEventArgs
                    {
                        ExistingCredential = existing,
                        NewPassword = password,
                        Url = url,
                        Domain = domain
                    });
                }
            }
        }

        /// <summary>
        /// Saves a captured password to the vault.
        /// </summary>
        public async Task<Credential> SaveCapturedPasswordAsync(PasswordCaptureEventArgs args, string? title = null)
        {
            var credential = new Credential
            {
                Title = title ?? args.Domain,
                Username = args.Username,
                Password = args.Password,
                Url = args.Url,
                EntryType = EntryType.Password,
                Group = "Captured Logins",
                CreatedUtc = DateTimeOffset.UtcNow,
                LastUpdatedUtc = DateTimeOffset.UtcNow
            };

            await _repository.SaveCredentialAsync(credential);
            return credential;
        }

        /// <summary>
        /// Updates an existing credential with a new password.
        /// </summary>
        public async Task UpdateCredentialPasswordAsync(Credential credential, string newPassword)
        {
            if (credential == null) throw new ArgumentNullException(nameof(credential));

            credential.Password = newPassword;
            credential.LastUpdatedUtc = DateTimeOffset.UtcNow;

            await _repository.UpdateCredentialAsync(credential);
        }

        private string ExtractUsername(LoginFormDetectionResult formData, Dictionary<string, string> fieldValues)
        {
            // Try email fields first
            var emailField = formData.EmailFields.FirstOrDefault();
            if (emailField != null && fieldValues.TryGetValue(GetFieldKey(emailField), out var email))
                return email;

            // Try username fields
            var usernameField = formData.UsernameFields.FirstOrDefault();
            if (usernameField != null && fieldValues.TryGetValue(GetFieldKey(usernameField), out var username))
                return username;

            return string.Empty;
        }

        private string ExtractPassword(LoginFormDetectionResult formData, Dictionary<string, string> fieldValues)
        {
            var passwordField = formData.PasswordFields.FirstOrDefault();
            if (passwordField != null && fieldValues.TryGetValue(GetFieldKey(passwordField), out var password))
                return password;

            return string.Empty;
        }

        private string ExtractConfirmPassword(LoginFormDetectionResult formData, Dictionary<string, string> fieldValues)
        {
            var confirmField = formData.PasswordConfirmFields.FirstOrDefault();
            if (confirmField != null && fieldValues.TryGetValue(GetFieldKey(confirmField), out var confirm))
                return confirm;

            return string.Empty;
        }

        private string GetFieldKey(FormFieldInfo field)
        {
            return !string.IsNullOrEmpty(field.Id) ? field.Id : field.Name;
        }

        private async Task<Credential?> FindExistingCredentialAsync(string domain, string username)
        {
            var credentials = (await _repository.GetAllCredentialsAsync())
                .Where(c => c.EntryType == EntryType.Password)
                .ToList();

            foreach (var cred in credentials)
            {
                var credDomain = ExtractDomain(cred.Url);
                if (credDomain == domain && 
                    string.Equals(cred.Username, username, StringComparison.OrdinalIgnoreCase))
                {
                    return cred;
                }
            }

            return null;
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
                return uri.Host.ToLowerInvariant();
            }
            catch
            {
                return string.Empty;
            }
        }

        private void OnPasswordCaptured(PasswordCaptureEventArgs e)
        {
            PasswordCaptured?.Invoke(this, e);
        }

        private void OnPasswordChanged(PasswordChangeEventArgs e)
        {
            PasswordChanged?.Invoke(this, e);
        }

        private class PendingCapture
        {
            public string Username { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
            public DateTimeOffset CapturedAt { get; set; }
        }
    }

    /// <summary>
    /// Event args for password capture.
    /// </summary>
    public sealed class PasswordCaptureEventArgs : EventArgs
    {
        public string Url { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public CaptureType CaptureType { get; set; }
    }

    /// <summary>
    /// Event args for password change detection.
    /// </summary>
    public sealed class PasswordChangeEventArgs : EventArgs
    {
        public Credential ExistingCredential { get; set; } = null!;
        public string NewPassword { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
    }

    /// <summary>
    /// Type of password capture.
    /// </summary>
    public enum CaptureType
    {
        NewLogin,
        Registration,
        PasswordChange
    }
}
