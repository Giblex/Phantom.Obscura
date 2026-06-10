using System;
using System.Reactive;
using Avalonia.Media;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using ReactiveUI;

namespace PhantomVault.UI.ViewModels
{

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

        public string Label { get; }

        public string FilterKey { get; }

        public IconPreset Icon { get; }

        public string Accent { get; }

        public SolidColorBrush AccentBrush => _accentBrush;

        public bool IsPinned
        {
            get => _isPinned;
            set => this.RaiseAndSetIfChanged(ref _isPinned, value);
        }

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

        public string CountDisplay => Count == 1 ? "1 item" : $"{Count} items";
    }
}

