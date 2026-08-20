using System.ComponentModel;
using Logaffe.Application.Operations;
using Logaffe.Domain.Hosts;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Logaffe.Api.Mcp;

/// <summary>
/// The fifth tool: what a machine was doing, for the question the entries cannot
/// answer.
/// </summary>
/// <remarks>
/// <para>
/// <b>It reads and it manages nothing.</b> Creating a host, naming one, ending
/// one, minting its token and saying which host a project sits on are operator
/// acts and are absent from this interface, not forbidden on it (ADR 0018).
/// Nothing here asks a machine for anything either — it reads what the
/// collectors have already delivered.
/// </para>
/// <para>
/// <b>This is the one tool whose answer is not confined to a single project</b>,
/// because a host may carry several. What comes back is numbers the
/// installation's own collector read off a machine, carrying no text from
/// anywhere except a mount path the operator wrote into their own collector's
/// configuration — so the boundary that holds untrusted content inside one
/// project has nothing here to hold apart (ADR 0045).
/// </para>
/// <para>
/// <b>A reading token and nothing else is offered this.</b> An administering
/// token authenticates at the same endpoint and is handed a tool list that does
/// not contain it — absent rather than present and refusing, which is what keeps
/// a session that can act from ever holding a log line (ADR 0046).
/// </para>
/// </remarks>
[Authorize(Policy = AgentAuthentication.ReadingPolicy)]
[McpServerToolType]
public static class HostTools
{
    [McpServerTool(
        Name = "get_host_samples",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        """
        What one machine reported about itself over a time range: processor,
        memory, load average and how full its filesystems were. This is the tool
        for the question the entries cannot answer — the errors started at 03:14,
        and the memory on that machine had been at the ceiling since 02:50.

        The host is the one a project runs on, given by identity on
        list_projects. A project on no host has no machine to ask about.

        The answer is divided into equal spans, and each span carries both the
        average across it and the highest reading in it — an average is precisely
        what hides the spike that was worth finding. A span the machine reported
        nothing in is absent rather than zero: that it was switched off or too
        busy to report is a fact, and a zero would state its opposite.

        A read gets five seconds. One that uses them up comes back with `narrow`
        instead of samples.
        """)]
    public static async Task<HostSamplesAnswer> GetAsync(
        ReadSamples samples,
        [Description(
            "The machine to read, as list_projects gives it on the project that "
            + "runs on it.")]
        Guid hostId,
        [Description("The start of the range, inclusive.")]
        DateTimeOffset from,
        [Description("The end of the range, inclusive.")]
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var (start, end) = from <= to ? (from, to) : (to, from);

        // The agent is given no say in how many spans it gets. A week at one
        // sample a minute is ten thousand readings, and a caller that could ask
        // for all of them would be a caller that spends its own context on the
        // shape of a line — so the count comes from the range, and the same rule
        // gives the operator's band its default (`docs/mcp.md`).
        var buckets = BucketCount.For(end - start);

        var read = await samples.ExecuteAsync(hostId, start, end, buckets, cancellationToken);
        if (read is null)
        {
            throw NoSuchHost(hostId);
        }

        var span = buckets.Value == 0 ? TimeSpan.Zero : (end - start) / buckets.Value;

        return read.Expired
            ? HostSamplesAnswer.RanOut(span, read.Narrow)
            : HostSamplesAnswer.Of(read.Answer!, span);
    }

    /// <remarks>
    /// It names <c>list_projects</c> rather than a tool that lists hosts, because
    /// there is not one: a host reaches an agent as a fact about a project, which
    /// is what keeps this adapter from having a query that resolves one thing
    /// into another.
    /// </remarks>
    private static McpException NoSuchHost(Guid hostId) =>
        new($"There is no host {hostId} in this installation. Call list_projects.");
}
