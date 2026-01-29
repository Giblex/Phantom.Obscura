using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ReactiveUI;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services.Security;

namespace PhantomVault.UI.ViewModels
{
    /// <summary>
    /// View model for the decoy vault preview window.
    /// Displays generated fake credentials for testing the decoy system.
    /// </summary>
    public sealed class DecoyPreviewViewModel : ReactiveObject
    {
        private readonly DecoyVaultService _decoyVaultService;

        public DecoyPreviewViewModel(VaultDatabase? decoyDatabase, DecoyVaultService decoyVaultService)
        {
            _decoyVaultService = decoyVaultService;

            // Extract all credentials from decoy database
            if (decoyDatabase?.Groups != null)
            {
                var allCredentials = decoyDatabase.Groups
                    .Where(g => g.Entries != null)
                    .SelectMany(g => g.Entries!)
                    .ToList();

                DecoyCredentials = new ObservableCollection<Credential>(allCredentials);
                TotalCredentialsText = $"{DecoyCredentials.Count} fake credentials generated";
            }
            else
            {
                DecoyCredentials = new ObservableCollection<Credential>();
                TotalCredentialsText = "No credentials generated";
            }

            CloseCommand = ReactiveCommand.Create(CloseWindow);
        }

        public ObservableCollection<Credential> DecoyCredentials { get; }
        public string TotalCredentialsText { get; }
        public ReactiveCommand<Unit, Unit> CloseCommand { get; }

        private void CloseWindow()
        {
            // Find and close the preview window
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var window = desktop.Windows.FirstOrDefault(w => w is Views.DecoyPreviewWindow);
                window?.Close();
            }
        }
    }
}
