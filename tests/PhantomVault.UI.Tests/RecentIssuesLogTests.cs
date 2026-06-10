using System.Linq;
using Avalonia.Threading;
using PhantomVault.UI.Desktop.Services;
using Xunit;

namespace PhantomVault.UI.Tests;

// Methods within a single xUnit class run sequentially, so the shared singleton
// is safe here as long as each test clears first and flushes the dispatcher.
public sealed class RecentIssuesLogTests
{
    private static void Flush()
    {
        // RecentIssuesLog posts to the Avalonia dispatcher when off the UI thread;
        // drain the queued jobs so assertions observe a settled state.
        Dispatcher.UIThread.RunJobs();
    }

    private static RecentIssuesLog Reset()
    {
        var log = RecentIssuesLog.Instance;
        log.Clear();
        Flush();
        return log;
    }

    [Fact]
    public void Record_AddsEntry_MostRecentFirst()
    {
        var log = Reset();

        log.Record(IssueSeverity.Warning, "First");
        log.Record(IssueSeverity.Error, "Second", "details");
        Flush();

        Assert.Equal(2, log.Entries.Count);
        Assert.Equal("Second", log.Entries[0].Title);
        Assert.Equal(IssueSeverity.Error, log.Entries[0].Severity);
        Assert.Equal("First", log.Entries[1].Title);
    }

    [Fact]
    public void Record_IgnoresBlankTitle()
    {
        var log = Reset();

        log.Record(IssueSeverity.Warning, "");
        log.Record(IssueSeverity.Warning, "   ");
        Flush();

        Assert.Empty(log.Entries);
    }

    [Fact]
    public void Record_IsBoundedToFiftyEntries()
    {
        var log = Reset();

        for (var i = 0; i < 75; i++)
        {
            log.Record(IssueSeverity.Warning, $"Issue {i}");
        }
        Flush();

        Assert.Equal(50, log.Entries.Count);
        // Newest is at the head; the 25 oldest should have been evicted.
        Assert.Equal("Issue 74", log.Entries[0].Title);
        Assert.Equal("Issue 25", log.Entries[^1].Title);
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var log = Reset();

        log.Record(IssueSeverity.Error, "Boom");
        Flush();
        Assert.NotEmpty(log.Entries);

        log.Clear();
        Flush();
        Assert.Empty(log.Entries);
    }

    [Fact]
    public void IssueEntry_Display_PrefersTitleWhenNoDetail()
    {
        var log = Reset();

        log.Record(IssueSeverity.Warning, "Just a title");
        log.Record(IssueSeverity.Error, "Has detail", "the detail");
        Flush();

        var detailed = log.Entries.First(e => e.Title == "Has detail");
        var bare = log.Entries.First(e => e.Title == "Just a title");

        Assert.Equal("Has detail — the detail", detailed.Display);
        Assert.Equal("Just a title", bare.Display);
        Assert.Equal("Error", detailed.SeverityText);
        Assert.Equal("Warning", bare.SeverityText);
    }
}
