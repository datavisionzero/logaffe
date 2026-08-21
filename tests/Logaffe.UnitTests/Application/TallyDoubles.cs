using Logaffe.Application.Ports;
using Logaffe.Domain.Projects;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// The tally table as the acts above it see it: it keeps what it was given, adds
/// to an hour it already holds, and records what it was asked to remove.
/// </summary>
/// <remarks>
/// It accumulates because that is the one thing the port promises that a store
/// could get wrong in a way an act would never notice — a flush carries what
/// arrived since the last one, so a store that wrote instead of adding would
/// leave every hour holding its final minute.
/// </remarks>
internal sealed class RecordingTallies : ITallies
{
    private readonly Dictionary<(Guid ProjectId, DateTimeOffset Hour), Tally> _rows = [];

    /// <summary>Every flush written, in order, as the increments it carried.</summary>
    public List<IReadOnlyList<TallyIncrement>> Flushes { get; } = [];

    /// <summary>Every cutoff a sweep asked for, in order.</summary>
    public List<DateTimeOffset> Cutoffs { get; } = [];

    /// <summary>Every project a sweep asked to be removed whole, in order.</summary>
    public List<Guid> Removed { get; } = [];

    /// <summary>What a write throws instead of storing.</summary>
    public Exception? Refusing { get; set; }

    /// <summary>What the table says it still holds rows for.</summary>
    public List<Guid> Holding { get; } = [];

    public IReadOnlyList<Tally> Rows => [.. _rows.Values.OrderBy(row => row.Hour)];

    public Tally Row(Guid projectId, DateTimeOffset hour) => _rows[(projectId, hour)];

    public Task AddAsync(
        IReadOnlyList<TallyIncrement> increments, CancellationToken cancellationToken)
    {
        if (Refusing is not null)
        {
            // Nothing is kept, which is the real store's transaction: what threw
            // stored none of it.
            return Task.FromException(Refusing);
        }

        Flushes.Add(increments);

        foreach (var increment in increments)
        {
            var key = (increment.ProjectId, increment.Hour);

            if (!_rows.TryGetValue(key, out var tally))
            {
                tally = Tally.For(increment.ProjectId, increment.Hour);
                _rows[key] = tally;
            }

            tally.Add(increment.Entries, increment.AtErrorOrAbove);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Tally>> ReadAsync(
        Guid projectId,
        DateTimeOffset fromHour,
        DateTimeOffset toHour,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Tally>>(
        [
            .. _rows.Values
                .Where(row => row.ProjectId == projectId && row.Hour >= fromHour && row.Hour < toHour)
                .OrderBy(row => row.Hour),
        ]);

    public Task<IReadOnlyList<Guid>> ProjectsWithTalliesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>([.. Holding]);

    public Task RemoveHoursBeforeAsync(DateTimeOffset hour, CancellationToken cancellationToken)
    {
        Cutoffs.Add(hour);

        foreach (var key in _rows.Keys.Where(key => key.Hour < hour).ToList())
        {
            _rows.Remove(key);
        }

        return Task.CompletedTask;
    }

    public Task RemoveProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        Removed.Add(projectId);

        foreach (var key in _rows.Keys.Where(key => key.ProjectId == projectId).ToList())
        {
            _rows.Remove(key);
        }

        return Task.CompletedTask;
    }
}
