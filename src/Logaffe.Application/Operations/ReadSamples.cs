using Logaffe.Application.Ports;
using Logaffe.Domain.Hosts;
using Logaffe.Domain.Queries;

namespace Logaffe.Application.Operations;

/// <summary>
/// What one host reported over a range, and which host it was.
/// </summary>
/// <remarks>
/// The name rides along because the read had to find the host anyway to answer
/// at all, and because the agent is given a host as an identity and nothing else
/// (<c>docs/mcp.md</c>) — so this is where it learns what the machine is called,
/// at the one moment it has a reason to say so.
/// </remarks>
public sealed record HostSamples(Guid HostId, string Name, SampleWindow Window);

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
    /// <summary>
    /// What the host reported, or <c>null</c> when there is no such host.
    /// </summary>
    public async Task<Read<HostSamples>?> ExecuteAsync(
        Guid hostId,
        DateTimeOffset from,
        DateTimeOffset to,
        BucketCount buckets,
        CancellationToken cancellationToken)
    {
        // Asked first so that a host deleted in another tab is an answer rather
        // than an empty window that reads as a quiet machine.
        var host = await hosts.FindAsync(hostId, cancellationToken);
        if (host is null)
        {
            return null;
        }

        // A range given backwards is read as the range it names rather than
        // refused: there is one operator and two ends of a slider, and no
        // interpretation of "to before from" is more useful than the obvious
        // one.
        var (start, end) = from <= to ? (from, to) : (to, from);

        SampleWindow window;
        try
        {
            window = await samples.ReadAsync(hostId, start, end, buckets, cancellationToken);
        }
        catch (ReadExpiredException)
        {
            // The five seconds, on a read that has one thing to narrow: the
            // range is always set here — there is no asking a host for
            // everything it ever reported — so the only adjustment is a shorter
            // one (ADR 0026).
            return new Read<HostSamples>(null, [Narrowing.SmallerTimeRange]);
        }

        return Read<HostSamples>.Of(new HostSamples(host.Id, host.Name, window));
    }
}
