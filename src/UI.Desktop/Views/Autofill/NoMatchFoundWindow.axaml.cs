using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using PhantomVault.UI.ViewModels.AutoFill;
using PhantomVault.UI.Views;

namespace PhantomVault.UI.Views.AutoFill
{
    public partial class NoMatchFoundWindow : ThemeAwareWindow
    {
        private readonly TaskCompletionSource<NoMatchResult> _tcs = new();
        private Border? _shell;
        private bool _closing;

        public NoMatchFoundWindow()
        {
            InitializeComponent();

            _shell = this.FindControl<Border>("Shell");

            // Frameless, so the header is the drag handle.
            var header = this.FindControl<Grid>("DragHeader");
            if (header != null)
            {
                header.PointerPressed += (_, e) =>
                {
                    if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Source == header)
                        BeginMoveDrag(e);
                };
            }

            Opened += (_, _) =>
            {
                if (_shell != null)
                    _ = PhantomVault.UI.Views.Autofill.AutofillMotion.EnterAsync(_shell, fromY: 10);
            };
        }

        protected override void OnDataContextChanged(System.EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (DataContext is NoMatchFoundViewModel vm)
            {
                vm.ResultChosen += (_, result) =>
                {
                    _tcs.TrySetResult(result);
                    _ = CloseAnimatedAsync();
                };
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape && DataContext is NoMatchFoundViewModel vm &&
                vm.CancelCommand is System.Windows.Input.ICommand cancel && cancel.CanExecute(null))
            {
                cancel.Execute(null);
                e.Handled = true;
            }
        }

        private async Task CloseAnimatedAsync()
        {
            if (_closing) return;
            _closing = true;
            if (_shell != null)
                await PhantomVault.UI.Views.Autofill.AutofillMotion.ExitAsync(_shell);
            try { Close(); } catch (System.InvalidOperationException) { }
        }

        public Task<NoMatchResult> ShowAsync()
        {
            Show();
            return _tcs.Task;
        }
    }
}
