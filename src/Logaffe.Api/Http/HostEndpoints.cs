using Logaffe.Application.Operations;
using Logaffe.Domain.Hosts;
using Logaffe.Domain.Projects;
using Microsoft.AspNetCore.RateLimiting;

// The framework's own Host — the one that builds an application — is in scope
// here through the implicit usings, and this file is about the other kind. The
// alias picks ours, so that the word means what the product means by it
// everywhere it appears below.
using Host = Logaffe.Domain.Hosts.Host;

namespace Logaffe.Api.Http;

/// <param name="Name">
/// Unique across the installation, and the whole of what a host is. There is no
/// group to relax it the way a project's name is relaxed: a host sits in
/// nothing, so two machines called <c>web</c> are the trap with nothing beside
/// them to tell them apart.
/// </param>
public sealed record HostRequest(string? Name);

/// <summary>One host, by itself.</summary>
public sealed record HostResponse(Guid Id, string Name, DateTimeOffset CreatedAt);

/// <summary>
/// One host on the list the operator reads.
/// </summary>
/// <param name="HostTokens">
/// How many tokens can report to it: one ordinarily, two while it is being
/// rotated, and none for a machine nothing can deliver to. That last case is why
/// the number is on the list at all.
/// </param>
/// <param name="LastReportedAt">
/// When a sample last arrived from it, or <c>null</c> when none ever has — a
/// host between being created and its collector being started, or one whose
/// machine is switched off. It is read off the newest sample rather than kept
/// beside the host, so it cannot disagree with what the samples say.
/// </param>
/// <param name="Projects">
/// How many projects say they run on it, which is what the screen in front of a
/// deletion says will be left on no host.
/// </param>
public sealed record ListedHostResponse(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    int HostTokens,
    DateTimeOffset? LastReportedAt,
    int Projects);

/// <summary>
/// One span of a read, carrying both the average across it and the highest
/// reading in it.
/// </summary>
/// <remarks>
/// The peak rides beside the average because an average is precisely what hides
/// the spike that was worth finding. <paramref name="MemoryTotal"/> is not
/// averaged: it is how large the machine is rather than how much of it was in
/// use.
/// </remarks>
/// <param name="Start">
/// The beginning of the span. Buckets are contiguous and equal, so the next
/// one's start is this one's end.
/// </param>
public sealed record SampleBucketResponse(
    DateTimeOffset Start,
    double CpuAverage,
    double CpuPeak,
    long MemoryUsedAverage,
    long MemoryUsedPeak,
    long MemoryTotal,
    double LoadAverage,
    double LoadPeak);

/// <summary>One span of a read of one of the host's filesystems.</summary>
public sealed record FilesystemBucketResponse(
    DateTimeOffset Start,
    string Mount,
    long UsedAverage,
    long UsedPeak,
    long Total);

/// <summary>
/// What a host reported over a range, bucketed.
/// </summary>
/// <remarks>
/// A range with nothing in it is two empty lists rather than an absence: a
/// machine that was switched off reported nothing, which is an answer, and the
/// band draws the gap rather than drawing through it.
/// </remarks>
/// <param name="BucketSeconds">
/// How long one span is, which the caller does not choose and therefore has to
/// be told. It is what turns a list of spans back into a picture: a band draws
/// a run the host reported in as a run, and the distance between two spans that
/// is wider than this as the gap it is.
/// </param>
/// <param name="HostName">
/// Which machine this is, which the band over a project's entries has no other
/// way to learn: it is drawn for the host the open project sits on, and the
/// project carries that host's identity and not its name. It rides along here
/// rather than being a second request because the read had to find the host
/// anyway — the same argument that puts it on the agent's answer.
/// </param>
public sealed record SampleWindowResponse(
    string HostName,
    double BucketSeconds,
    IEnumerable<SampleBucketResponse> Samples,
    IEnumerable<FilesystemBucketResponse> Filesystems);

/// <inheritdoc cref="RetentionWindowRequest"/>
public sealed record SampleRetentionRequest(int RetentionDays);

