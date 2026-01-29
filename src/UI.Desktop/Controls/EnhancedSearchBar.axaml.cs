using Avalonia.Controls;
using Avalonia.Threading;
using System;
using ReactiveUI;

namespace PhantomVault.UI.Controls
{
    public partial class EnhancedSearchBar : UserControl
    {
        public EnhancedSearchBar()
        {
            InitializeComponent();

            this.Loaded += (s, e) =>
            {
                // Focus search box when control becomes visible
                Dispatcher.UIThread.Post(() =>
                {
                    var searchTextBox = this.FindControl<TextBox>("SearchTextBox");
                    searchTextBox?.Focus();
                }, DispatcherPriority.Background);
            };
        }
    }
}
