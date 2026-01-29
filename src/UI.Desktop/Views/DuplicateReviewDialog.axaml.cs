using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using PhantomVault.UI.ViewModels;
using PhantomVault.Core.Services;
using System.Collections.Generic;

namespace PhantomVault.UI.Views
{
    public partial class DuplicateReviewDialog : ThemeAwareWindow
    {
        public DuplicateReviewDialog()
        {
            InitializeComponent();
        }

        public DuplicateReviewDialog(List<DuplicateInfo> duplicates)
        {
            InitializeComponent();
            DataContext = new DuplicateReviewViewModel(duplicates);

            if (DataContext is DuplicateReviewViewModel vm)
            {
                vm.SetOwnerWindow(this);
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
