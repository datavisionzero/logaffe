using Logaffe.Application.Ports;
using Logaffe.Domain.Entries;
using Logaffe.Domain.Queries;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// The read side of the entry table as the acts above it see it: it records what
/// it was asked and answers with what the test told it to.
/// </summary>
/// <remarks>
/// <b>It does not filter and it does not order.</b> Whether a filter narrows to
/// the right rows, and whether the statement meets the index it was written for,
/// is asked of a real Postgres. What the acts above decide is which project is
/// asked, what happens to a page that filled, and what a caller is told when the
/// five seconds run out — and that is what this stands in for.
/// </remarks>
internal sealed class RecordingReader : IEntryReader
{
    /// <summary>Every page asked for, in order.</summary>
    public List<(Guid ProjectId, EntryFilters Filters, EntryCursor? After)> Pages { get; } = [];

    /// <summary>Every count asked for, in order.</summary>
    public List<(Guid ProjectId, EntryFilters Filters, Grouping Grouping, TimeBucket Bucket)>
        Counts { get; } = [];

    /// <summary>Every poll of the tail, in order.</summary>
    public List<(Guid ProjectId, EntryFilters Filters, TailCursor Since)> Polls { get; } = [];

    /// <summary>Every arming of a tail, in order.</summary>
    public List<Guid> Armings { get; } = [];

    /// <summary>Every entry asked for by identity, in order.</summary>
    public List<(Guid ProjectId, long Id)> Lookups { get; } = [];

    /// <summary>What a page comes back as.</summary>
    public IReadOnlyList<LogEntry> Paging { get; set; } = [];

    /// <summary>What a count comes back as.</summary>
    public IReadOnlyList<CountedGroup> Counting { get; set; } = [];

    /// <summary>What a poll of the tail comes back as.</summary>
    public IReadOnlyList<LogEntry> Arriving { get; set; } = [];

    /// <summary>Where the arrival order ends, for the poll that arms a tail.</summary>
    public TailCursor? Newest { get; set; }

    /// <summary>What a lookup comes back as.</summary>
    public LogEntry? Finding { get; set; }

    /// <summary>Whether the reads run out of their five seconds instead of answering.</summary>
    public bool Expiring { get; set; }

    /// <summary>A page of <paramref name="count"/> entries, which is all these acts read of one.</summary>
    public void PagingOf(int count) =>
        Paging = [.. Enumerable.Range(0, count).Select(i => An.Entry(i + 1))];

    /// <summary>A poll answering <paramref name="count"/> entries.</summary>
    public void ArrivingOf(int count) =>
        Arriving = [.. Enumerable.Range(0, count).Select(i => An.Entry(i + 1))];

    public Task<IReadOnlyList<LogEntry>> PageAsync(
        Guid projectId,
        EntryFilters filters,
        EntryCursor? after,
        CancellationToken cancellationToken)
    {
        Pages.Add((projectId, filters, after));

        return Expiring
            ? Task.FromException<IReadOnlyList<LogEntry>>(Expired())
            : Task.FromResult(Paging);
    }

    public Task<IReadOnlyList<CountedGroup>> CountAsync(
        Guid projectId,
        EntryFilters filters,
        Grouping grouping,
        TimeBucket bucket,
        CancellationToken cancellationToken)
    {
        Counts.Add((projectId, filters, grouping, bucket));

        return Expiring
            ? Task.FromException<IReadOnlyList<CountedGroup>>(Expired())
            : Task.FromResult(Counting);
    }

    public Task<IReadOnlyList<LogEntry>> ArrivalsAsync(
        Guid projectId,
        EntryFilters filters,
        TailCursor since,
        CancellationToken cancellationToken)
    {
        Polls.Add((projectId, filters, since));

        return Expiring
            ? Task.FromException<IReadOnlyList<LogEntry>>(Expired())
            : Task.FromResult(Arriving);
    }

    public Task<TailCursor?> NewestArrivalAsync(
        Guid projectId, CancellationToken cancellationToken)
    {
        Armings.Add(projectId);

        return Expiring
            ? Task.FromException<TailCursor?>(Expired())
            : Task.FromResult(Newest);
    }

    public Task<LogEntry?> FindAsync(
        Guid projectId, long id, CancellationToken cancellationToken)
    {
        Lookups.Add((projectId, id));

        return Task.FromResult(Finding);
    }

    private static ReadExpiredException Expired() =>
        new(new OperationCanceledException());
}

/// <summary>
/// The entries these tests page over. What is on one is not what they are about,
/// so there is one shape of it and the identity is what differs.
/// </summary>
internal static class An
{
    public static readonly DateTimeOffset Time = new(2026, 8, 8, 10, 0, 0, TimeSpan.Zero);

    public static LogEntry Entry(long id, DateTimeOffset? at = null, DateTimeOffset? received = null)
        => new()
    {
        Id = id,
        ProjectId = Guid.CreateVersion7(),

        // Descending by event time, as a page is: the last entry of a full page
        // is the one the next cursor is taken from, and taking it from the wrong
        // end is exactly the mistake worth catching.
        EventTime = at ?? Time.AddSeconds(-id),

        // The other clock, which is the tail's and which a late delivery is the
        // whole point of: it is given separately because the two disagreeing is
        // the ordinary case rather than the odd one.
        ReceiptTime = received ?? at ?? Time,
        Level = Level.Information,
        MessageTemplate = "Checkout {OrderId} failed",
        RenderedMessage = $"Checkout {id} failed",
        MessageTruncated = false,
        ExceptionTruncated = false,
    };
}
