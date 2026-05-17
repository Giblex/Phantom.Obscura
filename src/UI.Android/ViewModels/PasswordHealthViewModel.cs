using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;

namespace PhantomVault.Android.ViewModels
{
    /// <summary>
    /// ViewModel for the password health analysis page.
    /// Analyses all vault credentials and surfaces weak, reused, and old passwords.
    /// </summary>
    public sealed partial class PasswordHealthViewModel : BaseViewModel
    {
        private readonly PasswordHealthService _healthService;
        private readonly VaultViewModel _vaultVm;

        [ObservableProperty] private PasswordHealthReport? _report;
        [ObservableProperty] private ObservableCollection<string> _weakTitles = new();
        [ObservableProperty] private ObservableCollection<string> _reusedTitles = new();
        [ObservableProperty] private ObservableCollection<string> _oldTitles = new();
        [ObservableProperty] private bool _hasResults;

        public PasswordHealthViewModel(PasswordHealthService healthService, VaultViewModel vaultVm)
        {
            _healthService = healthService;
            _vaultVm = vaultVm;
        }

        [RelayCommand]
        private async Task AnalyseAsync()
        {
            await RunSafeAsync(async () =>
            {
                var credentials = _vaultVm.Credentials;
                if (credentials.Count == 0)
                {
                    StatusMessage = "No credentials to analyse.";
                    return;
                }

                Report = await _healthService.AnalyzeAsync(credentials);
                WeakTitles = new ObservableCollection<string>(Report.WeakTitles);
                ReusedTitles = new ObservableCollection<string>(Report.ReusedTitles);
                OldTitles = new ObservableCollection<string>(Report.OldTitles);
                HasResults = true;
                StatusMessage = null;
            }, "Analysing…");
        }
    }
}
