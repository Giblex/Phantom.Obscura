using System;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using System.Security.Cryptography;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using ReactiveUI;
using Avalonia.Media;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// View model for the window that adds a new password entry to the
    /// vault. In a real application the password would be persisted to an
    /// encrypted database. Here we simply expose the properties and
    /// demonstrate how a random password generator might work.
    /// </summary>
    public sealed class AddPasswordViewModel : ReactiveObject
    {
        private string _title = string.Empty;
        private string _username = string.Empty;
        private string _password = string.Empty;
        private string _url = string.Empty;
        private bool _generateRandom;
        private bool _isBusy;
        private DateTimeOffset? _lastSaved;
        private string? _autoDetectedIcon;
        private string? _autoDetectedIconPath;
        private bool _hasAutoDetectedIcon;
        private Color _selectedIconColor = Color.Parse("#FFB5E5FF"); // Default pastel blue
        private bool _useNoColor = false;
        private string _iconInitials = "?";
        private readonly IconManager? _iconManager;

        // Pastel color options for icons without images
        public Color[] AvailableColors { get; } = new[]
        {
            Color.Parse("#FFB5E5FF"), // Pastel Blue
            Color.Parse("#FFFFC1E3"), // Pastel Pink
            Color.Parse("#FFFFDFBB"), // Pastel Peach
            Color.Parse("#FFC7E5C7"), // Pastel Green
            Color.Parse("#FFFFE5B4"), // Pastel Yellow
            Color.Parse("#FFE5D4FF"), // Pastel Purple
            Color.Parse("#FFFFC9C9"), // Pastel Red
            Color.Parse("#FFD4F4FF"), // Pastel Cyan
            Color.Parse("#FFFFE4F0"), // Pastel Rose
            Color.Parse("#FFE8F5E9")  // Pastel Mint
        };

        public AddPasswordViewModel()
        {
            // Initialize IconManager
            try
            {
                var iconsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Icons");
                _iconManager = new IconManager(iconsDir);
            }
            catch
            {
                // IconManager initialization failed, auto-detection won't work
            }

            GeneratePasswordCommand = ReactiveCommand.Create(() =>
            {
                Password = GenerateRandomPassword(16);
            });

            // Save only enabled when not busy and Title is not empty
            SaveCommand = ReactiveCommand.Create(() =>
            {
                // Persisting the entry is outside the scope of this example.
                LastSaved = DateTimeOffset.UtcNow;
            }, this.WhenAnyValue(vm => vm.IsBusy, vm => vm.Title, (busy, title) => !busy && !string.IsNullOrWhiteSpace(title)));

            // Command used by the UI to select a color or choose 'no color'.
            SelectIconColorCommand = ReactiveCommand.Create<Color?>(c =>
            {
                if (c is null)
                {
                    UseNoColor = true;
                }
                else
                {
                    SelectedIconColor = c.Value;
                }
            });

            // Auto-detect icon when Title or Url changes
            this.WhenAnyValue(vm => vm.Title, vm => vm.Url)
                .Throttle(TimeSpan.FromMilliseconds(300))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => UpdateAutoDetectedIcon());
        }

        public string Title
        {
            get => _title;
            set => this.RaiseAndSetIfChanged(ref _title, value);
        }

        public string Username
        {
            get => _username;
            set => this.RaiseAndSetIfChanged(ref _username, value);
        }

        public string Password
        {
            get => _password;
            set => this.RaiseAndSetIfChanged(ref _password, value);
        }

        public string Url
        {
            get => _url;
            set => this.RaiseAndSetIfChanged(ref _url, value);
        }

        public bool GenerateRandom
        {
            get => _generateRandom;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _generateRandom, value) && value)
                {
                    // Immediately generate a password when toggled on
                    Password = GenerateRandomPassword(16);
                }
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
        }

        public DateTimeOffset? LastSaved
        {
            get => _lastSaved;
            private set => this.RaiseAndSetIfChanged(ref _lastSaved, value);
        }

        /// <summary>
        /// Path to auto-detected icon image file
        /// </summary>
        public string? AutoDetectedIconPath
        {
            get => _autoDetectedIconPath;
            private set => this.RaiseAndSetIfChanged(ref _autoDetectedIconPath, value);
        }

        /// <summary>
        /// Whether an icon was auto-detected
        /// </summary>
        public bool HasAutoDetectedIcon
        {
            get => _hasAutoDetectedIcon;
            private set => this.RaiseAndSetIfChanged(ref _hasAutoDetectedIcon, value);
        }

        /// <summary>
        /// Selected background color for text-based icon
        /// </summary>
        public Color SelectedIconColor
        {
            get => _selectedIconColor;
            set
            {
                // Update value (ReactiveUI may return the value or a boolean from RaiseAndSetIfChanged)
                this.RaiseAndSetIfChanged(ref _selectedIconColor, value);
                // Selecting a concrete color disables the NoColor flag
                UseNoColor = false;
            }
        }

        /// <summary>
        /// When true, indicates the user explicitly chose 'No color' for the icon background.
        /// </summary>
        public bool UseNoColor
        {
            get => _useNoColor;
            set => this.RaiseAndSetIfChanged(ref _useNoColor, value);
        }

        /// <summary>
        /// Initials to display when no icon image is available
        /// </summary>
        public string IconInitials
        {
            get => _iconInitials;
            private set => this.RaiseAndSetIfChanged(ref _iconInitials, value);
        }

        /// <summary>
        /// Internal detected icon name
        /// </summary>
        public string? AutoDetectedIcon
        {
            get => _autoDetectedIcon;
            private set => this.RaiseAndSetIfChanged(ref _autoDetectedIcon, value);
        }

        public ReactiveCommand<Unit, Unit> GeneratePasswordCommand { get; }

        public ReactiveCommand<Unit, Unit> SaveCommand { get; }

        public ReactiveCommand<Color?, Unit> SelectIconColorCommand { get; }

        private static string GenerateRandomPassword(int length)
        {
            const string charset = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789!@#$%^&*()";
            var bytes = new byte[length];
            RandomNumberGenerator.Fill(bytes);
            var chars = new char[length];
            for (int i = 0; i < length; i++)
            {
                chars[i] = charset[bytes[i] % charset.Length];
            }
            Array.Clear(bytes, 0, bytes.Length);
            return new string(chars);
        }

        /// <summary>
        /// Attempts to auto-detect an icon based on the current Title and Url
        /// </summary>
        private void UpdateAutoDetectedIcon()
        {
            if (_iconManager == null)
            {
                HasAutoDetectedIcon = false;
                UpdateIconInitials();
                return;
            }

            // Create a temporary credential for icon detection
            var tempCredential = new Credential
            {
                Title = Title,
                Url = Url
            };

            try
            {
                var iconPath = _iconManager.FindIconPathForCredential(tempCredential);

                if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                {
                    AutoDetectedIconPath = iconPath;
                    HasAutoDetectedIcon = true;
                    AutoDetectedIcon = Path.GetFileNameWithoutExtension(iconPath);
                }
                else
                {
                    AutoDetectedIconPath = null;
                    HasAutoDetectedIcon = false;
                    AutoDetectedIcon = null;
                }
            }
            catch
            {
                AutoDetectedIconPath = null;
                HasAutoDetectedIcon = false;
                AutoDetectedIcon = null;
            }

            UpdateIconInitials();
        }

        /// <summary>
        /// Updates the initials displayed when no icon image is available
        /// </summary>
        private void UpdateIconInitials()
        {
            if (HasAutoDetectedIcon)
            {
                IconInitials = string.Empty;
                return;
            }

            // Generate initials from title
            if (!string.IsNullOrWhiteSpace(Title))
            {
                var words = Title.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length >= 2)
                {
                    IconInitials = $"{words[0][0]}{words[1][0]}".ToUpper();
                }
                else if (words.Length == 1 && words[0].Length >= 2)
                {
                    IconInitials = words[0].Substring(0, 2).ToUpper();
                }
                else if (words.Length == 1)
                {
                    IconInitials = words[0][0].ToString().ToUpper();
                }
                else
                {
                    IconInitials = "?";
                }
            }
            else
            {
                IconInitials = "?";
            }
        }
    }
}