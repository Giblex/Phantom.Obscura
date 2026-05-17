using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PhantomVault.Android.ViewModels
{
    public sealed partial class AboutViewModel : BaseViewModel
    {
        [ObservableProperty] private string _appVersion = "1.0.0";
        [ObservableProperty] private string _platform = "Android";
        [ObservableProperty] private string _dotNetVersion =
            System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

        public AboutViewModel()
        {
            AppVersion = GetType().Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
            Platform = $"Android {DeviceInfo.Current.VersionString}";
        }

        [RelayCommand]
        private async Task ViewLicensesAsync()
            => await Shell.Current.DisplayAlert(
                "Licenses",
                "Phantom Obscura uses open-source libraries. See the GitHub repository for full license details.",
                "OK");
    }
}
