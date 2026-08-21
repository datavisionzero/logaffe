using Logaffe.Domain.Hosts;

namespace Logaffe.Application.Ports;

/// <summary>
/// Reading what a host reported.
/// </summary>
/// <remarks>
/// <para>
/// One host and a range is the whole of what can be asked. There is no filter,
/// no cursor and no query language — the schema is closed (ADR 0044), so there
/// are no dimensions to aggregate across and nothing for a query language to
/// say.
/// </para>
/// <para>
/// The bucketing is done here rather than by the caller, because it is one
/// grouped statement over the key and the alternative is ten thousand rows
/// crossing a layer boundary on their way to being averaged.
/// </para>
/// </remarks>
public interface ISampleReader
{
    /// <summary>
    /// What one host reported between <paramref name="from"/> and
    /// <paramref name="to"/>, divided into <paramref name="buckets"/> equal
    /// spans.
    /// </summary>
    /// <remarks>
    /// Spans with no sample in them are absent from the answer rather than
    /// present and empty: a machine that was switched off reported nothing, and
    /// a bucket carrying zeroes would say it reported nought per cent of a
    /// processor.
    /// </remarks>
    Task<SampleWindow> ReadAsync(
        Guid hostId,
        DateTimeOffset from,
        DateTimeOffset to,
        BucketCount buckets,
        CancellationToken cancellationToken);

    /// <summary>
    /// When each host last reported, hosts that never have left out.
    /// </summary>
    /// <remarks>
    /// Read off the newest sample rather than written beside it on the host row.
    /// There is nothing else to keep current, and a column saying a host
    /// reported a minute ago while its newest sample is a day old is the
    /// disagreement that comes free with storing one fact twice (ADR 0039).
    /// </remarks>
    Task<IReadOnlyDictionary<Guid, DateTimeOffset>> LastReportedAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// The newest sample each of the named hosts reported, with the filesystem
    /// readings taken with it. Hosts that never reported are left out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The hosts are named rather than found, because the caller is holding the
    /// list already and because it is what keeps this a walk of a handful of
    /// keys: one backwards scan to each host's newest instant, then the readings
    /// at it. Finding them here instead would be a grouped statement over the
    /// whole of the larger table, which is the plan
    /// <c>docs/storage.md</c> keeps this key's shape to avoid.
    /// </para>
    /// <para>
    /// <b>It is the newest reading and not an average of recent ones.</b> How
    /// full a disk is is a level rather than a rate: what the operator wants to
    /// know is how much room is left now, and an average over the last hour is
    /// an answer to a question nobody asked.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<NewestReport>> NewestReportsAsync(
        IReadOnlyList<Guid> hostIds, CancellationToken cancellationToken);
}
