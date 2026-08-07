namespace Logaffe.Application.Ports;

/// <summary>
/// The log entries an installation holds.
/// </summary>
/// <remarks>
/// <para>
/// This is the port over the one table that dominates the product, and it
/// offers what the sweep needs and nothing else. The writer that takes a batch
/// and the reader that answers a filtered page are the ingestion and querying
/// paths' and arrive with them; putting all three here now would be writing
/// down the shape of two paths that have not been built.
/// </para>
/// <para>
/// Nothing on it hands an entry back. Removing is counted rather than
/// enumerated, because the caller's question is how much of a portion it got
/// through and not which rows they were — an answer that would be megabytes on
/// the way to being discarded.
/// </para>
/// </remarks>
public interface IEntries
{
    /// <summary>
    /// Every project identity the entry table still holds rows for.
    /// </summary>
    /// <remarks>
    /// Asked because deleting a project leaves its entries behind (ADR 0019),
    /// and once the project row is gone there is nothing else that names them:
    /// the sweep walks the projects, and a project that no longer exists is not
    /// on that walk. This is the other end of it — what the table says is in
    /// there, against what the installation says exists.
    /// </remarks>
    Task<IReadOnlyList<Guid>> ProjectsWithEntriesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Removes up to <paramref name="portion"/> of one project's entries that
    /// arrived before <paramref name="receivedBefore"/>, and answers how many
    /// went.
    /// </summary>
    /// <remarks>
    /// Bounded because the alternative is one statement holding a long
    /// transaction across a table other projects are still being written to.
    /// The caller repeats it: a portion that comes back short is the last one.
    /// </remarks>
    Task<int> RemoveReceivedBeforeAsync(
        Guid projectId, DateTimeOffset receivedBefore, int portion, CancellationToken cancellationToken);
}
