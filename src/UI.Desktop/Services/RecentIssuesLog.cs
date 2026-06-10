using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;

namespace PhantomVault.UI.Desktop.Services;

public enum IssueSeverity
{
    Warning,
    Error
}

public sealed class IssueEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public IssueSeverity Severity { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;

    public string TimeText => Timestamp.ToString("HH:mm:ss");
    public string SeverityText => Severity == IssueSeverity.Error ? "Error" : "Warning";
    public string Display => string.IsNullOrWhiteSpace(Detail) ? Title : $"{Title} — {Detail}";
}

// Bounded, UI-thread-affined log of issues surfaced to the user (errors/warnings).
// Toasts feed this automatically so a user can review what went wrong after the toast fades.
public sealed class RecentIssuesLog
{
    private const int MaxEntries = 50;
    private static RecentIssuesLog? _instance;

    public static RecentIssuesLog Instance => _instance ??= new RecentIssuesLog();

    public ObservableCollection<IssueEntry> Entries { get; } = new();

    private RecentIssuesLog()
    {
    }

    public void Record(IssueSeverity severity, string title, string detail = "")
    {
        if (string.IsNullOrWhiteSpace(title))
            return;

        var entry = new IssueEntry { Severity = severity, Title = title, Detail = detail };

        void Add()
        {
            Entries.Insert(0, entry);
            while (Entries.Count > MaxEntries)
                Entries.RemoveAt(Entries.Count - 1);
        }

        if (Dispatcher.UIThread.CheckAccess())
            Add();
        else
            Dispatcher.UIThread.Post(Add);
    }

    public void Clear()
    {
        if (Dispatcher.UIThread.CheckAccess())
            Entries.Clear();
        else
            Dispatcher.UIThread.Post(() => Entries.Clear());
    }
}
