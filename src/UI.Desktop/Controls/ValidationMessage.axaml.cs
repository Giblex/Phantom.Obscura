using Avalonia;
using Avalonia.Controls;

namespace PhantomVault.UI.Desktop.Controls
{
    public partial class ValidationMessage : UserControl
    {
        public static readonly StyledProperty<string?> MessageProperty =
            AvaloniaProperty.Register<ValidationMessage, string?>(nameof(Message));

        public static readonly StyledProperty<bool> IsValidProperty =
            AvaloniaProperty.Register<ValidationMessage, bool>(nameof(IsValid), defaultValue: false);

        public string? Message
        {
            get => GetValue(MessageProperty);
            set => SetValue(MessageProperty, value);
        }

        public bool IsValid
        {
            get => GetValue(IsValidProperty);
            set => SetValue(IsValidProperty, value);
        }

        public ValidationMessage()
        {
            InitializeComponent();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == IsValidProperty)
            {
                UpdateValidationState();
            }
        }

        private void UpdateValidationState()
        {
            var border = this.FindControl<Border>("ValidationBorder");
            var icon = this.FindControl<Control>("ValidationIcon");
            var text = this.FindControl<TextBlock>("MessageText");

            if (border != null && text != null)
            {
                if (IsValid)
                {
                    border.Classes.Add("validation-success");
                    text.Classes.Add("validation-success");
                }
                else
                {
                    border.Classes.Remove("validation-success");
                    text.Classes.Remove("validation-success");
                }
            }
        }
    }
}
