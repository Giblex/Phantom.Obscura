using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using PhantomRecovery.App.ViewModels;
using PhantomRecovery.App.Integration;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.Views
{
    public partial class RecoveryPanel : UserControl
    {
        public event EventHandler? CloseRequested;
        private MainViewModel? _viewModel;
        private static string? _cachedUsbPath;
        private static DateTime _cacheExpiry = DateTime.MinValue;

        public RecoveryPanel()
        {
            InitializeComponent();
            InitializeRecoveryView();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private async void InitializeRecoveryView()
        {
            // Show progress indicator
            var progressBorder = this.FindControl<Border>("ProgressOverlay");
            if (progressBorder != null)
            {
                progressBorder.IsVisible = true;
            }

            try
            {
                RecoveryDeveloperMode.Log("InitializeRecoveryView started");

                var recoveryView = this.FindControl<PhantomRecovery.App.Views.MainView>("RecoveryView");
                if (recoveryView != null)
                {
                    // Use developer mode settings if enabled, otherwise use production settings
                    VaultLaunchOptions options;
                    string vaultPath;
                    string? usbPath = null;

                    if (RecoveryDeveloperMode.IsEnabled)
                    {
                        RecoveryDeveloperMode.Log("Using developer mode configuration");
                        options = RecoveryDeveloperMode.GetDeveloperLaunchOptions();
                        vaultPath = RecoveryDeveloperMode.DeveloperVaultPath;
                        usbPath = RecoveryDeveloperMode.DeveloperUsbPath;
                    }
                    else
                    {
                        RecoveryDeveloperMode.Log("Using production configuration");
                        vaultPath = System.IO.Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "PhantomRecoveryVault"
                        );

                        options = new VaultLaunchOptions
                        {
                            VaultPath = vaultPath,
                            MasterSecret = "phantom-recovery-master",
                            RecoveryPin = "0000",
                            AutoOpenOnLaunch = true,
                            CreateIfMissing = true
                        };

                        // Try to detect USB drive (production mode)
                        usbPath = TryDetectUsbDrive();
                    }

                    // Create vault with USB binding if USB is available
                    if (!string.IsNullOrEmpty(usbPath) && System.IO.Directory.Exists(usbPath))
                    {
                        RecoveryDeveloperMode.Log($"Creating vault with USB binding at: {usbPath}");

                        var integratedService = new IntegratedRecoveryService();
                        await integratedService.CreateRecoveryVaultWithBindingAsync(
                            vaultPath,
                            usbPath,
                            options.MasterSecret ?? "phantom-recovery-master",
                            options.RecoveryPin ?? "0000",
                            null
                        );

                        RecoveryDeveloperMode.Log("Vault created with USB binding successfully");
                    }
                    else
                    {
                        RecoveryDeveloperMode.Log("No USB path available, creating vault without binding");
                    }

                    _viewModel = new MainViewModel(options);
                    recoveryView.DataContext = _viewModel;
                    
                    // Add fake recovery entries for developer mode
                    if (RecoveryDeveloperMode.IsEnabled)
                    {
                        RecoveryDeveloperMode.Log("Adding fake recovery entries for developer mode");
                        await AddFakeRecoveryEntriesAsync(_viewModel);
                    }
                    
                    RecoveryDeveloperMode.Log("RecoveryView initialized successfully");

                    // Hide progress indicator
                    if (progressBorder != null)
                    {
                        progressBorder.IsVisible = false;
                    }

                    // Toast notification removed per user request
                    // if (usbBindingCreated)
                    // {
                    //     ShowToastNotification("USB Binding Created", "Recovery vault successfully bound to USB drive", true);
                    // }
                }
            }
            catch (Exception ex)
            {
                // Hide progress indicator on error
                if (progressBorder != null)
                {
                    progressBorder.IsVisible = false;
                }

                RecoveryDeveloperMode.Log($"Error during initialization: {ex.Message}");
                await ShowErrorAndClose("Recovery Initialization Failed", 
                    $"Failed to initialize PhantomRecovery:\n\n{ex.Message}");
            }
        }

        private string? TryDetectUsbDrive()
        {
            // Check cache first (60-second TTL)
            if (DateTime.UtcNow < _cacheExpiry && _cachedUsbPath != null)
            {
                RecoveryDeveloperMode.Log($"Using cached USB path: {_cachedUsbPath}");
                return _cachedUsbPath;
            }

            try
            {
                var drives = System.IO.DriveInfo.GetDrives();
                foreach (var drive in drives)
                {
                    if (drive.DriveType == System.IO.DriveType.Removable && drive.IsReady)
                    {
                        // Check if this drive has a PhantomVault manifest
                        var manifestPath = System.IO.Path.Combine(drive.RootDirectory.FullName, ".phantom_manifest");
                        if (System.IO.File.Exists(manifestPath))
                        {
                            RecoveryDeveloperMode.Log($"Found USB drive with manifest: {drive.RootDirectory.FullName}");
                            
                            // Cache the result for 60 seconds
                            _cachedUsbPath = drive.RootDirectory.FullName;
                            _cacheExpiry = DateTime.UtcNow.AddSeconds(60);
                            
                            return drive.RootDirectory.FullName;
                        }
                    }
                }
                
                RecoveryDeveloperMode.Log("No USB drive with manifest detected");
                
                // Clear cache if no drive found
                _cachedUsbPath = null;
                _cacheExpiry = DateTime.MinValue;
            }
            catch (Exception ex)
            {
                RecoveryDeveloperMode.Log($"Error detecting USB: {ex.Message}");
            }

            return null;
        }

        private void Close_Click(object? sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private async Task ShowErrorAndClose(string title, string message)
        {
            var window = TopLevel.GetTopLevel(this) as Window;
            if (window != null)
            {
                var dialog = new Window
                {
                    Title = title,
                    Width = 500,
                    Height = 250,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false
                };

                var panel = new StackPanel
                {
                    Margin = new Avalonia.Thickness(20),
                    Spacing = 15
                };

                panel.Children.Add(new TextBlock
                {
                    Text = title,
                    FontSize = 18,
                    FontWeight = Avalonia.Media.FontWeight.Bold
                });

                panel.Children.Add(new TextBlock
                {
                    Text = message,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                });

                var closeButton = new Button
                {
                    Content = "Close",
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Padding = new Avalonia.Thickness(30, 8)
                };

                closeButton.Click += (s, e) =>
                {
                    dialog.Close();
                    CloseRequested?.Invoke(this, EventArgs.Empty);
                };

                panel.Children.Add(closeButton);
                dialog.Content = panel;

                await dialog.ShowDialog(window);
            }
            else
            {
                // Fallback if no window found
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        private void ShowToastNotification(string title, string message, bool isSuccess)
        {
            var window = TopLevel.GetTopLevel(this) as Window;
            if (window == null) return;

            var toast = new Border
            {
                Background = isSuccess 
                    ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(34, 139, 34))
                    : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(220, 53, 69)),
                CornerRadius = new Avalonia.CornerRadius(8),
                Padding = new Avalonia.Thickness(16, 12),
                Margin = new Avalonia.Thickness(20),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
                BoxShadow = new Avalonia.Media.BoxShadows(
                    new Avalonia.Media.BoxShadow
                    {
                        Blur = 20,
                        Color = Avalonia.Media.Colors.Black,
                        OffsetY = 4
                    })
            };

            var panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = Avalonia.Media.FontWeight.Bold,
                FontSize = 14,
                Foreground = Avalonia.Media.Brushes.White
            });
            panel.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 12,
                Foreground = Avalonia.Media.Brushes.White,
                Opacity = 0.9
            });

            toast.Child = panel;

            // Add to window's overlay (if available) or main content
            if (window.Content is Panel mainPanel)
            {
                mainPanel.Children.Add(toast);

                // Auto-remove after 4 seconds with fade animation
                var timer = new System.Threading.Timer(_ =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (mainPanel.Children.Contains(toast))
                        {
                            mainPanel.Children.Remove(toast);
                        }
                    });
                }, null, 4000, System.Threading.Timeout.Infinite);
            }
        }

        /// <summary>
        /// Adds fake recovery entries for developer testing purposes
        /// </summary>
        private async Task AddFakeRecoveryEntriesAsync(MainViewModel viewModel)
        {
            try
            {
                // Wait for vault to be ready
                await Task.Delay(500);

                var fakeEntries = new[]
                {
                    new { Category = "Master Keys", Name = "PhantomVault Master Key #1", Type = "Recovery Key", Content = "PV-MK-2024-A1B2C3D4-E5F6G7H8-I9J0K1L2", Notes = "Primary master recovery key - Keep offline" },
                    new { Category = "Master Keys", Name = "PhantomVault Master Key #2", Type = "Recovery Key", Content = "PV-MK-2024-M3N4O5P6-Q7R8S9T0-U1V2W3X4", Notes = "Secondary master recovery key - USB backup" },
                    new { Category = "Master Keys", Name = "Emergency Recovery Code", Type = "Recovery Code", Content = "EMRG-5Y6Z-7A8B-9C0D-1E2F", Notes = "Use only if both master keys are lost" },
                    
                    new { Category = "Hardware Keys", Name = "YubiKey 5 NFC", Type = "Hardware Token", Content = "Serial: 12345678\nSlot 1: HOTP\nSlot 2: Static Password", Notes = "Registered 2024-01-15 | Primary 2FA device" },
                    new { Category = "Hardware Keys", Name = "Titan Security Key", Type = "Hardware Token", Content = "Serial: GOOG-87654321\nProtocol: FIDO2/U2F", Notes = "Backup 2FA device - Keep in safe" },
                    
                    new { Category = "Backup Codes", Name = "Google Account Recovery", Type = "Backup Codes", Content = "1. 4729-8356\n2. 9283-1047\n3. 5628-3940\n4. 7401-5829\n5. 2938-4756", Notes = "Generated 2024-12-01 | 5 remaining" },
                    new { Category = "Backup Codes", Name = "GitHub 2FA Codes", Type = "Backup Codes", Content = "1. gh_A1B2C3D4E5F6\n2. gh_G7H8I9J0K1L2\n3. gh_M3N4O5P6Q7R8", Notes = "Generated 2024-11-20 | Never used" },
                    new { Category = "Backup Codes", Name = "Microsoft Account Recovery", Type = "Backup Codes", Content = "1. MSFT-9384-2847\n2. MSFT-5729-3048\n3. MSFT-8264-1937", Notes = "Print and store securely" },
                    
                    new { Category = "SSH Keys", Name = "Production Server Key", Type = "SSH Private Key", Content = "-----BEGIN OPENSSH PRIVATE KEY-----\nb3BlbnNzaC1rZXktdjEAAAAA...FAKE...KEY\n-----END OPENSSH PRIVATE KEY-----", Notes = "prod-server-01.example.com | Expires 2025-06-30" },
                    new { Category = "SSH Keys", Name = "Git Repository Key", Type = "SSH Private Key", Content = "-----BEGIN OPENSSH PRIVATE KEY-----\nb3BlbnNzaC1rZXktdjEAAAAA...FAKE...GIT\n-----END OPENSSH PRIVATE KEY-----", Notes = "github.com/phantom-vault | Read/Write access" },
                    
                    new { Category = "Certificates", Name = "Code Signing Certificate", Type = "X.509 Certificate", Content = "Subject: CN=PhantomVault, O=PhantomSoft\nIssuer: DigiCert\nSerial: 0A1B2C3D4E5F6789\nExpires: 2025-12-31", Notes = "Used for signing releases | Passphrase stored separately" },
                    new { Category = "Certificates", Name = "SSL/TLS Certificate", Type = "X.509 Certificate", Content = "Subject: CN=*.phantomvault.io\nIssuer: Let's Encrypt\nSerial: 9F8E7D6C5B4A3210\nExpires: 2025-03-15", Notes = "Wildcard cert for all subdomains" },
                    
                    new { Category = "Recovery Phrases", Name = "Crypto Wallet Seed", Type = "BIP39 Seed Phrase", Content = "abandon ability able about above absent absorb abstract absurd abuse access accident account accuse achieve acid acoustic acquire across act action actor actress actual", Notes = "24-word BIP39 | DO NOT SHARE | Hardware wallet backup" },
                    new { Category = "Recovery Phrases", Name = "Password Manager Seed", Type = "Recovery Phrase", Content = "correct horse battery staple mountain river ocean forest", Notes = "Emergency recovery for password vault" },
                    
                    new { Category = "API Keys", Name = "AWS Root Account", Type = "API Credentials", Content = "Access Key: AKIA...FAKE...1234\nSecret Key: wJal...FAKE...xyz/abcd\nAccount ID: 123456789012", Notes = "Root credentials - Use IAM roles instead when possible" },
                    new { Category = "API Keys", Name = "Stripe Production API", Type = "API Key", Content = "sk_live_51...FAKE...prodkey", Notes = "Payment processing | Restricted to production domain" },
                    
                    new { Category = "Emergency Contacts", Name = "Security Team Lead", Type = "Contact", Content = "Name: Alex Thompson\nEmail: alex.thompson@phantomvault.io\nPhone: +1 (555) 123-4567\nSignal: +1 (555) 765-4321", Notes = "Primary security contact | Available 24/7" },
                    new { Category = "Emergency Contacts", Name = "Legal Counsel", Type = "Contact", Content = "Firm: CyberLaw Associates\nContact: Sarah Chen\nEmail: schen@cyberlaw.com\nPhone: +1 (555) 987-6543", Notes = "For breach notifications and compliance" },
                    
                    new { Category = "Vault Access", Name = "Safe Deposit Box #247", Type = "Physical Access", Content = "Bank: First National\nLocation: 123 Main St, Suite 500\nBox #: 247\nKey Location: Home safe", Notes = "Contains USB backup drive and printed recovery codes" },
                    new { Category = "Vault Access", Name = "Home Safe Combination", Type = "Physical Access", Content = "Model: SentrySafe X125\nCombination: 45-23-67-89\nOverride Code: 8273", Notes = "Changed 2024-10-01 | Battery backup last tested OK" }
                };

                RecoveryDeveloperMode.Log($"Creating {fakeEntries.Length} fake recovery entries");

                // Note: This is a simplified implementation. In reality, you'd need to:
                // 1. Access the recovery vault through the ViewModel
                // 2. Add artifacts using the proper PhantomRecovery API
                // 3. Handle encryption and storage properly
                
                // For now, just log that we would add these entries
                foreach (var entry in fakeEntries)
                {
                    RecoveryDeveloperMode.Log($"Would add: [{entry.Category}] {entry.Name} ({entry.Type})");
                }

                RecoveryDeveloperMode.Log("Fake entries added successfully (logged only - actual implementation requires vault API integration)");
            }
            catch (Exception ex)
            {
                RecoveryDeveloperMode.Log($"Error adding fake entries: {ex.Message}");
            }
        }
    }
}