/// <summary>How long the installation keeps samples, as it stands.</summary>
public sealed record SampleRetentionResponse(int RetentionDays);

/// <summary>
/// What a window would put outside itself, read before it is applied.
/// </summary>
/// <param name="RetentionDays">
/// The window that was asked about, echoed back so that an answer arriving after
/// the operator has moved the field on is recognizable as the answer to the
/// question it was.
/// </param>
/// <param name="Samples">
/// How many readings the sweep would remove, across every host — because the
/// window spans the installation rather than a machine.
/// </param>
public sealed record SamplesOutsideWindowResponse(int RetentionDays, long Samples);

/// <summary>
/// The operator's host acts, reached over HTTP, and the one window that governs
/// every host's samples.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these is behind the operator's session. An agent is given a
/// host's samples and the identity of the host a project sits on, because those
/// are facts it reads; it cannot make a host, name one, end one, mint its token
/// or say where a project runs. Those are the administering half of MCP and are
/// absent from the reading token's list rather than forbidden on it (ADR 0046).
/// </para>
/// <para>
/// <b>Creating a host does not hand back the command that starts its
/// collector.</b> Issuing its token does (<see cref="CollectorCommand"/>), which
/// is the ingest token's arrangement exactly: creating a project hands back no
/// delivery snippet either, because the snippet is a thing made out of a
/// credential and creating the unit is not minting one. What
/// <c>docs/ui.md</c> promises — that creating a host in the settings gives back
/// the finished command — is the screen making both calls, the way it does for a
/// new project.
/// </para>
/// <para>
/// <b>Deletion is not confirmed here.</b> It is confirmed by typing the host's
/// name, and that guard is the screen's, for the reason a project's is: this
/// route takes no name and compares none, because repeating it back would
/// protect nobody who issued the <c>DELETE</c> deliberately.
/// </para>
/// <para>
/// The sample retention routes are here rather than beside a project's because
/// there is one of them for the whole installation
/// (<c>docs/metrics.md</c>) — and they are under <c>/samples</c> rather than
/// under a host for the same reason, even though the screen that carries them is
/// the one that lists hosts.
/// </para>
/// </remarks>
public static class HostEndpoints
{
    public static IEndpointRouteBuilder MapHosts(this IEndpointRouteBuilder endpoints)
    {
        var operatorSurface = endpoints
            .MapGroup(string.Empty)
            .RequireAuthorization()
            .RequireRateLimiting(PublicRateLimits.Operator);

        operatorSurface.MapHostActs();
        operatorSurface.MapSampleRetention();

        return endpoints;
    }

