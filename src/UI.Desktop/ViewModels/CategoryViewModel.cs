using ReactiveUI;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// View model for a credential category.
    /// </summary>
    public sealed class CategoryViewModel : ReactiveObject
    {
        private string _name = string.Empty;
        private string _icon = "📁";
        private int _count;
        private string? _tileColor; // optional hex color for sidebar tile background
        private bool _isActive;

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
    }
}
