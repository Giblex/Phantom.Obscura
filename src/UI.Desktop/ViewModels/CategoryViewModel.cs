using ReactiveUI;
using PhantomVault.UI.Helpers;

namespace PhantomVault.UI.ViewModels
{

    public sealed class CategoryViewModel : ReactiveObject
    {
        private string _name = string.Empty;
        private string _icon = IconPathMigrator.DefaultIcon;
        private int _count;
        private string? _tileColor;
        private bool _isActive;
        private bool _isPinned = true;

        public string Name
        {
            get => _name;
            set => this.RaiseAndSetIfChanged(ref _name, value);
        }

        public string Icon
        {
            get => _icon;
            set => this.RaiseAndSetIfChanged(ref _icon, value);
        }

        public int Count
        {
            get => _count;
            set => this.RaiseAndSetIfChanged(ref _count, value);
        }

        public string? TileColor
        {
            get => _tileColor;
            set => this.RaiseAndSetIfChanged(ref _tileColor, value);
        }

        public bool IsActive
        {
            get => _isActive;
            set => this.RaiseAndSetIfChanged(ref _isActive, value);
        }

        public bool IsPinned
        {
            get => _isPinned;
            set => this.RaiseAndSetIfChanged(ref _isPinned, value);
        }
    }
}

