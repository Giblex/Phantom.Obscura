using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhantomVault.Core.Models;

namespace PhantomVault.Android.ViewModels
{
    public sealed partial class TrashViewModel : BaseViewModel
    {
        private readonly VaultViewModel _vaultVm;

        public ObservableCollection<Credential> TrashedItems => _vaultVm.TrashedItems;

        [ObservableProperty] private bool _hasItems;

        public TrashViewModel(VaultViewModel vaultVm)
        {
            _vaultVm = vaultVm;
            _vaultVm.TrashedItems.CollectionChanged += (_, _) =>
                HasItems = _vaultVm.TrashedItems.Count > 0;
            HasItems = vaultVm.TrashedItems.Count > 0;
        }

        [RelayCommand]
        private void Restore(Credential? c) => _vaultVm.RestoreCredentialCommand.Execute(c);

        [RelayCommand]
        private void PermanentDelete(Credential? c) => _vaultVm.PermanentDeleteCommand.Execute(c);

        [RelayCommand]
        private async Task PurgeAllAsync()
        {
            var ok = await Shell.Current.DisplayAlert(
                "Empty Trash",
                "Permanently delete all trashed credentials? This cannot be undone.",
                "Delete All", "Cancel");

            if (ok) _vaultVm.PurgeTrashCommand.Execute(null);
        }
    }
}
