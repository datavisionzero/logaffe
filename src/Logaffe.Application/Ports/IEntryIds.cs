namespace Logaffe.Application.Ports;

/// <summary>
/// Where an entry's identity comes from, which is the installation rather than
/// the database.
/// </summary>
/// <remarks>
/// <para>
/// Binary <c>COPY</c> carries the value with the row, so a sequence would mean a
/// <c>nextval</c> per entry or a round trip per batch on the hottest path in the
/// product. An installation is a <b>single writer</b> — one container, one
/// ingestion endpoint — so a counter seeded from the high-water mark at startup
/// and handed out in blocks is all this needs (<c>docs/storage.md</c>).
/// </para>
/// <para>
/// <b>Gaps are irrelevant and uniqueness is not.</b> Nothing counts these and
/// nothing assumes they are dense, so a block reserved by a batch that then fails
/// to store is simply gone. What cannot happen is two rows with one identity: the
/// cursor of <c>docs/querying.md</c> is <c>(event_time, id)</c> and is only total
/// because of it.
/// </para>
/// </remarks>
public interface IEntryIds
{
    /// <summary>
    /// Takes <paramref name="count"/> identities out of circulation and answers
    /// with the first of them; the block runs from there upwards.
    /// </summary>
    Task<long> ReserveAsync(int count, CancellationToken cancellationToken);
}
