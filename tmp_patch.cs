using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views
{
    public partial class VaultWindow : Window
    {
        public VaultWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void GoBack_Click(object? sender, RoutedEventArgs e)
        {
            // Simply close this window
            Close();
        }

        private void OnItemSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (DataContext is VaultViewModel vm && e.AddedItems.Count > 0)
            {
                var selected = e.AddedItems[0]?.ToString();
                if (!string.IsNullOrEmpty(selected))
                {
                    vm.OpenItemCommand.Execute(selected);
                }
            }
        }

        private async void OpenSettings_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not VaultViewModel vaultVm) return;

            var viewModel = new VaultSettingsViewModel();
            
            // Pass credentials to settings for import/export
            var credentials = vaultVm.GetAllCredentials();
            viewModel.SetCredentials(credentials);
            
            System.Diagnostics.Debug.WriteLine($"[VAULT] Setting up import callback. Current vault has {credentials.Count} credentials");
            
            // Handle credential imports
            viewModel.OnCredentialsImported = async (importedCreds) =>
            {
                System.Diagnostics.Debug.WriteLine($"[VAULT] OnCredentialsImported callback triggered with {importedCreds.Count} credentials");
                
                // Ensure we're on the UI thread for all operations
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    int addedCount = 0;
                    // Update vault with imported credentials
                    foreach (var cred in importedCreds)
                    {
                        // Get CURRENT credentials list (not stale snapshot)
                        var currentCredentials = vaultVm.GetAllCredentials();
                        
                        // Check if credential already exists
                        bool isDuplicate = currentCredentials.Any(existing => 
                            existing.Title.Equals(cred.Title, StringComparison.OrdinalIgnoreCase));
                        
                        if (!isDuplicate)
                        {
                            System.Diagnostics.Debug.WriteLine($"[VAULT] Adding credential: {cred.Title}");
                            vaultVm.AddCredentialFromImport(cred);
                            addedCount++;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[VAULT] Skipping duplicate: {cred.Title}");
                        }
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"[VAULT] Import callback complete. Added {addedCount} credentials");
                    
                    // Show confirmation
                    if (addedCount > 0)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                        {
                            var dialog = new Avalonia.Controls.Window
                            {
                                Title = "Import Complete",
                                Width = 300,
                                Height = 150,
                                Content = new Avalonia.Controls.TextBlock
                                {
                                    Text = $"Successfully imported {addedCount} credentials!",
                                    Margin = new Avalonia.Thickness(20),
                                    TextAlignment = Avalonia.Media.TextAlignment.Center,
                                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                                }
                            };
                            await dialog.ShowDialog(this);
                        });
                    }
                });
            };
            
            var window = new VaultSettingsWindow
            {
                DataContext = viewModel
            };
            viewModel.SetOwnerWindow(window);
            
            await window.ShowDialog(this);
            
            vaultVm.StatusMessage = "Settings updated";
        }

        private async void OpenPasswordGenerator_Click(object? sender, RoutedEventArgs e)
        {
            var viewModel = new PasswordGeneratorViewModel();
            var window = new PasswordGeneratorWindow
            {
                DataContext = viewModel
            };
            
            await window.ShowDialog(this);
            
            // Update status after generator closed
            if (DataContext is VaultViewModel vm)
            {
                vm.StatusMessage = "Password generator closed";
            }
        }

        private async void OpenThemeSettings_Click(object? sender, RoutedEventArgs e)
        {
            var vm = new ThemeSettingsViewModel();
            var win = new ThemeSettingsWindow { DataContext = vm };
            await win.ShowDialog(this);
        }

        private void CloseEditPanel_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is VaultViewModel vm)
            {
                vm.CloseEditPanel();
            }
        }

        private void CloseMobileDetailOverlay_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is VaultViewModel vm)
            {
                // Clear the selection to close the mobile overlay
                vm.ClearSelectedCredentialCommand.Execute().Subscribe();
            }
        }
    }
}
