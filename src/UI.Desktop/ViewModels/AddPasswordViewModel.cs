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
        private Color _selectedIconColor = Color.Parse("#FFB5E5FF");
        private bool _useNoColor = false;
        private string _iconInitials = "?";
        private readonly IconManager? _iconManager;

        public Color[] AvailableColors { get; } = new[]
        {
            Color.Parse("#FFB5E5FF"),
            Color.Parse("#FFFFC1E3"),
            Color.Parse("#FFFFDFBB"),
            Color.Parse("#FFC7E5C7"),
            Color.Parse("#FFFFE5B4"),
            Color.Parse("#FFE5D4FF"),
            Color.Parse("#FFFFC9C9"),
            Color.Parse("#FFD4F4FF"),
            Color.Parse("#FFFFE4F0"),
            Color.Parse("#FFE8F5E9")
        };

        public AddPasswordViewModel()
        {

            try
            {
                var iconsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Visuals");
                _iconManager = new IconManager(iconsDir);
            }
            catch
            {

            }

            GeneratePasswordCommand = ReactiveCommand.Create(() =>
            {
                Password = GenerateRandomPassword(16);
            });

            SaveCommand = ReactiveCommand.Create(() =>
            {

                LastSaved = DateTimeOffset.UtcNow;
            }, this.WhenAnyValue(vm => vm.IsBusy, vm => vm.Title, (busy, title) => !busy && !string.IsNullOrWhiteSpace(title)));

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

        public string? AutoDetectedIconPath
        {
            get => _autoDetectedIconPath;
            private set => this.RaiseAndSetIfChanged(ref _autoDetectedIconPath, value);
        }

        public bool HasAutoDetectedIcon
        {
            get => _hasAutoDetectedIcon;
            private set => this.RaiseAndSetIfChanged(ref _hasAutoDetectedIcon, value);
        }

        public Color SelectedIconColor
        {
            get => _selectedIconColor;
            set
            {

                this.RaiseAndSetIfChanged(ref _selectedIconColor, value);

                UseNoColor = false;
            }
        }

        public bool UseNoColor
        {
            get => _useNoColor;
            set => this.RaiseAndSetIfChanged(ref _useNoColor, value);
        }

        public string IconInitials
        {
            get => _iconInitials;
            private set => this.RaiseAndSetIfChanged(ref _iconInitials, value);
        }

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

        private void UpdateAutoDetectedIcon()
        {
            if (_iconManager == null)
            {
                HasAutoDetectedIcon = false;
                UpdateIconInitials();
                return;
            }

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

        private void UpdateIconInitials()
        {
            if (HasAutoDetectedIcon)
            {
                IconInitials = string.Empty;
                return;
            }

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

