using Logaffe.Application.Ports;

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
