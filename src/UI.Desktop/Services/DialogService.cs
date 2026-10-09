using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services;
using PhantomVault.UI.Models;

namespace PhantomVault.UI.Services
{

    /// <summary>
    /// The app's popups (information, errors, confirmations, pickers), built in code on one
    /// shell that matches the rest of the app (Styles/DialogStyles.axaml): a header with a
    /// coloured icon circle, caption and title; content in section tiles; and a footer with the
    /// actions, the primary one on the right. Every colour comes from the active theme, so the
    /// popups follow theme and light/dark changes like everything else. Escape cancels and
    /// Enter runs the primary action.
    /// </summary>
    public class DialogService
    {
        private enum DialogKind { Info, Warning, Error, Success, Question, Danger }

        private enum ButtonRole { Secondary, Primary, Danger }

        private const double MaxAutoHeight = 720;

        // ── Shell ────────────────────────────────────────────────────────────────────────

        private static IBrush ThemeBrush(string key, string fallbackHex)
        {
            if (Application.Current is { } app &&
                app.TryGetResource(key, app.ActualThemeVariant, out var value) && value is IBrush brush)
            {
                return brush;
            }

            return new SolidColorBrush(Color.Parse(fallbackHex));
        }

        private static (string Glyph, IBrush Brush, string Caption) Look(DialogKind kind) => kind switch
        {
            DialogKind.Warning => ("!", ThemeBrush("WarningBrush", "#F08A42"), "Warning"),
            DialogKind.Error => ("!", ThemeBrush("ErrorBrush", "#EF4444"), "Something went wrong"),
            DialogKind.Success => ("✓", ThemeBrush("SuccessBrush", "#4ADE80"), "Done"),
            DialogKind.Question => ("?", ThemeBrush("AccentBrush", "#6B8CAE"), "Please confirm"),
            DialogKind.Danger => ("!", ThemeBrush("ErrorBrush", "#EF4444"), "This can't be undone"),
            _ => ("i", ThemeBrush("AccentBrush", "#6B8CAE"), "Information")
        };

        /// <summary>A dialog window. Without a height it sizes to its content, up to <see cref="MaxAutoHeight"/>.</summary>
        private static Window NewDialog(string title, double width, Window? owner, double? height = null, bool resizable = false)
        {
            var dialog = new Window
            {
                Title = title,
                Width = width,
                MinWidth = Math.Min(width, 420),
                CanResize = resizable,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                Background = ThemeBrush("WindowBackgroundBrush", "#1A2530"),
                Classes = { "animated-window" }
            };

            if (height.HasValue)
            {
                dialog.Height = height.Value;
            }
            else
            {
                dialog.SizeToContent = SizeToContent.Height;
                dialog.MaxHeight = MaxAutoHeight;
            }

            return dialog;
        }

        /// <summary>Header, scrolling body and footer, in the shared dialog layout.</summary>
        private static Control Layout(DialogKind kind, string title, Control body, Control actions, string? caption = null)
        {
            var (glyph, brush, defaultCaption) = Look(kind);

            var icon = new Border
            {
                Classes = { "dialog-icon" },
                Child = new TextBlock
                {
                    Text = glyph,
                    FontSize = 18,
                    FontWeight = FontWeight.Bold,
                    Foreground = brush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            var titles = new StackPanel
            {
                Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = caption ?? defaultCaption, Classes = { "dialog-caption" } },
                    new TextBlock { Text = title, Classes = { "dialog-title" } }
                }
            };
            Grid.SetColumn(titles, 2);

            var header = new Border
            {
                Classes = { "dialog-header" },
                Child = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,14,*"),
                    Children = { icon, titles }
                }
            };

            // The inset is a margin on the body, not ScrollViewer.Padding: padding shifts content
            // already measured at the full width, which then runs off the right edge.
            body.Margin = new Thickness(24, 4, 24, 20);
            var scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = body
            };
            Grid.SetRow(scroll, 1);

            var footer = new Border { Classes = { "dialog-footer" }, Child = actions };
            Grid.SetRow(footer, 2);

