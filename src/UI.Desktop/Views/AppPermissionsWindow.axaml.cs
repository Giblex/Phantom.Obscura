using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ReactiveUI;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.Views
{
    public partial class AppPermissionsWindow : ThemeAwareWindow
    {
        public AppPermissionsWindow()
        {
            var vm = new AppPermissionsViewModel();
            vm.CloseRequested += (_, _) => Close();
            DataContext = vm;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void Close_Click(object? sender, RoutedEventArgs e) => Close();
    }

    /// <summary>
    /// Self-contained view model for the app auto-fill permissions editor.
    /// Loads the persisted list, lets the user add/remove/toggle apps, and
    /// writes the result back to <see cref="SettingsService"/> on Save.
    /// </summary>
    public sealed class AppPermissionsViewModel : ReactiveObject
    {
        private string _newAppName = string.Empty;

        public ObservableCollection<PermissionItem> Permissions { get; } = new();

        public string NewAppName
        {
            get => _newAppName;
            set => this.RaiseAndSetIfChanged(ref _newAppName, value);
        }

        public bool IsEmpty => Permissions.Count == 0;

        public ReactiveCommand<Unit, Unit> AddAppCommand { get; }
        public ReactiveCommand<PermissionItem, Unit> RemoveAppCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveCommand { get; }

        public event EventHandler? CloseRequested;

        public AppPermissionsViewModel()
        {
            try
            {
                var settings = SettingsService.Load();
                foreach (var p in settings.AutoFillAppPermissions ?? new System.Collections.Generic.List<AutoFillAppPermission>())
                {
                    if (!string.IsNullOrWhiteSpace(p.AppName))
                        Permissions.Add(new PermissionItem { AppName = p.AppName, Allowed = p.Allowed });
                }
            }
            catch { /* best-effort load */ }

            Permissions.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(IsEmpty));

            AddAppCommand = ReactiveCommand.Create(AddApp);
            RemoveAppCommand = ReactiveCommand.Create<PermissionItem>(RemoveApp);
            SaveCommand = ReactiveCommand.Create(Save);
        }

        private void AddApp()
        {
            var name = (NewAppName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name)) return;
            if (Permissions.Any(p => string.Equals(p.AppName, name, StringComparison.OrdinalIgnoreCase)))
            {
                NewAppName = string.Empty;
                return;
            }
            Permissions.Add(new PermissionItem { AppName = name, Allowed = true });
            NewAppName = string.Empty;
        }

        private void RemoveApp(PermissionItem? item)
        {
            if (item != null) Permissions.Remove(item);
        }

        private void Save()
        {
            try
            {
                SettingsService.Update(s =>
                {
                    s.AutoFillAppPermissions = Permissions
                        .Where(p => !string.IsNullOrWhiteSpace(p.AppName))
                        .Select(p => new AutoFillAppPermission { AppName = p.AppName.Trim(), Allowed = p.Allowed })
                        .ToList();
                });
            }
            catch { /* best-effort persist */ }
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        public sealed class PermissionItem : ReactiveObject
        {
            private string _appName = string.Empty;
            private bool _allowed = true;

            public string AppName
            {
                get => _appName;
                set => this.RaiseAndSetIfChanged(ref _appName, value);
            }

            public bool Allowed
            {
                get => _allowed;
                set => this.RaiseAndSetIfChanged(ref _allowed, value);
            }
        }
    }
}
