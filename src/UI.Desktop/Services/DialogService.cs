using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using PhantomVault.UI.Models;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Service for displaying dialogs and messages to the user.
    /// </summary>
    public class DialogService
    {
        // Dull Navy Blue color for all dialog backgrounds (except error/success which stay red/green)
        private static readonly SolidColorBrush DullNavyBrush = new SolidColorBrush(Color.Parse("#3D4F61"));
        /// <summary>
        /// Shows an informational message dialog.
        /// </summary>
        public async Task ShowInfoAsync(string title, string message, Window? owner = null)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 450,
                Height = 250,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                Background = DullNavyBrush
            };

            var panel = new StackPanel { Margin = new Avalonia.Thickness(20) };

            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 18,
                FontWeight = Avalonia.Media.FontWeight.Normal,
                Foreground = Avalonia.Media.Brushes.White,
                Margin = new Avalonia.Thickness(0, 0, 0, 15)
            });

            panel.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 14,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Foreground = Avalonia.Media.Brushes.LightGray,
                Margin = new Avalonia.Thickness(0, 0, 0, 20)
            });

            var okButton = new Button
            {
                Width = 100,
                Height = 35,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Background = new SolidColorBrush(Color.Parse("#6B8CAE")), // Pastel blue
                Foreground = Avalonia.Media.Brushes.White
            };
            okButton.Content = new TextBlock { Text = "OK" };

            okButton.Click += (s, e) => dialog.Close();
            panel.Children.Add(okButton);

            dialog.Content = panel;

            if (owner != null)
                await dialog.ShowDialog(owner);
            else
                dialog.Show();
        }

        /// <summary>
        /// Shows a warning message dialog.
        /// </summary>
        public async Task ShowWarningAsync(string title, string message, Window? owner = null)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 450,
                Height = 250,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                Background = DullNavyBrush
            };

            var panel = new StackPanel { Margin = new Avalonia.Thickness(20) };

            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 18,
                FontWeight = Avalonia.Media.FontWeight.Normal,
                Foreground = Avalonia.Media.Brushes.White,
                Margin = new Avalonia.Thickness(0, 0, 0, 15)
            });

            panel.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 14,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Foreground = Avalonia.Media.Brushes.White,
                Margin = new Avalonia.Thickness(0, 0, 0, 20)
            });

            var okButton = new Button
            {
                Width = 100,
                Height = 35,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Background = new SolidColorBrush(Color.Parse("#6B8CAE")), // Pastel blue
                Foreground = Avalonia.Media.Brushes.White
            };
            okButton.Content = new TextBlock { Text = "OK" };

            okButton.Click += (s, e) => dialog.Close();
            panel.Children.Add(okButton);

            dialog.Content = panel;

            if (owner != null)
                await dialog.ShowDialog(owner);
            else
                dialog.Show();
        }

        /// <summary>
        /// Shows an error message dialog.
        /// </summary>
        public async Task ShowErrorAsync(string title, string message, Window? owner = null)
        {
            try
            {
                var dialog = new Window
                {
                    Title = title ?? "Error",
                    Width = 500,
                    Height = 300,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    Background = Avalonia.Media.Brushes.DarkRed
                };

                var panel = new StackPanel { Margin = new Avalonia.Thickness(20) };

                panel.Children.Add(new TextBlock
                {
                    Text = title ?? "Error",
                    FontSize = 18,
                    FontWeight = Avalonia.Media.FontWeight.Normal,
                    Foreground = Avalonia.Media.Brushes.White,
                    Margin = new Avalonia.Thickness(0, 0, 0, 15)
                });

                panel.Children.Add(new TextBlock
                {
                    Text = message ?? "An unknown error occurred.",
                    FontSize = 14,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Foreground = Avalonia.Media.Brushes.White,
                    Margin = new Avalonia.Thickness(0, 0, 0, 20)
                });

                var okButton = new Button
                {
                    Width = 100,
                    Height = 35,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Background = Avalonia.Media.Brushes.White,
                    Foreground = Avalonia.Media.Brushes.DarkRed,
                    FontWeight = Avalonia.Media.FontWeight.Normal
                };

                // Create TextBlock for button content instead of using Content property directly
                var buttonText = new TextBlock
                {
                    Text = "OK",
                    Foreground = Avalonia.Media.Brushes.DarkRed,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                };
                okButton.Content = buttonText;

                okButton.Click += (s, e) => dialog.Close();
                panel.Children.Add(okButton);

                dialog.Content = panel;

                if (owner != null)
                {
                    try
                    {
                        await dialog.ShowDialog(owner);

                        // After the dialog closes, attempt to reset the owner viewmodel if it supports it.
                        try
                        {
                            if (owner.DataContext is PhantomVault.UI.Services.IResettableOnError resettable)
                            {
                                await resettable.ResetAfterErrorAsync();
                            }
                        }
                        catch
                        {
                            // Best-effort: do not crash the dialog pathway if reset fails.
                        }
                    }
                    catch (System.InvalidCastException)
                    {
                        // Defensive: some callers in rare cases may pass an unexpected object
                        // as the owner (for example when wiring errors occur). Fall back to
                        // showing a non-modal dialog so the user still sees the error instead
                        // of crashing the app. Do not rethrow.
                        dialog.Show();
                    }
                    catch (System.Exception)
                    {
                        // Any other dialog show failure - fall back to non-modal display.
                        dialog.Show();
                    }
                }
                else
                {
                    dialog.Show();
                }
            }
            catch (Exception ex)
            {
                // Last resort: Log to console and show a simple message box
                Console.WriteLine($"CRITICAL ERROR in ShowErrorAsync: {ex.Message}");
                Console.WriteLine($"Original error was - Title: {title}, Message: {message}");
                // Try to show something to the user
                try
                {
                    var fallbackDialog = new Window
                    {
                        Title = "Error",
                        Width = 400,
                        Height = 200,
                        Content = new TextBlock { Text = $"An error occurred: {message}", Margin = new Avalonia.Thickness(20) }
                    };
                    fallbackDialog.Show();
                }
                catch
                {
                    // Complete failure - at least we logged to console
                }
            }
        }

        /// <summary>
        /// Shows a confirmation dialog and returns true when the user accepts.
        /// </summary>
        public async Task<bool> ShowConfirmationAsync(
            string title,
            string message,
            string confirmText = "Continue",
            string cancelText = "Cancel",
            Window? owner = null)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 520,
                Height = 260,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                Background = new SolidColorBrush(Color.Parse("#071018"))
            };

            bool result = false;

            var panel = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 18
            };

            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 20,
                FontWeight = FontWeight.Normal,
                Foreground = Brushes.White
            });

            panel.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.Parse("#C5DDE5")),
                LineHeight = 22
            });

            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var cancelButton = new Button
            {
                Width = 120,
                Height = 40,
                Background = new SolidColorBrush(Color.Parse("#0C1620")),
                BorderBrush = new SolidColorBrush(Color.Parse("#395264")),
                BorderThickness = new Thickness(1),
                Foreground = Brushes.White,
                Content = new TextBlock
                {
                    Text = cancelText,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            var confirmButton = new Button
            {
                Width = 160,
                Height = 40,
                Background = new SolidColorBrush(Color.Parse("#173042")),
                BorderBrush = new SolidColorBrush(Color.Parse("#55C3CF")),
                BorderThickness = new Thickness(1),
                Foreground = Brushes.White,
                Content = new TextBlock
                {
                    Text = confirmText,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            cancelButton.Click += (_, _) =>
            {
                result = false;
                dialog.Close();
            };

            confirmButton.Click += (_, _) =>
            {
                result = true;
                dialog.Close();
            };

            buttonRow.Children.Add(cancelButton);
            buttonRow.Children.Add(confirmButton);
            panel.Children.Add(buttonRow);

            dialog.Content = panel;

            if (owner != null)
                await dialog.ShowDialog(owner);
            else
                dialog.Show();

            return result;
        }

        /// <summary>
        /// Shows a success message dialog.
        /// </summary>
        public async Task ShowSuccessAsync(string title, string message, Window? owner = null)
        {
            // If an owner is provided, scale the success dialog to a large portion of the owner
            double desiredWidth = 900;
            double desiredHeight = 600;
            if (owner != null)
            {
                try
                {
                    // Use 90% of owner's width and 65% of owner's height, but keep minima
                    desiredWidth = System.Math.Max(800, owner.Width * 0.9);
                    desiredHeight = System.Math.Max(520, owner.Height * 0.65);
                }
                catch
                {
                    // ignore if owner dimensions not available
                }
            }

            var dialog = new Window
            {
                Title = title,
                Width = desiredWidth,
                Height = desiredHeight,
                MinWidth = 700,
                MinHeight = 420,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                CanResize = true,
                Background = DullNavyBrush
            };

            // generous padding so content has breathing room and can expand
            var padding = new Avalonia.Thickness(32);
            var panel = new StackPanel { Margin = padding };

            panel.Children.Add(new TextBlock
            {
                Text = "✅ " + title,
                FontSize = 20,
                FontWeight = Avalonia.Media.FontWeight.Bold,
                Foreground = Avalonia.Media.Brushes.LightGreen,
                Margin = new Avalonia.Thickness(0, 0, 0, 18)
            });

            var messageBlock = new TextBlock
            {
                Text = message,
                FontSize = 15,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Foreground = Avalonia.Media.Brushes.White,
                Margin = new Avalonia.Thickness(0, 0, 0, 24)
            };

            // Allow the message to use most of the dialog width
            try
            {
                messageBlock.MaxWidth = dialog.Width - (padding.Left + padding.Right) - 20;
            }
            catch { }

            panel.Children.Add(messageBlock);

            var okButton = new Button
            {
                Width = 120,
                Height = 40,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2C3E50")),
                Foreground = Avalonia.Media.Brushes.White,
                FontSize = 14
            };
            okButton.Content = new TextBlock { Text = "OK" };

            okButton.Click += (s, e) => dialog.Close();
            panel.Children.Add(okButton);

            var scrollViewer = new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            dialog.Content = scrollViewer;

            if (owner != null)
            {
                try
                {
                    await dialog.ShowDialog(owner);
                }
                catch
                {
                    dialog.Show();
                }
            }
            else
            {
                dialog.Show();
            }
        }

        public async Task ShowImportSummaryAsync(string title, ImportResult result, Window? owner = null)
        {
            double desiredWidth = 840;
            double desiredHeight = 640;

            if (owner != null)
            {
                try
                {
                    desiredWidth = Math.Max(760, owner.Width * 0.75);
                    desiredHeight = Math.Max(560, owner.Height * 0.7);
                }
                catch
                {
                    // owner size may not be initialised yet
                }
            }

            var dialog = new Window
            {
                Title = title,
                Width = desiredWidth,
                Height = desiredHeight,
                MinWidth = 720,
                MinHeight = 520,
                CanResize = false,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                Background = DullNavyBrush
            };

            var container = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0)
            };

            var tile = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#2B3C4D")),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(36),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 720
            };

            var tileStack = new StackPanel
            {
                Spacing = 18,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var headerIcon = new Image
            {
                Source = LoadIconAsset("Assets/Icons/PO UI/Information empty (2)/Information empty (2)_electric_blue.png"),
                Width = 72,
                Height = 72,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            tileStack.Children.Add(headerIcon);
            tileStack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 28,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            tileStack.Children.Add(new TextBlock
            {
                Text = $"{result.SuccessCount} of {result.TotalProcessed} items imported",
                FontSize = 16,
                Foreground = Brushes.LightGray,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            var metricsPanel = new StackPanel
            {
                Spacing = 12,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            metricsPanel.Children.Add(CreateMetricRow(
                "Assets/Icons/PO UI/list view/list view_teal.png",
                "Total Processed",
                result.TotalProcessed,
                "records analysed"));

            metricsPanel.Children.Add(CreateMetricRow(
                "Assets/Icons/PO UI/Add/Add_teal.png",
                "Successfully Imported",
                result.SuccessCount,
                "credentials added to your vault"));

            if (result.DuplicateCount > 0)
            {
                metricsPanel.Children.Add(CreateMetricRow(
                    "Assets/Icons/PO UI/Copy to clipboard 2/Copy to clipboard 2_teal.png",
                    "Duplicates Detected",
                    result.DuplicateCount,
                    "handled during import"));
            }

            if (result.WarningCount > 0)
            {
                metricsPanel.Children.Add(CreateMetricRow(
                    "Assets/Icons/PO UI/Information empty (2)/Information empty (2)_golden_pastel_yellow.png",
                    "Validation Warnings",
                    result.WarningCount,
                    "review recommended"));
            }

            if (result.ErrorCount > 0)
            {
                metricsPanel.Children.Add(CreateMetricRow(
                    "Assets/Icons/PO UI/Information empty (2)/Information empty (2)_deeper_pink_red.png",
                    "Errors",
                    result.ErrorCount,
                    "items skipped"));
            }

            tileStack.Children.Add(metricsPanel);

            var warningItems = result.Warnings.Take(4).ToList();
            if (warningItems.Count > 0)
            {
                var warningsBorder = new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#1F2C38")),
                    CornerRadius = new CornerRadius(14),
                    Padding = new Thickness(18),
                    Margin = new Thickness(0, 12, 0, 0)
                };

                var warningsStack = new StackPanel
                {
                    Spacing = 10
                };

                warningsStack.Children.Add(new TextBlock
                {
                    Text = "Top Validation Notes",
                    FontSize = 15,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brushes.LightGoldenrodYellow
                });

                foreach (var warning in warningItems)
                {
                    warningsStack.Children.Add(CreateInfoRow(
                        "Assets/Icons/PO UI/Information empty (2)/Information empty (2)_golden_pastel_yellow.png",
                        warning));
                }

                if (result.Warnings.Count > warningItems.Count)
                {
                    warningsStack.Children.Add(new TextBlock
                    {
                        Text = $"{result.Warnings.Count - warningItems.Count} additional warnings not shown.",
                        FontSize = 13,
                        Foreground = Brushes.LightGray
                    });
                }

                warningsBorder.Child = warningsStack;
                tileStack.Children.Add(warningsBorder);
            }

            var errorItems = result.Errors.Take(3).ToList();
            if (errorItems.Count > 0)
            {
                var errorsBorder = new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#2F1F28")),
                    CornerRadius = new CornerRadius(14),
                    Padding = new Thickness(18),
                    Margin = new Thickness(0, 12, 0, 0)
                };

                var errorsStack = new StackPanel
                {
                    Spacing = 10
                };

                errorsStack.Children.Add(new TextBlock
                {
                    Text = "Skipped Items",
                    FontSize = 15,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brushes.MistyRose
                });

                foreach (var error in errorItems)
                {
                    errorsStack.Children.Add(CreateInfoRow(
                        "Assets/Icons/PO UI/Information empty (2)/Information empty (2)_deeper_pink_red.png",
                        error));
                }

                if (result.Errors.Count > errorItems.Count)
                {
                    errorsStack.Children.Add(new TextBlock
                    {
                        Text = $"{result.Errors.Count - errorItems.Count} additional errors not shown.",
                        FontSize = 13,
                        Foreground = Brushes.LightGray
                    });
                }

                errorsBorder.Child = errorsStack;
                tileStack.Children.Add(errorsBorder);
            }

            var okButton = new Button
            {
                Width = 140,
                Height = 42,
                Background = new SolidColorBrush(Color.Parse("#2C3E50")),
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 18, 0, 0)
            };
            okButton.Content = new TextBlock { Text = "OK", FontSize = 14 };
            okButton.Click += (s, e) => dialog.Close();

            tileStack.Children.Add(okButton);

            tile.Child = tileStack;
            container.Children.Add(tile);
            dialog.Content = container;

            if (owner != null)
            {
                try
                {
                    await dialog.ShowDialog(owner);
                }
                catch
                {
                    dialog.Show();
                }
            }
            else
            {
                dialog.Show();
            }
        }

        private static Control CreateMetricRow(string iconPath, string label, int value, string detail)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 16,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            panel.Children.Add(new Image
            {
                Source = LoadIconAsset(iconPath),
                Width = 40,
                Height = 40
            });

            var textStack = new StackPanel
            {
                Spacing = 2,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            textStack.Children.Add(new TextBlock
            {
                Text = value.ToString("N0"),
                FontSize = 20,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White
            });

            textStack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 14,
                Foreground = Brushes.LightGray
            });

            textStack.Children.Add(new TextBlock
            {
                Text = detail,
                FontSize = 12,
                Foreground = Brushes.Gray
            });

            panel.Children.Add(textStack);
            return panel;
        }

        private static Control CreateInfoRow(string iconPath, string text)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            row.Children.Add(new Image
            {
                Source = LoadIconAsset(iconPath),
                Width = 24,
                Height = 24
            });

            row.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 13,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 520
            });

            return row;
        }

        private static IImage LoadIconAsset(string relativePath)
        {
            // URL encode all spaces in the path for avares:// URI
            var encodedPath = relativePath.Replace(" ", "%20").Replace("\\", "/");
            var uri = new Uri($"avares://PhantomVault.UI/{encodedPath}");
            using var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }

        /// <summary>
        /// Shows a confirmation dialog with Yes/No buttons.
        /// </summary>
        public async Task<bool> ShowConfirmationAsync(string title, string message, Window? owner = null)
        {
            var result = false;
            var dialog = new Window
            {
                Title = title,
                Width = 450,
                Height = 250,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                Background = DullNavyBrush
            };

            var panel = new StackPanel { Margin = new Avalonia.Thickness(20) };

            panel.Children.Add(new TextBlock
            {
                Text = "❓ " + title,
                FontSize = 18,
                FontWeight = Avalonia.Media.FontWeight.Bold,
                Foreground = Avalonia.Media.Brushes.White,
                Margin = new Avalonia.Thickness(0, 0, 0, 15)
            });

            panel.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 14,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Foreground = Avalonia.Media.Brushes.LightGray,
                Margin = new Avalonia.Thickness(0, 0, 0, 20)
            });

            var buttonPanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Spacing = 10
            };

            var yesButton = new Button
            {
                Width = 100,
                Height = 35,
                Background = new SolidColorBrush(Color.Parse("#6B8CAE")), // Pastel blue
                Foreground = Avalonia.Media.Brushes.White
            };
            yesButton.Content = new TextBlock { Text = "Yes" };

            yesButton.Click += (s, e) =>
            {
                result = true;
                dialog.Close();
            };

            var noButton = new Button
            {
                Width = 100,
                Height = 35,
                Background = new SolidColorBrush(Color.Parse("#5A6C7E")), // Darker navy
                Foreground = Avalonia.Media.Brushes.White
            };
            noButton.Content = new TextBlock { Text = "No" };

            noButton.Click += (s, e) =>
            {
                result = false;
                dialog.Close();
            };

            buttonPanel.Children.Add(yesButton);
            buttonPanel.Children.Add(noButton);
            panel.Children.Add(buttonPanel);

            dialog.Content = panel;

            if (owner != null)
            {
                try
                {
                    await dialog.ShowDialog(owner);
                }
                catch
                {
                    dialog.Show();
                }
            }
            else
            {
                dialog.Show();
            }

            return result;
        }

        /// <summary>
        /// Options when deleting a category: move credentials to another category, delete them, or move to Trash.
        /// Returns a tuple of the chosen action and an optional target category name when action == Move.
        /// </summary>
        public enum CategoryDeleteAction
        {
            Cancel,
            Move,
            Delete,
            MoveToTrash
        }

        public async Task<(CategoryDeleteAction Action, string? TargetCategory)> ShowCategoryDeleteOptionsAsync(string categoryName, System.Collections.Generic.List<string> availableTargetCategories, Window? owner = null)
        {
            var dialog = new Window
            {
                Title = "Category deletion options",
                Width = 520,
                Height = 260,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                Background = DullNavyBrush
            };

            var panel = new StackPanel { Margin = new Avalonia.Thickness(14), Spacing = 10 };
            panel.Children.Add(new TextBlock
            {
                Text = $"What should happen to credentials in '{categoryName}'?",
                FontSize = 16,
                FontWeight = Avalonia.Media.FontWeight.Bold,
                Foreground = Avalonia.Media.Brushes.White,
                Margin = new Avalonia.Thickness(0, 0, 0, 6)
            });

            var combo = new ComboBox { Width = 360 };
            combo.ItemsSource = availableTargetCategories;
            combo.SelectedIndex = availableTargetCategories.Count > 0 ? 0 : -1;
            panel.Children.Add(new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Move to:", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Avalonia.Media.Brushes.LightGray },
                    combo
                }
            });

            var buttonPanel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8 };

            var moveBtn = new Button { Width = 100, Height = 36, Background = new SolidColorBrush(Color.Parse("#6B8CAE")), Foreground = Avalonia.Media.Brushes.White };
            moveBtn.Content = new TextBlock { Text = "Move" };
            var deleteBtn = new Button { Width = 100, Height = 36, Background = new SolidColorBrush(Color.Parse("#A85A5A")), Foreground = Avalonia.Media.Brushes.White };
            deleteBtn.Content = new TextBlock { Text = "Delete" };
            var trashBtn = new Button { Width = 140, Height = 36, Background = new SolidColorBrush(Color.Parse("#7A6C5A")), Foreground = Avalonia.Media.Brushes.White };
            trashBtn.Content = new TextBlock { Text = "Move to Trash" };
            var cancelBtn = new Button { Width = 100, Height = 36, Background = new SolidColorBrush(Color.Parse("#5A6C7E")), Foreground = Avalonia.Media.Brushes.White };
            cancelBtn.Content = new TextBlock { Text = "Cancel" };

            (CategoryDeleteAction Action, string? TargetCategory) result = (CategoryDeleteAction.Cancel, null);

            moveBtn.Click += (s, e) =>
            {
                var target = combo.SelectedItem as string;
                if (string.IsNullOrEmpty(target))
                {
                    // ignore - keep dialog open
                    return;
                }
                result = (CategoryDeleteAction.Move, target);
                dialog.Close();
            };

            deleteBtn.Click += (s, e) =>
            {
                result = (CategoryDeleteAction.Delete, null);
                dialog.Close();
            };

            trashBtn.Click += (s, e) =>
            {
                result = (CategoryDeleteAction.MoveToTrash, null);
                dialog.Close();
            };

            cancelBtn.Click += (s, e) =>
            {
                result = (CategoryDeleteAction.Cancel, null);
                dialog.Close();
            };

            buttonPanel.Children.Add(moveBtn);
            buttonPanel.Children.Add(trashBtn);
            buttonPanel.Children.Add(deleteBtn);
            buttonPanel.Children.Add(cancelBtn);

            panel.Children.Add(buttonPanel);

            dialog.Content = panel;

            if (owner != null)
            {
                try
                {
                    await dialog.ShowDialog(owner);
                }
                catch
                {
                    dialog.Show();
                }
            }
            else
            {
                dialog.Show();
            }

            return result;
        }

        public enum TrashManagerAction
        {
            Cancel,
            Restore,
            Empty
        }

        public enum BulkCategoriesAction
        {
            Cancel,
            Move,
            MoveToDeleted
        }

        public enum CategoryItemsAction
        {
            Cancel,
            Move
        }

        /// <summary>
        /// Shows a Trash manager dialog listing trashed credentials and allows restoring selected items or emptying the trash.
        /// Returns the chosen action and the list of selected credentials (if any).
        /// </summary>
        public async Task<(TrashManagerAction Action, List<SecureTrashRecord> Selected)> ShowTrashManagerAsync(List<SecureTrashRecord> trashedRecords, Window? owner = null)
        {
            var dialog = new Window
            {
                Title = "Trash Manager",
                Width = 700,
                Height = 480,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = true,
                Background = DullNavyBrush
            };

            var panel = new StackPanel { Margin = new Avalonia.Thickness(12), Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = "Trash", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.Bold, Foreground = Avalonia.Media.Brushes.White });

            var checkboxPanel = new StackPanel { Spacing = 6, Margin = new Avalonia.Thickness(0, 8, 0, 8) };
            var checkboxMap = new Dictionary<SecureTrashRecord, CheckBox>();
            foreach (var record in trashedRecords)
            {
                var subtitle = record.ScheduledPurgeUtc.HasValue
                    ? $"Purges {record.ScheduledPurgeUtc.Value:MMM dd}" : "Auto purge disabled";
                var cb = new CheckBox
                {
                    Content = $"{record.Payload.Title} — {record.Payload.Username} ({subtitle})",
                    IsChecked = false,
                    Foreground = Avalonia.Media.Brushes.LightGray
                };
                checkboxPanel.Children.Add(cb);
                checkboxMap[record] = cb;
            }

            var buttonPanel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8 };
            var restoreBtn = new Button { Width = 140, Height = 36, Background = new SolidColorBrush(Color.Parse("#6B8CAE")), Foreground = Avalonia.Media.Brushes.White };
            restoreBtn.Content = new TextBlock { Text = "Restore Selected" };
            var emptyBtn = new Button { Width = 120, Height = 36, Background = new SolidColorBrush(Color.Parse("#A85A5A")), Foreground = Avalonia.Media.Brushes.White };
            emptyBtn.Content = new TextBlock { Text = "Empty Trash" };
            var closeBtn = new Button { Width = 100, Height = 36, Background = new SolidColorBrush(Color.Parse("#5A6C7E")), Foreground = Avalonia.Media.Brushes.White };
            closeBtn.Content = new TextBlock { Text = "Close" };

            (TrashManagerAction Action, List<SecureTrashRecord> Selected) result = (TrashManagerAction.Cancel, new List<SecureTrashRecord>());

            restoreBtn.Click += (s, e) =>
            {
                var selected = new List<SecureTrashRecord>();
                foreach (var kv in checkboxMap)
                {
                    if (kv.Value.IsChecked == true) selected.Add(kv.Key);
                }
                if (selected.Count == 0)
                {
                    // no-op; keep dialog open
                    return;
                }
                result = (TrashManagerAction.Restore, selected);
                dialog.Close();
            };

            emptyBtn.Click += (s, e) =>
            {
                result = (TrashManagerAction.Empty, new List<SecureTrashRecord>(checkboxMap.Keys));
                dialog.Close();
            };

            closeBtn.Click += (s, e) =>
            {
                result = (TrashManagerAction.Cancel, new List<SecureTrashRecord>());
                dialog.Close();
            };

            buttonPanel.Children.Add(restoreBtn);
            buttonPanel.Children.Add(emptyBtn);
            buttonPanel.Children.Add(closeBtn);

            var scroll = new ScrollViewer { Content = checkboxPanel };
            panel.Children.Add(scroll);
            panel.Children.Add(buttonPanel);

            dialog.Content = panel;

            if (owner != null)
            {
                try
                {
                    await dialog.ShowDialog(owner);
                }
                catch
                {
                    dialog.Show();
                }
            }
            else
            {
                dialog.Show();
            }

            return result;
        }

        /// <summary>
        /// Dialog to choose a bulk action for selected categories' items: move to a target category or move to Deleted.
        /// </summary>
        public async Task<(BulkCategoriesAction Action, string? TargetCategory)> ShowBulkCategoryActionAsync(List<string> availableTargetCategories, Window? owner = null)
        {
            var dialog = new Window
            {
                Title = "Bulk Actions",
                Width = 520,
                Height = 220,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                Background = DullNavyBrush
            };

            var panel = new StackPanel { Margin = new Avalonia.Thickness(12), Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = "Apply action to items in the selected categories:", Foreground = Avalonia.Media.Brushes.White });

            var combo = new ComboBox { ItemsSource = availableTargetCategories, SelectedIndex = availableTargetCategories.Count > 0 ? 0 : -1, Width = 360 };
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock { Text = "Move to:", Foreground = Avalonia.Media.Brushes.LightGray, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            row.Children.Add(combo);
            panel.Children.Add(row);

            var buttonPanel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8 };
            var moveBtn = new Button { Width = 100, Height = 36, Background = new SolidColorBrush(Color.Parse("#6B8CAE")), Foreground = Avalonia.Media.Brushes.White };
            moveBtn.Content = new TextBlock { Text = "Move" };
            var deleteBtn = new Button { Width = 140, Height = 36, Background = new SolidColorBrush(Color.Parse("#7A6C5A")), Foreground = Avalonia.Media.Brushes.White };
            deleteBtn.Content = new TextBlock { Text = "Move to Deleted" };
            var cancelBtn = new Button { Width = 100, Height = 36, Background = new SolidColorBrush(Color.Parse("#5A6C7E")), Foreground = Avalonia.Media.Brushes.White };
            cancelBtn.Content = new TextBlock { Text = "Cancel" };

            (BulkCategoriesAction Action, string? TargetCategory) result = (BulkCategoriesAction.Cancel, null);

            moveBtn.Click += (s, e) =>
            {
                var target = combo.SelectedItem as string;
                if (string.IsNullOrEmpty(target)) return;
                result = (BulkCategoriesAction.Move, target);
                dialog.Close();
            };

            deleteBtn.Click += (s, e) => { result = (BulkCategoriesAction.MoveToDeleted, null); dialog.Close(); };
            cancelBtn.Click += (s, e) => { result = (BulkCategoriesAction.Cancel, null); dialog.Close(); };

            buttonPanel.Children.Add(moveBtn);
            buttonPanel.Children.Add(deleteBtn);
            buttonPanel.Children.Add(cancelBtn);
            panel.Children.Add(buttonPanel);

            dialog.Content = panel;
            if (owner != null)
                await dialog.ShowDialog(owner);
            else
                dialog.Show();

            return result;
        }

        /// <summary>
        /// Shows a dialog to multi-select items in a category and choose a target category to move them to.
        /// Returns the chosen action, selected credentials, and the target category (if any).
        /// </summary>
        public async Task<(CategoryItemsAction Action, List<Credential> Selected, string? TargetCategory)> ShowCategoryItemsManagerAsync(
            string categoryName,
            List<Credential> credentialsInCategory,
            List<string> availableTargetCategories,
            Window? owner = null)
        {
            var dialog = new Window
            {
                Title = $"Manage Items — {categoryName}",
                Width = 720,
                Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = true,
                Background = DullNavyBrush
            };

            var panel = new StackPanel { Margin = new Avalonia.Thickness(12), Spacing = 8 };
            var header = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
            header.Children.Add(new TextBlock { Text = $"Items in '{categoryName}'", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.Bold, Foreground = Avalonia.Media.Brushes.White });
            var selectAllBtn = new Button { Width = 110, Height = 30, Background = new SolidColorBrush(Color.Parse("#6B8CAE")), Foreground = Avalonia.Media.Brushes.White };
            selectAllBtn.Content = new TextBlock { Text = "Select All" };
            var selectNoneBtn = new Button { Width = 110, Height = 30, Background = new SolidColorBrush(Color.Parse("#5A6C7E")), Foreground = Avalonia.Media.Brushes.White };
            selectNoneBtn.Content = new TextBlock { Text = "Select None" };
            header.Children.Add(selectAllBtn);
            header.Children.Add(selectNoneBtn);
            panel.Children.Add(header);

            var checkboxPanel = new StackPanel { Spacing = 6, Margin = new Avalonia.Thickness(0, 8, 0, 8) };
            var checkboxMap = new Dictionary<Credential, CheckBox>();
            foreach (var cred in credentialsInCategory)
            {
                var label = string.IsNullOrWhiteSpace(cred.Username) ? cred.Title : $"{cred.Title} — {cred.Username}";
                var cb = new CheckBox { Content = label, IsChecked = false, Foreground = Avalonia.Media.Brushes.LightGray };
                checkboxPanel.Children.Add(cb);
                checkboxMap[cred] = cb;
            }

            var targetLabel = new TextBlock { Text = "Move selected to:", Foreground = Avalonia.Media.Brushes.LightGray };
            var targetCombo = new ComboBox { ItemsSource = availableTargetCategories, SelectedIndex = availableTargetCategories.Count > 0 ? 0 : -1 };

            panel.Children.Add(checkboxPanel);
            panel.Children.Add(new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 8,
                Children = { targetLabel, targetCombo }
            });

            var buttonPanel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8 };
            var moveBtn = new Button { Width = 140, Height = 36, Background = new SolidColorBrush(Color.Parse("#6B8CAE")), Foreground = Avalonia.Media.Brushes.White };
            moveBtn.Content = new TextBlock { Text = "Move Selected" };
            var closeBtn = new Button { Width = 100, Height = 36, Background = new SolidColorBrush(Color.Parse("#5A6C7E")), Foreground = Avalonia.Media.Brushes.White };
            closeBtn.Content = new TextBlock { Text = "Close" };

            (CategoryItemsAction Action, List<Credential> Selected, string? TargetCategory) result = (CategoryItemsAction.Cancel, new List<Credential>(), null);

            selectAllBtn.Click += (s, e) =>
            {
                foreach (var kv in checkboxMap) kv.Value.IsChecked = true;
            };

            selectNoneBtn.Click += (s, e) =>
            {
                foreach (var kv in checkboxMap) kv.Value.IsChecked = false;
            };

            moveBtn.Click += (s, e) =>
            {
                var target = targetCombo.SelectedItem as string;
                if (string.IsNullOrEmpty(target)) return; // keep open
                var selected = new List<Credential>();
                foreach (var kv in checkboxMap)
                {
                    if (kv.Value.IsChecked == true) selected.Add(kv.Key);
                }
                if (selected.Count == 0) return; // keep open
                result = (CategoryItemsAction.Move, selected, target);
                dialog.Close();
            };

            closeBtn.Click += (s, e) => dialog.Close();

            buttonPanel.Children.Add(moveBtn);
            buttonPanel.Children.Add(closeBtn);
            panel.Children.Add(buttonPanel);

            dialog.Content = panel;

            if (owner != null)
                await dialog.ShowDialog(owner);
            else
                dialog.Show();

            return result;
        }

        /// <summary>
        /// Simple select-a-category dialog returning the selected category name or null if cancelled.
        /// </summary>
        public async Task<string?> ShowSelectCategoryAsync(List<string> categories, string prompt, Window? owner = null)
        {
            var dialog = new Window
            {
                Title = "Select Category",
                Width = 480,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                Background = DullNavyBrush
            };

            var panel = new StackPanel { Margin = new Avalonia.Thickness(12), Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = prompt, Foreground = Avalonia.Media.Brushes.White });
            var combo = new ComboBox { ItemsSource = categories, SelectedIndex = categories.Count > 0 ? 0 : -1, Width = 360 };
            panel.Children.Add(combo);

            var buttonPanel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8 };
            var ok = new Button { Width = 100, Height = 36, Background = new SolidColorBrush(Color.Parse("#6B8CAE")), Foreground = Avalonia.Media.Brushes.White };
            ok.Content = new TextBlock { Text = "OK" };
            var cancel = new Button { Width = 100, Height = 36, Background = new SolidColorBrush(Color.Parse("#5A6C7E")), Foreground = Avalonia.Media.Brushes.White };
            cancel.Content = new TextBlock { Text = "Cancel" };

            string? result = null;
            ok.Click += (s, e) => { result = combo.SelectedItem as string; dialog.Close(); };
            cancel.Click += (s, e) => { result = null; dialog.Close(); };

            buttonPanel.Children.Add(cancel);
            buttonPanel.Children.Add(ok);
            panel.Children.Add(buttonPanel);

            dialog.Content = panel;
            if (owner != null)
                await dialog.ShowDialog(owner);
            else
                dialog.Show();

            return result;
        }

        /// <summary>
        /// Shows a dialog to select entries from a source category to migrate to a new category.
        /// Returns a tuple: (sourceCategory, selectedEntryKeys) or (null, null) if cancelled.
        /// Keys are composite strings in format "Title|Username".
        /// </summary>
        public async Task<(string? SourceCategory, List<string>? SelectedKeys)> ShowMigrateEntriesDialogAsync(
            List<string> sourceCategories,
            Func<string, List<(string Key, string Title, string Username)>> getEntriesForCategory,
            string newCategoryName,
            Window? owner = null)
        {
            var dialog = new Window
            {
                Title = "Move Entries to New Category",
                Width = 600,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = true,
                Background = DullNavyBrush
            };

            var mainPanel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 12 };

            // Header text
            mainPanel.Children.Add(new TextBlock
            {
                Text = $"Select entries to move into '{newCategoryName}'",
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                Foreground = Avalonia.Media.Brushes.White
            });

            // Source category selector
            var categoryPanel = new StackPanel { Spacing = 4 };
            categoryPanel.Children.Add(new TextBlock
            {
                Text = "Source Category:",
                FontSize = 12,
                Foreground = Avalonia.Media.Brushes.White
            });
            var categoryCombo = new ComboBox
            {
                ItemsSource = sourceCategories,
                SelectedIndex = sourceCategories.Count > 0 ? 0 : -1,
                Width = 300,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            categoryPanel.Children.Add(categoryCombo);
            mainPanel.Children.Add(categoryPanel);

            // Entries list
            var entriesPanel = new StackPanel { Spacing = 4 };
            entriesPanel.Children.Add(new TextBlock
            {
                Text = "Select Entries:",
                FontSize = 12,
                Foreground = Avalonia.Media.Brushes.White
            });

            var scrollViewer = new ScrollViewer
            {
                Height = 280,
                Background = new SolidColorBrush(Color.Parse("#2A3A4A"))
            };
            var entriesStack = new StackPanel { Margin = new Avalonia.Thickness(8), Spacing = 4 };
            scrollViewer.Content = entriesStack;
            entriesPanel.Children.Add(scrollViewer);
            mainPanel.Children.Add(entriesPanel);

            var checkBoxes = new List<(CheckBox CheckBox, string Key)>();

            Action updateEntriesList = () =>
            {
                entriesStack.Children.Clear();
                checkBoxes.Clear();

                var selectedCategory = categoryCombo.SelectedItem as string;
                if (string.IsNullOrEmpty(selectedCategory)) return;

                var entries = getEntriesForCategory(selectedCategory);

                if (entries.Count == 0)
                {
                    entriesStack.Children.Add(new TextBlock
                    {
                        Text = "No entries in this category",
                        FontStyle = FontStyle.Italic,
                        Foreground = Avalonia.Media.Brushes.Gray,
                        Margin = new Avalonia.Thickness(8)
                    });
                    return;
                }

                foreach (var entry in entries)
                {
                    var checkBox = new CheckBox
                    {
                        Margin = new Avalonia.Thickness(4, 2),
                        Foreground = Avalonia.Media.Brushes.White
                    };

                    var contentPanel = new StackPanel { Spacing = 2 };
                    contentPanel.Children.Add(new TextBlock
                    {
                        Text = entry.Title,
                        FontWeight = FontWeight.SemiBold,
                        FontSize = 13
                    });
                    if (!string.IsNullOrEmpty(entry.Username))
                    {
                        contentPanel.Children.Add(new TextBlock
                        {
                            Text = entry.Username,
                            FontSize = 11,
                            Foreground = new SolidColorBrush(Color.Parse("#A0AEC0"))
                        });
                    }
                    checkBox.Content = contentPanel;

                    entriesStack.Children.Add(checkBox);
                    checkBoxes.Add((checkBox, entry.Key));
                }
            };

            categoryCombo.SelectionChanged += (s, e) => updateEntriesList();
            updateEntriesList();

            // Select All / Deselect All buttons
            var selectionButtonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            var selectAllBtn = new Button
            {
                Content = "Select All",
                Width = 100,
                Height = 32,
                Background = new SolidColorBrush(Color.Parse("#5A6C7E")),
                Foreground = Avalonia.Media.Brushes.White
            };
            selectAllBtn.Click += (s, e) =>
            {
                foreach (var (cb, _) in checkBoxes) cb.IsChecked = true;
            };

            var deselectAllBtn = new Button
            {
                Content = "Deselect All",
                Width = 100,
                Height = 32,
                Background = new SolidColorBrush(Color.Parse("#5A6C7E")),
                Foreground = Avalonia.Media.Brushes.White
            };
            deselectAllBtn.Click += (s, e) =>
            {
                foreach (var (cb, _) in checkBoxes) cb.IsChecked = false;
            };

            selectionButtonPanel.Children.Add(selectAllBtn);
            selectionButtonPanel.Children.Add(deselectAllBtn);
            mainPanel.Children.Add(selectionButtonPanel);

            // Action buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Avalonia.Thickness(0, 8, 0, 0)
            };

            var skipBtn = new Button
            {
                Content = "Skip",
                Width = 100,
                Height = 36,
                Background = new SolidColorBrush(Color.Parse("#5A6C7E")),
                Foreground = Avalonia.Media.Brushes.White
            };

            var moveBtn = new Button
            {
                Content = "Move Selected",
                Width = 120,
                Height = 36,
                Background = new SolidColorBrush(Color.Parse("#6B8CAE")),
                Foreground = Avalonia.Media.Brushes.White
            };

            string? resultCategory = null;
            List<string>? resultKeys = null;

            skipBtn.Click += (s, e) =>
            {
                resultCategory = null;
                resultKeys = null;
                dialog.Close();
            };

            moveBtn.Click += (s, e) =>
            {
                resultCategory = categoryCombo.SelectedItem as string;
                resultKeys = checkBoxes.Where(x => x.CheckBox.IsChecked == true).Select(x => x.Key).ToList();
                dialog.Close();
            };

            buttonPanel.Children.Add(skipBtn);
            buttonPanel.Children.Add(moveBtn);
            mainPanel.Children.Add(buttonPanel);

            dialog.Content = mainPanel;

            if (owner != null)
                await dialog.ShowDialog(owner);
            else
                dialog.Show();

            return (resultCategory, resultKeys);
        }

        /// <summary>
        /// Shows a destructive action confirmation dialog that requires the user to type a specific confirmation text.
        /// </summary>
        public async Task<bool> ShowDestructiveConfirmationAsync(
            string title,
            string message,
            string confirmationText = "DELETE",
            Window? owner = null)
        {
            var result = false;
            var dialog = new Window
            {
                Title = title,
                Width = 520,
                Height = 320,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                Background = new SolidColorBrush(Color.Parse("#2B1F20")) // Dark red tint
            };

            var panel = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 12 };

            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 20,
                FontWeight = Avalonia.Media.FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#FF7A6A")), // Warning red
                Margin = new Avalonia.Thickness(0, 0, 0, 8)
            });

            panel.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 14,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Foreground = Avalonia.Media.Brushes.White,
                Margin = new Avalonia.Thickness(0, 0, 0, 12)
            });

            panel.Children.Add(new TextBlock
            {
                Text = $"Type '{confirmationText}' to confirm:",
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = Avalonia.Media.Brushes.LightGray,
                Margin = new Avalonia.Thickness(0, 8, 0, 4)
            });

            var confirmationTextBox = new TextBox
            {
                Width = 300,
                Height = 40,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.Parse("#1A1214")),
                Foreground = Avalonia.Media.Brushes.White,
                BorderBrush = new SolidColorBrush(Color.Parse("#FF7A6A")),
                BorderThickness = new Avalonia.Thickness(2)
            };

            panel.Children.Add(confirmationTextBox);

            var buttonPanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Spacing = 12,
                Margin = new Avalonia.Thickness(0, 16, 0, 0)
            };

            var confirmButton = new Button
            {
                Width = 140,
                Height = 42,
                Background = new SolidColorBrush(Color.Parse("#A85A5A")), // Muted red
                Foreground = Avalonia.Media.Brushes.White,
                FontWeight = FontWeight.SemiBold,
                IsEnabled = false
            };
            confirmButton.Content = new TextBlock { Text = "Confirm Delete" };

            var cancelButton = new Button
            {
                Width = 120,
                Height = 42,
                Background = new SolidColorBrush(Color.Parse("#5A6C7E")),
                Foreground = Avalonia.Media.Brushes.White
            };
            cancelButton.Content = new TextBlock { Text = "Cancel" };

            // Enable confirm button only when text matches
            confirmationTextBox.TextChanged += (s, e) =>
            {
                confirmButton.IsEnabled = confirmationTextBox.Text == confirmationText;
            };

            confirmButton.Click += (s, e) =>
            {
                if (confirmationTextBox.Text == confirmationText)
                {
                    result = true;
                    dialog.Close();
                }
            };

            cancelButton.Click += (s, e) =>
            {
                result = false;
                dialog.Close();
            };

            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(confirmButton);
            panel.Children.Add(buttonPanel);

            dialog.Content = panel;

            if (owner != null)
            {
                try
                {
                    await dialog.ShowDialog(owner);
                }
                catch
                {
                    dialog.Show();
                }
            }
            else
            {
                dialog.Show();
            }

            return result;
        }
    }
}
