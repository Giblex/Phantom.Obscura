using Avalonia.Controls;
using PhantomVault.UI.Desktop.Services;

namespace PhantomVault.UI.Views.Settings
{
    public partial class RecentIssuesView : UserControl
    {
        public RecentIssuesView()
        {
            InitializeComponent();

            var list = this.FindControl<ItemsControl>("IssuesList")!;
            var empty = this.FindControl<TextBlock>("EmptyText")!;
            var clear = this.FindControl<Button>("ClearButton")!;

            var log = RecentIssuesLog.Instance;
            list.ItemsSource = log.Entries;

            void RefreshEmpty() => empty.IsVisible = log.Entries.Count == 0;
            RefreshEmpty();
            log.Entries.CollectionChanged += (_, __) => RefreshEmpty();

            clear.Click += (_, __) => log.Clear();
        }
    }
}
