using System.Threading.Tasks;
using Avalonia.Controls;
using PhantomVault.UI.ViewModels.AutoFill;

namespace PhantomVault.UI.Views.AutoFill
{
    public partial class NoMatchFoundWindow : Window
    {
        private readonly TaskCompletionSource<NoMatchResult> _tcs = new();

        public NoMatchFoundWindow()
        {
            InitializeComponent();
        }

        protected override void OnDataContextChanged(System.EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (DataContext is NoMatchFoundViewModel vm)
            {
                vm.ResultChosen += (_, result) =>
                {
                    _tcs.TrySetResult(result);
                    Close();
                };
            }
        }

        /// <summary>
        /// Shows the dialog and awaits the user's choice.
        /// </summary>
        public Task<NoMatchResult> ShowAsync()
        {
            Show();
            return _tcs.Task;
        }
    }
}
