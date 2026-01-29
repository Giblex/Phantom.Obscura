using Avalonia.Controls;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PhantomVault.UI.Desktop.Controls
{
    public partial class ThemeToggleButton : UserControl, INotifyPropertyChanged
    {
        private bool _isDarkMode = true;
        private string _themeLabel = "Dark";

        public new event PropertyChangedEventHandler? PropertyChanged;

        public bool IsDarkMode
        {
            get => _isDarkMode;
            set
            {
                if (_isDarkMode != value)
                {
                    _isDarkMode = value;
                    OnPropertyChanged();
                    ThemeLabel = value ? "Dark" : "Light";
                    // TODO: Call ThemeManagerService to switch theme
                }
            }
        }

        public string ThemeLabel
        {
            get => _themeLabel;
            set
            {
                if (_themeLabel != value)
                {
                    _themeLabel = value;
                    OnPropertyChanged();
                }
            }
        }

        public ThemeToggleButton()
        {
            InitializeComponent();
            DataContext = this;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
