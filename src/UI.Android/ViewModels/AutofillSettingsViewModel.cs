using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PhantomVault.Android.ViewModels
{
    public sealed partial class AutofillSettingsViewModel : BaseViewModel
    {
        [ObservableProperty]
        private bool _isPhantomSetAsAutofillService;

        [ObservableProperty]
        private string _currentAutofillService = "None";

        [ObservableProperty]
        private bool _autofillEnabled = true;

        [ObservableProperty]
        private bool _inlineAutofillEnabled = true;

        [ObservableProperty]
        private bool _savePromptEnabled = true;

        public async Task InitializeAsync()
        {
#if ANDROID
            try
            {
                var context = global::Android.App.Application.Context;
                var autofillManager = (global::Android.Views.Autofill.AutofillManager?)
                    context.GetSystemService("autofill");

                if (autofillManager != null)
                {
                    IsPhantomSetAsAutofillService = autofillManager.IsEnabled &&
                        autofillManager.HasEnabledAutofillServices;
                    CurrentAutofillService = IsPhantomSetAsAutofillService
                        ? "Phantom Obscura"
                        : "Other / None";
                }
            }
            catch
            {
                CurrentAutofillService = "Unknown";
            }
#endif
            await Task.CompletedTask;
        }

        [RelayCommand]
        private void OpenAutofillSystemSettings()
        {
#if ANDROID
            try
            {
                var intent = new global::Android.Content.Intent(global::Android.Provider.Settings.ActionRequestSetAutofillService);
                intent.SetData(global::Android.Net.Uri.Parse("package:com.giblex.phantom.obscura"));
                intent.SetFlags(global::Android.Content.ActivityFlags.NewTask);
                global::Android.App.Application.Context.StartActivity(intent);
            }
            catch
            {
                // Fallback to general autofill settings
                var intent = new global::Android.Content.Intent(global::Android.Provider.Settings.ActionInputMethodSettings);
                intent.SetFlags(global::Android.Content.ActivityFlags.NewTask);
                global::Android.App.Application.Context.StartActivity(intent);
            }
#endif
        }

        [RelayCommand]
        private async Task RefreshStatusAsync()
        {
            await InitializeAsync();
            StatusMessage = "Status refreshed.";
        }
    }
}
