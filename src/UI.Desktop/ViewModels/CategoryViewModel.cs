using ReactiveUI;
using PhantomVault.UI.Helpers;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// View model for a credential category.
    /// </summary>
    public sealed class CategoryViewModel : ReactiveObject
    {
        private string _name = string.Empty;
        private string _icon = IconPathMigrator.DefaultIcon;
        private int _count;
        private string? _tileColor; // optional hex color for sidebar tile background
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

        /// <summary>
        /// Whether this category is pinned (visible) in the dashboard quick access row.
        /// Defaults to true so all categories appear initially.
        /// </summary>
        public bool IsPinned
        {
            get => _isPinned;
            set => this.RaiseAndSetIfChanged(ref _isPinned, value);
        }
    }
}
