using System;
using System.Reactive;
using Avalonia.Media;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// Represents a pinnable category shortcut tile on the dashboard.
    /// Each tile maps to an <see cref="EntryType"/> and navigates to
    /// the corresponding filtered view when clicked.
    /// </summary>
    public sealed class QuickAccessCategoryTile : ReactiveObject
    {
        private bool _isPinned = true;
        private int _count;
        private readonly SolidColorBrush _accentBrush;

        public QuickAccessCategoryTile(
            string label,
            string filterKey,
            IconPreset icon,
            string accent)
        {
            Label = label;
            FilterKey = filterKey;
            Icon = icon;
            Accent = accent;
            _accentBrush = new SolidColorBrush(Color.Parse(accent));
        }

        /// <summary>Display label shown under the icon.</summary>
        public string Label { get; }

        /// <summary>The filter key passed to <c>NavigateToVaultWithFilter</c>.</summary>
        public string FilterKey { get; }

        /// <summary>Icon preset for the <c>SvgIcon</c> control.</summary>
        public IconPreset Icon { get; }

        /// <summary>Hex accent colour string for the icon circle (e.g. "#4A90D9").</summary>
        public string Accent { get; }

        /// <summary>An <see cref="IBrush"/> parsed from <see cref="Accent"/>.</summary>
        public SolidColorBrush AccentBrush => _accentBrush;

        /// <summary>Whether the tile is pinned (visible) in the dashboard.</summary>
        public bool IsPinned
        {
            get => _isPinned;
            set => this.RaiseAndSetIfChanged(ref _isPinned, value);
        }

        /// <summary>Number of credentials of this type in the vault.</summary>
        public int Count
        {
            get => _count;
            set
            {
                var prev = _count;
                this.RaiseAndSetIfChanged(ref _count, value);
                if (prev != _count)
                    this.RaisePropertyChanged(nameof(CountDisplay));
            }
        }

        /// <summary>Count formatted as a display string (e.g. "12 items").</summary>
        public string CountDisplay => Count == 1 ? "1 item" : $"{Count} items";
    }
}
