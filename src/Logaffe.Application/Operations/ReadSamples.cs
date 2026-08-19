using Logaffe.Application.Ports;
using Logaffe.Domain.Hosts;

namespace Logaffe.Application.Operations;

/// <summary>
/// What one host reported over a range, for the band above the entries and for
/// the agent asking the same question.
/// </summary>
/// <remarks>
/// <para>
/// <b>A host and a range is the whole of what can be asked.</b> There is no
/// filter, no cursor and no query language: the schema is closed (ADR 0044), so
/// there are no dimensions to aggregate across and nothing for a query language
/// to say.
/// </para>
/// <para>
/// <b>This is the one read that is not inside a single project.</b> A host may
/// carry several, and what comes back is numbers the installation's own
/// collector read off a machine — carrying no text from anywhere, so the
/// boundary that holds untrusted content inside one project has nothing here to
/// hold apart (ADR 0045).
/// </para>
/// <para>
/// Both consumers get the same answer through the same act, which is what
/// <c>docs/querying.md</c> promises of every read in this product: the band the
/// operator reads and the agent's tool are one surface, not two.
/// </para>
/// </remarks>
public sealed class ReadSamples(IHosts hosts, ISampleReader samples)
{
    public async Task<SampleWindow?> ExecuteAsync(
        Guid hostId,
        DateTimeOffset from,
        DateTimeOffset to,
        BucketCount buckets,
        CancellationToken cancellationToken)
    {
        // Asked first so that a host deleted in another tab is an answer rather
        // than an empty window that reads as a quiet machine.
        if (await hosts.FindAsync(hostId, cancellationToken) is null)
        {
            return null;
        }

        // A range given backwards is read as the range it names rather than
        // refused: there is one operator and two ends of a slider, and no
        // interpretation of "to before from" is more useful than the obvious
        // one.
        var (start, end) = from <= to ? (from, to) : (to, from);

        return await samples.ReadAsync(hostId, start, end, buckets, cancellationToken);
    }
}
