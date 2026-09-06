using Logaffe.Domain.History;

namespace Logaffe.Application.Ports;

/// <summary>
/// What was changed on this installation, and by whom.
/// </summary>
/// <remarks>
/// <para>
/// Append-only by construction: there is a write and there are reads, and
/// nothing here edits or removes a row. What removes one is the subject being
/// deleted or Host Recovery removing the identity it names.
/// </para>
/// <para>
/// The write is deliberately a separate call rather than something the stores do
/// on the way past. A change nobody recorded is a change that did not need
/// recording — the retention sweep writes millions of deletions and none of them
/// is a change to configuration — and a store that recorded everything it wrote
/// would have to be told which of those it was.
/// </para>
/// </remarks>
public interface IHistory
{
    Task RecordAsync(Change change, CancellationToken cancellationToken);

    /// <summary>
    /// The most recent changes, newest first, resuming after
    /// <paramref name="before"/> when there is one.
    /// </summary>
    /// <remarks>
    /// The cursor is the row's own id, which the database assigns in the order
    /// the rows were written — there is nothing to break a tie on, because there
    /// are no ties.
    /// </remarks>
    Task<IReadOnlyList<Change>> ListAsync(
        long? before, int limit, CancellationToken cancellationToken);
}