    private static void MapHostActs(this IEndpointRouteBuilder endpoints)
    {
        var hosts = endpoints.MapGroup("/hosts");

        hosts.MapPost(string.Empty, async (
                HostRequest request,
                CreateHost create,
                CancellationToken cancellationToken) =>
            {
                if (!IsAName(request.Name))
                {
                    return NotAName();
                }

                var created = await create.ExecuteAsync(request.Name!, cancellationToken);

                // Two machines called `web` is the trap this refuses. The name is
                // the operator's to change afterwards, so a taken one is a
                // conflict with what the installation already holds rather than a
                // malformed request.
                return created.Outcome is CreateHostOutcome.NameTaken
                    ? Results.Conflict()
                    : Results.Created($"/hosts/{created.Host!.Id}", Shown(created.Host));
            })
            .WithName("CreateHost")
            .WithSummary("Brings a host into existence, which is the only way one comes about.")
            .Produces<HostResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        hosts.MapGet(string.Empty, async (
                ListHosts list,
                CancellationToken cancellationToken) =>
            {
                // A host that has never reported is in this answer rather than
                // left out of it: it is something the operator made, and a list
                // that omitted it would answer where the machine they just added
                // went.
                var held = await list.ExecuteAsync(cancellationToken);

                return Results.Ok(held.Select(host => new ListedHostResponse(
                    host.Id,
                    host.Name,
                    host.CreatedAt,
                    host.HostTokens,
                    host.LastReportedAt,
                    host.Projects)));
            })
            .WithName("ListHosts")
            .WithSummary("Every host the installation holds.")
            .Produces<IEnumerable<ListedHostResponse>>();

        hosts.MapPatch("/{id:guid}", async (
                Guid id,
                HostRequest request,
                RenameHost rename,
                CancellationToken cancellationToken) =>
            {
                if (!IsAName(request.Name))
                {
                    return NotAName();
                }

                // A rename moves nothing: the samples, the token and the projects
                // sitting on this machine are attached to the identity, and no
                // collector notices.
                return await rename.ExecuteAsync(id, request.Name!, cancellationToken) switch
                {
                    RenameHostOutcome.Renamed => Results.NoContent(),
                    RenameHostOutcome.NameTaken => Results.Conflict(),
                    _ => Results.NotFound(),
                };
            })
            .WithName("RenameHost")
            .WithSummary("Gives a host another name; the identity is not one.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        hosts.MapDelete("/{id:guid}", async (
                Guid id,
                DeleteHost delete,
                CancellationToken cancellationToken) =>
                // The host and its tokens go at once and its samples follow in
                // the background, exactly as a deleted project's entries do
                // (ADR 0019). The projects that sat on it are left sitting on
                // none and lose nothing else.
                await delete.ExecuteAsync(id, cancellationToken)
                    ? Results.NoContent()
                    : Results.NotFound())
            .WithName("DeleteHost")
            .WithSummary("Ends a host and the history of the machine behind it.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        hosts.MapGet("/{id:guid}/samples", async (
                Guid id,
                DateTimeOffset from,
                DateTimeOffset to,
                ReadSamples read,
                CancellationToken cancellationToken) =>
            {
                // The caller names a range and the installation says how it
                // divided it, exactly as the agent's tool works. A count on the
                // wire was offered here first, on the theory that the band knows
                // how wide it is on the screen — but the range already answers
                // that (a bucket is never finer than the interval that fills it,
                // and never more than two hundred of them), and a parameter no
                // caller has a better answer for is a parameter to be wrong
                // about.
                var span = (to - from).Duration();
                var count = BucketCount.For(span);

                var samples = await read.ExecuteAsync(id, from, to, count, cancellationToken);

                if (samples is null)
                {
                    return Results.NotFound();
                }

                return samples.Expired
                    ? ReadExpiredResponse.Of(samples.Narrow)
                    : Results.Ok(Shown(samples.Answer!, span / count.Value));
            })
            .WithName("ReadHostSamples")
            .WithSummary("What one host reported over a range, in equal spans.")
            .Produces<SampleWindowResponse>()
            .Produces<ReadExpiredResponse>(StatusCodes.Status408RequestTimeout)
            .Produces(StatusCodes.Status404NotFound);

        hosts.MapGet("/{id:guid}/mounts", async (
                Guid id,
                ListTheMountsAHostReports mounts,
                CancellationToken cancellationToken) =>
            {
                // What the operator picks the installation's own mount out of
                // (`docs/alerts.md`). It is the newest sample's filesystems
                // rather than a list anybody maintains, which is the shape a
                // filter's values have and is the same argument (ADR 0029).
                //
                // An empty answer is an ordinary one and not a 404: a machine
                // that has never reported and one whose collector was told to
                // watch nothing put the same choice in front of the operator,
                // which is none.
                var reported = await mounts.ExecuteAsync(id, cancellationToken);

                return Results.Ok(reported.Select(mount => mount.Value));
            })
            .WithName("ListHostMounts")
            .WithSummary("The filesystems one machine last reported on.")
            .Produces<IEnumerable<string>>();
    }

    private static void MapSampleRetention(this IEndpointRouteBuilder endpoints)
    {
        var samples = endpoints.MapGroup("/samples/retention");

        samples.MapGet(string.Empty, async (
                ChangeSampleRetention retention,
                CancellationToken cancellationToken) =>
            {
                var window = await retention.ReadAsync(cancellationToken);

                return Results.Ok(new SampleRetentionResponse(window.Days));
            })
            .WithName("ReadSampleRetention")
            .WithSummary("How long this installation keeps every host's samples.")
            .Produces<SampleRetentionResponse>();

        samples.MapGet("/outside", async (
                int retentionDays,
                ChangeSampleRetention retention,
                CancellationToken cancellationToken) =>
            {
                // Refused where every other window is. There is no answering
                // "and this is what two years would keep", because that is not a
                // window an installation has (ADR 0020).
                if (!RetentionWindow.TryOfDays(retentionDays, out var proposed))
                {
                    return NotAWindow();
                }

                var outside = await retention.CountOutsideAsync(proposed, cancellationToken);

                return Results.Ok(new SamplesOutsideWindowResponse(retentionDays, outside));
            })
            .WithName("CountSamplesOutsideWindow")
            .WithSummary("How many samples a retention window would remove, before it is applied.")
            .Produces<SamplesOutsideWindowResponse>()
            .ProducesValidationProblem();

        samples.MapGet("/footprint", async (
                int retentionDays,
                ReadTheFootprint footprint,
                CancellationToken cancellationToken) =>
            {
                if (!RetentionWindow.TryOfDays(retentionDays, out var proposed))
                {
                    return NotAWindow();
                }

                // The project's three numbers, with the middle one worked out
                // from what the collectors report rather than from a tally: a
                // machine writes a row a minute and its filesystems' rows beside
                // it, so the rate is the product's and not a thing to measure
                // (ADR 0048).
                var cost = await footprint.OfSamplesAsync(proposed, cancellationToken);

                return Results.Ok(FootprintResponse.Of(retentionDays, cost));
            })
            .WithName("ReadSampleFootprint")
            .WithSummary("What a sample retention window will cost, before it is applied.")
            .Produces<FootprintResponse>()
            .ProducesValidationProblem();

        samples.MapPut(string.Empty, async (
                SampleRetentionRequest request,
                ChangeSampleRetention retention,
                CancellationToken cancellationToken) =>
            {
                if (!RetentionWindow.TryOfDays(request.RetentionDays, out var window))
                {
                    return NotAWindow();
                }

                // Lowering it puts samples outside the window and the sweep
                // removes them; raising it brings nothing back. How many is the
                // read above, and it is a route of its own for the reason the
                // project's is: the warning is a screen in front of this act, and
                // this stays a write with no reading behaviour in it.
                await retention.ExecuteAsync(window, cancellationToken);

                return Results.NoContent();
            })
            .WithName("ChangeSampleRetention")
            .WithSummary("Changes how long this installation keeps every host's samples.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();
    }

    private static HostResponse Shown(Host host) => new(host.Id, host.Name, host.CreatedAt);

    private static SampleWindowResponse Shown(HostSamples read, TimeSpan span) => new(
        read.Name,
        span.TotalSeconds,
        read.Window.Samples.Select(bucket => new SampleBucketResponse(
            bucket.Start,
            bucket.CpuAverage,
            bucket.CpuPeak,
            bucket.MemoryUsedAverage,
            bucket.MemoryUsedPeak,
            bucket.MemoryTotal,
            bucket.LoadAverage,
            bucket.LoadPeak)),
        read.Window.Filesystems.Select(bucket => new FilesystemBucketResponse(
            bucket.Start,
            bucket.MountPath.Value,
            bucket.UsedAverage,
            bucket.UsedPeak,
            bucket.Total)));

    /// <summary>
    /// The domain refuses a name that is not one as a backstop; a caller taking
    /// it from a person says so first, and this is that.
    /// </summary>
    private static bool IsAName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= Host.NameMaxLength;

    private static IResult NotAName() => Results.ValidationProblem(
        new Dictionary<string, string[]>
        {
            ["name"] =
            [
                "A host has a name, of at most "
                + $"{Host.NameMaxLength} characters.",
            ],
        });

    private static IResult NotAWindow() => Results.ValidationProblem(
        new Dictionary<string, string[]>
        {
            ["retentionDays"] =
            [
                "A retention window is between "
                + $"{RetentionWindow.MinimumDays} and {RetentionWindow.MaximumDays} days.",
            ],
        });
}
