using Logaffe.Domain.Entries;

namespace Logaffe.Application.Ports;

/// <summary>
/// The log entries an installation holds.
/// </summary>
/// <remarks>
/// <para>
/// This is the port over the one table that dominates the product: the writer
/// the ingestion path hands a batch to, and what the sweep needs to take rows
/// out again. Reading the entries is <see cref="IEntryReader"/>, which is a port
/// of its own because it is a different surface with different consumers.
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
    /// Writes a whole batch, every entry of it carrying the identity it was
    /// given before it got here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is one call for the batch rather than one per entry because this is
    /// the hottest path in the product and the thing on the other side is a
    /// binary <c>COPY</c>
    /// (<see href="https://github.com/datavisionzero/logaffe/blob/main/docs/adr/0003-ef-core-owns-the-schema-the-log-path-goes-around-it.md">ADR 0003</see>).
    /// </para>
    /// <para>
    /// <b>It either stores the batch or throws.</b> There is no partial answer
    /// here: which entries are worth storing was decided before this was called,
    /// and a store that cannot be reached is the <c>503</c> of
    /// <c>docs/ingestion.md</c> — the batch is gone, which is what
    /// fire-and-forget means and why the application still has its file.
    /// </para>
    /// </remarks>
    Task WriteAsync(IReadOnlyList<LogEntry> batch, CancellationToken cancellationToken);

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

    /// <summary>
    /// How many of one project's entries arrived before
    /// <paramref name="receivedBefore"/>.
    /// </summary>
    /// <remarks>
    /// The same shape as the removal above and over the same index, asked for a
    /// window that has not been applied: it is what the operator is told before
    /// a lower window takes effect, because a settings field that silently
    /// destroys data is a bad settings field.
    /// </remarks>
    Task<long> CountReceivedBeforeAsync(
        Guid projectId, DateTimeOffset receivedBefore, CancellationToken cancellationToken);
}
