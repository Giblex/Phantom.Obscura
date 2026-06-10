using Avalonia.Controls;
using Avalonia.Threading;
using System;
using ReactiveUI;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.Controls
{
    public partial class EnhancedSearchBar : UserControl
    {
        public EnhancedSearchBar()
        {
            InitializeComponent();

            this.Loaded += (s, e) =>
            {
                bool focusOnOpen = true;
                try { focusOnOpen = SettingsService.Load().FocusSearchOnOpen; }
                catch { }

                if (!focusOnOpen)
                {
                    return;
                }

                Dispatcher.UIThread.Post(() =>
                {
                    var searchTextBox = this.FindControl<TextBox>("SearchTextBox");
                    searchTextBox?.Focus();
                }, DispatcherPriority.Background);
            };
        }
    }
}