            // On a card over the window background, like the Icon Manager and tool windows.
            return new Border
            {
                Classes = { "dialog-card" },
                Child = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                    Children = { header, scroll, footer }
                }
            };
        }

        private static TextBlock Message(string? text) => new()
        {
            Text = text ?? string.Empty,
            Classes = { "dialog-message" }
        };

        private static Button DialogButton(string text, ButtonRole role = ButtonRole.Secondary)
        {
            var button = new Button
            {
                Content = text,
                Classes = { "liquid-glass", "dialog-button" }
            };
            if (role == ButtonRole.Primary) button.Classes.Add("dialog-primary");
            if (role == ButtonRole.Danger) button.Classes.Add("dialog-danger");
            return button;
        }

        /// <summary>Footer actions: <paramref name="leading"/> on the left, the rest right-aligned in order.</summary>
        private static Control Actions(Control? leading, params Control[] buttons)
        {
            var right = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            foreach (var b in buttons) right.Children.Add(b);
            Grid.SetColumn(right, 1);

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            if (leading != null) grid.Children.Add(leading);
            grid.Children.Add(right);
            return grid;
        }

        private static Border Tile(Control child) => new()
        {
            Classes = { "section-tile" },
            Child = child
        };

        private static Control Field(string label, Control control) => new StackPanel
        {
            Classes = { "field-group" },
            Children =
            {
                new TextBlock { Text = label, Classes = { "field-label" } },
                control
            }
        };

        private static ComboBox Picker(IEnumerable<string> items, int count) => new()
        {
            ItemsSource = items,
            SelectedIndex = count > 0 ? 0 : -1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Classes = { "form-field" }
        };

        private static TextBlock TileLabel(string text) => new() { Text = text, Classes = { "tile-label" } };

        /// <summary>Enter runs the primary action (when enabled); Escape runs cancel.</summary>
        private static void WireKeys(Window dialog, Button? primary, Action onEscape)
        {
            dialog.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    onEscape();
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter && primary is { IsEnabled: true } && e.Source is not TextBox { AcceptsReturn: true })
                {
                    primary.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    e.Handled = true;
                }
            };
        }

        /// <summary>
        /// Shows the dialog and waits until it closes: modal over <paramref name="owner"/>, or as
        /// a normal window when there is none (it used to return immediately in that case, so a
        /// confirmation without an owner always read as "no").
        /// </summary>
        private static async Task ShowAsync(Window dialog, Window? owner)
        {
            if (owner != null)
            {
                try
                {
                    await dialog.ShowDialog(owner);
                    return;
                }
                catch (InvalidOperationException)
                {
                    // Owner not shown / already closing: fall back to a normal window.
                }
            }

            var closed = new TaskCompletionSource();
            dialog.Closed += (_, _) => closed.TrySetResult();
            dialog.Show();
            await closed.Task;
        }

        /// <summary>A message with a single OK button.</summary>
        private static async Task ShowMessageAsync(DialogKind kind, string title, string? message, Window? owner, string? caption = null, double width = 480)
        {
            var dialog = NewDialog(title, width, owner);
            var ok = DialogButton("OK", ButtonRole.Primary);
            ok.Click += (_, _) => dialog.Close();
            WireKeys(dialog, ok, dialog.Close);
            dialog.Content = Layout(kind, title, Message(message), Actions(null, ok), caption);
            await ShowAsync(dialog, owner);
        }

        // ── Messages ─────────────────────────────────────────────────────────────────────

        public Task ShowInfoAsync(string title, string message, Window? owner = null)
            => ShowMessageAsync(DialogKind.Info, title, message, owner);

        public Task ShowWarningAsync(string title, string message, Window? owner = null)
            => ShowMessageAsync(DialogKind.Warning, title, message, owner);

        public Task ShowSuccessAsync(string title, string message, Window? owner = null)
            => ShowMessageAsync(DialogKind.Success, title, message, owner, width: 520);

        public async Task ShowErrorAsync(string title, string message, Window? owner = null)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    await ShowMessageAsync(DialogKind.Error, title ?? "Error", message ?? "An unknown error occurred.", owner, width: 520);

                    try
                    {
                        if (owner?.DataContext is IResettableOnError resettable)
                        {
                            await resettable.ResetAfterErrorAsync();
                        }
                    }
                    catch
                    {
                        // Resetting the owner is best effort; the error itself was shown.
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"CRITICAL ERROR in ShowErrorAsync: {ex.Message}");
                    Console.WriteLine($"Original error was - Title: {title}, Message: {message}");

                    try
                    {
                        var fallbackDialog = new Window
                        {
                            Title = "Error",
                            Width = 400,
                            Height = 200,
                            Content = new TextBlock { Text = $"An error occurred: {message}", Margin = new Thickness(20), TextWrapping = TextWrapping.Wrap }
                        };
                        fallbackDialog.Show();
                    }
                    catch
                    {
                        // Nothing more can be shown.
                    }
                }
            });
        }

        // ── Confirmations ────────────────────────────────────────────────────────────────

        public async Task<bool> ShowConfirmationAsync(
            string title,
            string message,
            string confirmText = "Continue",
            string cancelText = "Cancel",
            Window? owner = null)
        {
            var result = false;
            var dialog = NewDialog(title, 500, owner);

            var cancel = DialogButton(cancelText);
            var confirm = DialogButton(confirmText, ButtonRole.Primary);
            cancel.Click += (_, _) => { result = false; dialog.Close(); };
            confirm.Click += (_, _) => { result = true; dialog.Close(); };
            WireKeys(dialog, confirm, () => { result = false; dialog.Close(); });

            dialog.Content = Layout(DialogKind.Question, title, Message(message), Actions(null, cancel, confirm));
            await ShowAsync(dialog, owner);
            return result;
        }

        public async Task<bool> ShowConfirmationAsync(string title, string message, Window? owner = null)
        {
            var result = false;
            var dialog = NewDialog(title, 480, owner);

            var no = DialogButton("No");
            var yes = DialogButton("Yes", ButtonRole.Primary);
            no.Click += (_, _) => { result = false; dialog.Close(); };
            yes.Click += (_, _) => { result = true; dialog.Close(); };
            WireKeys(dialog, yes, () => { result = false; dialog.Close(); });

            dialog.Content = Layout(DialogKind.Question, title, Message(message), Actions(null, no, yes));
            await ShowAsync(dialog, owner);
            return result;
        }

        public async Task<bool> ShowDestructiveConfirmationAsync(
            string title,
            string message,
            string confirmationText = "DELETE",
            Window? owner = null)
        {
            var result = false;
            var dialog = NewDialog(title, 520, owner);

            var typed = new TextBox
            {
                Classes = { "form-field" },
                Watermark = confirmationText,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var cancel = DialogButton("Cancel");
            var confirm = DialogButton("Confirm delete", ButtonRole.Danger);
            confirm.IsEnabled = false;

            typed.TextChanged += (_, _) => confirm.IsEnabled = typed.Text == confirmationText;
            cancel.Click += (_, _) => { result = false; dialog.Close(); };
            confirm.Click += (_, _) =>
            {
                if (typed.Text != confirmationText) return;
                result = true;
                dialog.Close();
            };
            WireKeys(dialog, confirm, () => { result = false; dialog.Close(); });

            var body = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    Message(message),
                    Field($"Type '{confirmationText}' to confirm", typed)
                }
            };

            dialog.Content = Layout(DialogKind.Danger, title, body, Actions(null, cancel, confirm));
            dialog.Opened += (_, _) => typed.Focus();
            await ShowAsync(dialog, owner);
            return result;
        }

        /// <summary>
        /// Asks for a single line of text (a recovery code, for instance). Returns null when
        /// cancelled, so an empty entry and a refusal stay distinguishable.
        /// </summary>
        public async Task<string?> ShowTextPromptAsync(
            string title,
            string message,
            string fieldLabel,
            string? watermark = null,
            string confirmText = "Continue",
            DialogKindHint kind = DialogKindHint.Question,
            Window? owner = null)
        {
            string? result = null;
            var dialog = NewDialog(title, 520, owner);

            var entry = new TextBox
            {
                Classes = { "form-field" },
                Watermark = watermark ?? string.Empty,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var cancel = DialogButton("Cancel");
            var confirm = DialogButton(confirmText, ButtonRole.Primary);

            cancel.Click += (_, _) => { result = null; dialog.Close(); };
            confirm.Click += (_, _) => { result = entry.Text ?? string.Empty; dialog.Close(); };
            WireKeys(dialog, confirm, () => { result = null; dialog.Close(); });

            var body = new StackPanel
            {
                Spacing = 16,
                Children = { Message(message), Field(fieldLabel, entry) }
            };

            dialog.Content = Layout(
                kind == DialogKindHint.Warning ? DialogKind.Warning : DialogKind.Question,
                title, body, Actions(null, cancel, confirm));
            dialog.Opened += (_, _) => entry.Focus();
            await ShowAsync(dialog, owner);
            return result;
        }

        /// <summary>Caller-facing subset of the dialog styles, so callers need no internal type.</summary>
        public enum DialogKindHint
        {
            Question,
            Warning
        }

        // ── Import summary ───────────────────────────────────────────────────────────────

        public async Task ShowImportSummaryAsync(string title, ImportResult result, Window? owner = null)
        {
            var dialog = NewDialog(title, 640, owner);

            var success = ThemeBrush("SuccessBrush", "#4ADE80");
            var warning = ThemeBrush("WarningBrush", "#F08A42");
            var error = ThemeBrush("ErrorBrush", "#EF4444");
            var primary = ThemeBrush("PrimaryTextBrush", "#E6ECF5");

            var metrics = new WrapPanel { Orientation = Orientation.Horizontal };
            void Metric(int value, string label, string detail, IBrush valueBrush)
            {
                metrics.Children.Add(new Border
                {
                    Classes = { "section-tile" },
                    Width = 180,
                    Margin = new Thickness(0, 0, 10, 10),
                    Child = new StackPanel
                    {
                        Spacing = 2,
                        Children =
                        {
                            new TextBlock { Text = value.ToString("N0"), FontSize = 24, FontWeight = FontWeight.Bold, Foreground = valueBrush },
                            new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, Foreground = primary },
                            new TextBlock { Text = detail, Classes = { "field-hint" } }
                        }
                    }
                });
            }

            Metric(result.TotalProcessed, "Processed", "records analysed", primary);
            Metric(result.SuccessCount, "Imported", "added to your vault", success);
            if (result.DuplicateCount > 0) Metric(result.DuplicateCount, "Duplicates", "handled during import", primary);
            if (result.WarningCount > 0) Metric(result.WarningCount, "Warnings", "review recommended", warning);
            if (result.ErrorCount > 0) Metric(result.ErrorCount, "Errors", "items skipped", error);

            var body = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    Message($"{result.SuccessCount} of {result.TotalProcessed} items imported."),
                    metrics
                }
            };

            Control? Notes(string label, IReadOnlyList<string> items, int shown, IBrush bullet)
            {
                if (items.Count == 0) return null;
                var list = new StackPanel { Spacing = 8, Children = { TileLabel(label) } };
                foreach (var item in items.Take(shown))
                {
                    list.Children.Add(new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("Auto,8,*"),
                        Children =
                        {
                            new TextBlock { Text = "•", Foreground = bullet, FontWeight = FontWeight.Bold },
                            WithColumn(new TextBlock { Text = item, TextWrapping = TextWrapping.Wrap, Foreground = primary }, 2)
                        }
                    });
                }
                if (items.Count > shown)
                {
                    list.Children.Add(new TextBlock { Text = $"{items.Count - shown} more not shown.", Classes = { "field-hint" } });
                }
                return Tile(list);
            }

            if (Notes("VALIDATION NOTES", result.Warnings, 4, warning) is { } warningTile) body.Children.Add(warningTile);
            if (Notes("SKIPPED ITEMS", result.Errors, 3, error) is { } errorTile) body.Children.Add(errorTile);

            var ok = DialogButton("OK", ButtonRole.Primary);
            ok.Click += (_, _) => dialog.Close();
            WireKeys(dialog, ok, dialog.Close);

            var kind = result.ErrorCount > 0 || result.WarningCount > 0 ? DialogKind.Warning : DialogKind.Success;
            dialog.Content = Layout(kind, title, body, Actions(null, ok), "Import complete");
            await ShowAsync(dialog, owner);
        }

        private static T WithColumn<T>(T control, int column) where T : Control
        {
            Grid.SetColumn(control, column);
            return control;
        }

        // ── Categories ───────────────────────────────────────────────────────────────────

        public enum CategoryDeleteAction
        {
            Cancel,
            Move,
            Delete,
            MoveToTrash
        }

        public async Task<(CategoryDeleteAction Action, string? TargetCategory)> ShowCategoryDeleteOptionsAsync(string categoryName, System.Collections.Generic.List<string> availableTargetCategories, Window? owner = null)
        {
            (CategoryDeleteAction Action, string? TargetCategory) result = (CategoryDeleteAction.Cancel, null);
            var dialog = NewDialog("Delete category", 560, owner);

            var combo = Picker(availableTargetCategories, availableTargetCategories.Count);

            var cancel = DialogButton("Cancel");
            var trash = DialogButton("Move to trash");
            var delete = DialogButton("Delete", ButtonRole.Danger);
            var move = DialogButton("Move", ButtonRole.Primary);

            cancel.Click += (_, _) => { result = (CategoryDeleteAction.Cancel, null); dialog.Close(); };
            trash.Click += (_, _) => { result = (CategoryDeleteAction.MoveToTrash, null); dialog.Close(); };
            delete.Click += (_, _) => { result = (CategoryDeleteAction.Delete, null); dialog.Close(); };
            move.Click += (_, _) =>
            {
                if (combo.SelectedItem is not string target || string.IsNullOrEmpty(target)) return;
                result = (CategoryDeleteAction.Move, target);
                dialog.Close();
            };
            WireKeys(dialog, move, () => { result = (CategoryDeleteAction.Cancel, null); dialog.Close(); });

            var body = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    Message($"What should happen to the credentials in '{categoryName}'?"),
                    Field("Move them to", combo)
                }
            };

            dialog.Content = Layout(DialogKind.Question, $"Delete '{categoryName}'", body,
                Actions(cancel, trash, delete, move), "Delete category");
            await ShowAsync(dialog, owner);
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

        public async Task<(TrashManagerAction Action, List<SecureTrashRecord> Selected)> ShowTrashManagerAsync(List<SecureTrashRecord> trashedRecords, Window? owner = null)
        {
            (TrashManagerAction Action, List<SecureTrashRecord> Selected) result = (TrashManagerAction.Cancel, new List<SecureTrashRecord>());
            var dialog = NewDialog("Trash", 640, owner, height: 560, resizable: true);

            var checkboxMap = new Dictionary<SecureTrashRecord, CheckBox>();
            var list = new StackPanel { Spacing = 8 };
            foreach (var record in trashedRecords)
            {
                var subtitle = record.ScheduledPurgeUtc.HasValue
                    ? $"Purges {record.ScheduledPurgeUtc.Value:MMM dd}" : "Auto purge disabled";
                var cb = new CheckBox
                {
                    Content = EntryLabel(record.Payload.Title, $"{record.Payload.Username} · {subtitle}"),
                    IsChecked = false
                };
                list.Children.Add(cb);
                checkboxMap[record] = cb;
            }
            if (trashedRecords.Count == 0)
            {
                list.Children.Add(new TextBlock { Text = "The trash is empty.", Classes = { "field-hint" } });
            }

            List<SecureTrashRecord> Selected() => checkboxMap.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();

            var close = DialogButton("Close");
            var empty = DialogButton("Empty trash", ButtonRole.Danger);
            var restore = DialogButton("Restore selected", ButtonRole.Primary);

            restore.Click += (_, _) =>
            {
                var selected = Selected();
                if (selected.Count == 0) return;
                result = (TrashManagerAction.Restore, selected);
                dialog.Close();
            };

            empty.Click += async (_, _) =>
            {
                var selected = Selected();
                // Purge selected items if any are checked; otherwise the whole trash.
                var target = selected.Count > 0 ? selected : new List<SecureTrashRecord>(checkboxMap.Keys);
                if (target.Count == 0) return;

                var scope = selected.Count > 0
                    ? $"the {target.Count} selected item{(target.Count == 1 ? string.Empty : "s")}"
                    : $"all {target.Count} item{(target.Count == 1 ? string.Empty : "s")} in the trash";
                var confirmed = await ShowConfirmationAsync(
                    "Permanently delete?",
                    $"This will securely and permanently delete {scope}. This cannot be undone.",
                    dialog);
                if (!confirmed) return;

                result = (TrashManagerAction.Empty, target);
                dialog.Close();
            };

            close.Click += (_, _) => { result = (TrashManagerAction.Cancel, new List<SecureTrashRecord>()); dialog.Close(); };
            WireKeys(dialog, null, () => { result = (TrashManagerAction.Cancel, new List<SecureTrashRecord>()); dialog.Close(); });

            var body = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    Message("Restore items to the vault, or delete them permanently."),
                    Tile(list)
                }
            };

            dialog.Content = Layout(DialogKind.Info, "Trash", body, Actions(close, empty, restore), "Secure rubbish bin");
            await ShowAsync(dialog, owner);
            return result;
        }

        public async Task<(BulkCategoriesAction Action, string? TargetCategory)> ShowBulkCategoryActionAsync(List<string> availableTargetCategories, Window? owner = null)
        {
            (BulkCategoriesAction Action, string? TargetCategory) result = (BulkCategoriesAction.Cancel, null);
            var dialog = NewDialog("Bulk actions", 540, owner);

            var combo = Picker(availableTargetCategories, availableTargetCategories.Count);

            var cancel = DialogButton("Cancel");
            var toDeleted = DialogButton("Move to Deleted");
            var move = DialogButton("Move", ButtonRole.Primary);

            move.Click += (_, _) =>
            {
                if (combo.SelectedItem is not string target || string.IsNullOrEmpty(target)) return;
                result = (BulkCategoriesAction.Move, target);
                dialog.Close();
            };
            toDeleted.Click += (_, _) => { result = (BulkCategoriesAction.MoveToDeleted, null); dialog.Close(); };
            cancel.Click += (_, _) => { result = (BulkCategoriesAction.Cancel, null); dialog.Close(); };
            WireKeys(dialog, move, () => { result = (BulkCategoriesAction.Cancel, null); dialog.Close(); });

            var body = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    Message("Apply an action to the items in the selected categories."),
                    Field("Move to", combo)
                }
            };

            dialog.Content = Layout(DialogKind.Question, "Selected categories", body, Actions(cancel, toDeleted, move), "Bulk actions");
            await ShowAsync(dialog, owner);
            return result;
        }

        public async Task<(CategoryItemsAction Action, List<Credential> Selected, string? TargetCategory)> ShowCategoryItemsManagerAsync(
            string categoryName,
            List<Credential> credentialsInCategory,
            List<string> availableTargetCategories,
            Window? owner = null)
        {
            (CategoryItemsAction Action, List<Credential> Selected, string? TargetCategory) result = (CategoryItemsAction.Cancel, new List<Credential>(), null);
            var dialog = NewDialog($"Manage items — {categoryName}", 680, owner, height: 580, resizable: true);

            var checkboxMap = new Dictionary<Credential, CheckBox>();
            var list = new StackPanel { Spacing = 8 };
            foreach (var cred in credentialsInCategory)
            {
                var cb = new CheckBox { Content = EntryLabel(cred.Title, cred.Username), IsChecked = false };
                list.Children.Add(cb);
                checkboxMap[cred] = cb;
            }
            if (credentialsInCategory.Count == 0)
            {
                list.Children.Add(new TextBlock { Text = "No items in this category.", Classes = { "field-hint" } });
            }

            var selectAll = DialogButton("Select all");
            var selectNone = DialogButton("Select none");
            selectAll.Click += (_, _) => { foreach (var cb in checkboxMap.Values) cb.IsChecked = true; };
            selectNone.Click += (_, _) => { foreach (var cb in checkboxMap.Values) cb.IsChecked = false; };

            var targetCombo = Picker(availableTargetCategories, availableTargetCategories.Count);

            var close = DialogButton("Close");
            var move = DialogButton("Move selected", ButtonRole.Primary);

            move.Click += (_, _) =>
            {
                if (targetCombo.SelectedItem is not string target || string.IsNullOrEmpty(target)) return;
                var selected = checkboxMap.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();
                if (selected.Count == 0) return;
                result = (CategoryItemsAction.Move, selected, target);
                dialog.Close();
            };
            close.Click += (_, _) => dialog.Close();
            WireKeys(dialog, null, dialog.Close);

            var body = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { selectAll, selectNone } },
                    Tile(list),
                    Field("Move selected to", targetCombo)
                }
            };

            dialog.Content = Layout(DialogKind.Info, $"Items in '{categoryName}'", body, Actions(close, move), "Manage items");
            await ShowAsync(dialog, owner);
            return result;
        }

        public async Task<string?> ShowSelectCategoryAsync(List<string> categories, string prompt, Window? owner = null)
        {
            string? result = null;
            var dialog = NewDialog("Select category", 500, owner);

            var combo = Picker(categories, categories.Count);

            var cancel = DialogButton("Cancel");
            var ok = DialogButton("OK", ButtonRole.Primary);
            ok.Click += (_, _) => { result = combo.SelectedItem as string; dialog.Close(); };
            cancel.Click += (_, _) => { result = null; dialog.Close(); };
            WireKeys(dialog, ok, () => { result = null; dialog.Close(); });

            var body = new StackPanel
            {
                Spacing = 16,
                Children = { Message(prompt), Field("Category", combo) }
            };

            dialog.Content = Layout(DialogKind.Question, "Select category", body, Actions(null, cancel, ok), "Choose a category");
            await ShowAsync(dialog, owner);
            return result;
        }

        public async Task<(string? SourceCategory, List<string>? SelectedKeys)> ShowMigrateEntriesDialogAsync(
            List<string> sourceCategories,
            Func<string, List<(string Key, string Title, string Username)>> getEntriesForCategory,
            string newCategoryName,
            Window? owner = null)
        {
            string? resultCategory = null;
            List<string>? resultKeys = null;
            var dialog = NewDialog("Move entries to new category", 620, owner, height: 620, resizable: true);

            var categoryCombo = Picker(sourceCategories, sourceCategories.Count);

            var entriesStack = new StackPanel { Spacing = 8 };
            var checkBoxes = new List<(CheckBox CheckBox, string Key)>();

            void UpdateEntriesList()
            {
                entriesStack.Children.Clear();
                checkBoxes.Clear();

                if (categoryCombo.SelectedItem is not string selectedCategory || string.IsNullOrEmpty(selectedCategory)) return;

                var entries = getEntriesForCategory(selectedCategory);
                if (entries.Count == 0)
                {
                    entriesStack.Children.Add(new TextBlock { Text = "No entries in this category.", Classes = { "field-hint" } });
                    return;
                }

                foreach (var entry in entries)
                {
                    var checkBox = new CheckBox { Content = EntryLabel(entry.Title, entry.Username) };
                    entriesStack.Children.Add(checkBox);
                    checkBoxes.Add((checkBox, entry.Key));
                }
            }

            categoryCombo.SelectionChanged += (_, _) => UpdateEntriesList();
            UpdateEntriesList();

            var selectAll = DialogButton("Select all");
            var deselectAll = DialogButton("Deselect all");
            selectAll.Click += (_, _) => { foreach (var (cb, _) in checkBoxes) cb.IsChecked = true; };
            deselectAll.Click += (_, _) => { foreach (var (cb, _) in checkBoxes) cb.IsChecked = false; };

            var skip = DialogButton("Skip");
            var move = DialogButton("Move selected", ButtonRole.Primary);

            skip.Click += (_, _) => { resultCategory = null; resultKeys = null; dialog.Close(); };
            move.Click += (_, _) =>
            {
                resultCategory = categoryCombo.SelectedItem as string;
                resultKeys = checkBoxes.Where(x => x.CheckBox.IsChecked == true).Select(x => x.Key).ToList();
                dialog.Close();
            };
            WireKeys(dialog, move, () => { resultCategory = null; resultKeys = null; dialog.Close(); });

            var body = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    Field("Source category", categoryCombo),
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { selectAll, deselectAll } },
                    Tile(entriesStack)
                }
            };

            dialog.Content = Layout(DialogKind.Question, $"Move entries into '{newCategoryName}'", body, Actions(skip, move), "New category");
            await ShowAsync(dialog, owner);
            return (resultCategory, resultKeys);
        }

        /// <summary>Checkbox content for an entry: title, with the username (or detail) under it.</summary>
        private static Control EntryLabel(string title, string? detail)
        {
            var stack = new StackPanel
            {
                Spacing = 1,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = ThemeBrush("PrimaryTextBrush", "#E6ECF5")
                    }
                }
            };
            if (!string.IsNullOrWhiteSpace(detail))
            {
                stack.Children.Add(new TextBlock { Text = detail, Classes = { "field-hint" } });
            }
            return stack;
        }
    }
}
