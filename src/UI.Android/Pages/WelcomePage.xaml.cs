using PhantomVault.Android.ViewModels;

namespace PhantomVault.Android.Pages;

public partial class WelcomePage : ContentPage
{
    private readonly WelcomeViewModel _vm;

    public WelcomePage(WelcomeViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
        _vm.NavigateRequested += OnNavigateRequested;
    }

    private async void OnNavigateRequested(string route)
    {
        // Push the navigation onto the UI thread, and tolerate redundant requests.
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try { await Shell.Current.GoToAsync(route); }
            catch { /* swallow — gate may fire repeatedly while user is mid-flow */ }
        });
    }

    protected override bool OnBackButtonPressed() => true; // No back from the gate.
}
