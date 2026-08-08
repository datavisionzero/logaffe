using Logaffe.Application.Ports;
using Logaffe.Domain.Entries;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// The entry table as the acts above it see it: it records what it was asked,
/// for which project and before when, and answers with what the test told it to.
/// </summary>
/// <remarks>
/// It holds no entries. Which rows a cutoff actually takes, and whether the
/// statements meet the index they were written for, is asked of a real Postgres
/// — this stands in for the two questions the acts decide, which are what they
/// ask and how often.
/// </remarks>
internal sealed class RecordingEntries : IEntries
{
    private readonly List<Guid> _holding = [];
    private readonly Queue<int> _removals = new();

    /// <summary>Every removal asked for, in order.</summary>
    public List<(Guid ProjectId, DateTimeOffset ReceivedBefore)> Removals { get; } = [];

    /// <summary>Every count asked for, in order.</summary>
    public List<(Guid ProjectId, DateTimeOffset ReceivedBefore)> Counts { get; } = [];

    /// <summary>
    /// Every batch written, in order. Unlike the removals this keeps the entries
    /// themselves, because what an ingestion is asked about is what it made of a
    /// line — the row it built, not that it built one.
    /// </summary>
    public List<IReadOnlyList<LogEntry>> Written { get; } = [];

    /// <summary>Every entry of every batch, which is the usual question.</summary>
    public IReadOnlyList<LogEntry> Entries => [.. Written.SelectMany(batch => batch)];

    /// <summary>
    /// What a write throws instead of storing, which is the store that cannot be
    /// reached.
    /// </summary>
    public Exception? Refusing { get; set; }

    /// <summary>What a count comes back as.</summary>
    public long Counting { get; set; }

    /// <summary>Which projects the table still holds rows for.</summary>
    public void Holding(params Guid[] projectIds) => _holding.AddRange(projectIds);

    /// <summary>What each removal in turn comes back as.</summary>
    public void Removing(params int[] removals)
    {
        foreach (var removal in removals)
        {
            _removals.Enqueue(removal);
        }
    }

    public int PortionsFor(Guid projectId) =>
        Removals.Count(removal => removal.ProjectId == projectId);

    public DateTimeOffset CutoffFor(Guid projectId) =>
        Removals.Single(removal => removal.ProjectId == projectId).ReceivedBefore;

    public Task WriteAsync(IReadOnlyList<LogEntry> batch, CancellationToken cancellationToken)
    {
        if (Refusing is not null)
        {
            return Task.FromException(Refusing);
        }

        Written.Add(batch);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> ProjectsWithEntriesAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>([.. _holding]);

    public Task<int> RemoveReceivedBeforeAsync(
        Guid projectId,
        DateTimeOffset receivedBefore,
        int portion,
        CancellationToken cancellationToken)
    {
        Removals.Add((projectId, receivedBefore));

        return Task.FromResult(_removals.Count > 0 ? _removals.Dequeue() : 0);
    }

    public Task<long> CountReceivedBeforeAsync(
        Guid projectId, DateTimeOffset receivedBefore, CancellationToken cancellationToken)
    {
        Counts.Add((projectId, receivedBefore));

        return Task.FromResult(Counting);
    }
}

/// <summary>
/// The counter, as the act above it sees it: it hands out blocks from where the
/// test says the table already got to.
/// </summary>
/// <remarks>
/// It counts the blocks as well as handing them out, because the act asks for
/// one per batch and asking twice for one batch would be a gap nobody noticed —
/// gaps being irrelevant is a statement about the store, not a licence to leave
/// them here.
/// </remarks>
internal sealed class HandingOutIds(long from = 0) : IEntryIds
{
    /// <summary>Every block asked for, in order, as its size.</summary>
    public List<int> Blocks { get; } = [];

    private long HandedOut { get; set; } = from;

    public Task<long> ReserveAsync(int count, CancellationToken cancellationToken)
    {
        Blocks.Add(count);
        HandedOut += count;

        return Task.FromResult(HandedOut - count + 1);
    }
}
