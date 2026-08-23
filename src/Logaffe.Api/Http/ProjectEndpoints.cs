using Logaffe.Application.Operations;
using Logaffe.Domain.Projects;
using Microsoft.AspNetCore.RateLimiting;

namespace Logaffe.Api.Http;

/// <param name="Name">
/// Unique within the project's group, and the only thing about a project that a
/// person reads.
/// </param>
/// <param name="RetentionDays">
/// How long the project keeps its entries, counted from receipt time, up to a
/// ceiling no installation can raise (ADR 0020).
/// </param>
/// <param name="GroupId">
/// The group to list the project under, or <c>null</c> for none — which is what
/// a caller that says nothing about it gets. Creating a project and putting it
/// where it belongs is one errand.
/// </param>
public sealed record CreateProjectRequest(string? Name, int RetentionDays, Guid? GroupId);

/// <inheritdoc cref="CreateProjectRequest"/>
public sealed record RenameProjectRequest(string? Name);

/// <inheritdoc cref="CreateProjectRequest"/>
public sealed record RetentionWindowRequest(int RetentionDays);

/// <summary>
/// What a window would put outside itself, read before it is applied.
/// </summary>
/// <param name="RetentionDays">
/// The window that was asked about, echoed back so that an answer arriving after
/// the operator has moved the field on is recognizable as the answer to the
/// question it was.
/// </param>
/// <param name="Entries">
/// How many of the project's entries the sweep would remove. Zero is the
/// ordinary answer — it is also what raising a window gives — and it is not a
/// warning.
/// </param>
public sealed record EntriesOutsideWindowResponse(int RetentionDays, long Entries);

/// <summary>
/// Where a project is listed: the identity of one of the installation's groups,
/// or <c>null</c> for no group at all.
/// </summary>
public sealed record ProjectGroupRequest(Guid? GroupId);

/// <summary>
/// Where a project runs: the identity of one of the installation's hosts, or
/// <c>null</c> for a machine this installation does not track.
/// </summary>
public sealed record ProjectHostRequest(Guid? HostId);

/// <summary>
/// Whether this project's alert conditions are evaluated.
/// </summary>
/// <remarks>
/// One flag rather than a mute per condition. The project a batch job writes
/// into at three in the morning is the project whose silence at four is not an
/// incident either, so the two conditions are muted by the same fact — and a
/// mute per condition is the beginning of the per-project configuration ADR 0050
/// exists to refuse.
/// </remarks>
public sealed record ProjectMuteRequest(bool Muted);

/// <summary>One project, by itself.</summary>
/// <param name="HostId">
/// The machine it runs on, or <c>null</c> for none — which is every project
/// until the operator says otherwise, and which costs nothing except that there
/// is no band to draw over its entries.
/// </param>
/// <param name="Muted">
/// Whether the alert conditions are evaluated for this project at all. It is
/// beside the group and the host because it is a fact about this project and
/// about nothing else (<c>docs/alerts.md</c>).
/// </param>
public sealed record ProjectResponse(
    Guid Id,
    string Name,
    Guid? GroupId,
    Guid? HostId,
    int RetentionDays,
    DateTimeOffset CreatedAt,
    bool Muted);

/// <summary>
/// One project on the list a session starts at.
/// </summary>
/// <param name="IngestTokens">
/// One ordinarily, two while the project is being rotated, and none for a
/// project whose door is closed — which is the reading this column is on the
/// list for.
/// </param>
/// <param name="LastReceivedAt">
/// When the project last received an entry, or <c>null</c> when it never has.
/// This is the fact the row is read for: whether the application behind it is
/// still delivering.
/// </param>
/// <param name="GroupId">
/// The group the project is listed under, or <c>null</c> for one in no group.
/// The group's name is on the group list rather than repeated on every row,
/// which is also what lets a group holding no projects be shown at all.
/// </param>
/// <param name="HostId">
/// The machine the project runs on, or <c>null</c> for one on no host. The
/// host's name is on the host list for the group's reason, and this is what says
/// whether there is a band to draw above this project's entries.
/// </param>
/// <inheritdoc cref="ProjectResponse" path="/param[@name='Muted']"/>
public sealed record ListedProjectResponse(
    Guid Id,
    string Name,
    Guid? GroupId,
    Guid? HostId,
    int RetentionDays,
    DateTimeOffset CreatedAt,
    int IngestTokens,
    DateTimeOffset? LastReceivedAt,
    bool Muted);

/// <summary>
/// The operator's project acts, reached over HTTP.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these is behind the operator's session, and every one of them
/// has a tool beside it on the administering half of MCP, which calls these same
/// use cases and adds nothing to them. What reaches none of them is a reading
/// token: it reads entries and counts them, and it cannot bring a project into
/// existence or end one — not as a permission but as an absence from the list of
/// five it is handed (ADR 0046).
/// </para>
/// <para>
/// <b>Creating a project says which group it is listed under and not which host
/// it runs on</b>, though the two relations have the same shape
/// (<c>docs/metrics.md</c>). The group is the heading the operator is already
/// choosing while they type the name; the machine is a fact about a deployment
/// that does not exist yet when the project is made, so it arrives afterwards
/// through a route of its own.
/// </para>
/// <para>
/// <b>Deletion is not confirmed here.</b> It is confirmed by typing the
/// project's name, and that guard is the screen's: this route takes no name and
/// compares none, because repeating it back would protect nobody who issued the
/// <c>DELETE</c> deliberately and would make one route answer to a rule none of
/// the others do.
/// </para>
/// </remarks>
public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjects(this IEndpointRouteBuilder endpoints)
    {
        var operatorSurface = endpoints
            .MapGroup("/projects")
            .RequireAuthorization()
            .RequireRateLimiting(PublicRateLimits.Operator);

        operatorSurface.MapPost(string.Empty, async (
                CreateProjectRequest request,
                CreateProject create,
                CancellationToken cancellationToken) =>
            {
                if (!IsAName(request.Name))
                {
                    return NotAName();
                }

                if (!RetentionWindow.TryOfDays(request.RetentionDays, out var retention))
                {
                    return NotAWindow();
                }

                var created = await create.ExecuteAsync(
                    request.Name!, retention, request.GroupId, cancellationToken);

                // Two projects called `api` is a trap for the operator reaching
                // for one of them at three in the morning. The name is theirs to
                // change afterwards, so a taken one is a conflict with what the
                // installation already holds rather than a malformed request —
                // and a group that is gone is the same answer as any other
                // identity this installation does not hold.
                return created.Outcome switch
                {
                    CreateProjectOutcome.Created => Results.Created(
                        $"/projects/{created.Project!.Id}", Shown(created.Project)),
                    CreateProjectOutcome.NameTaken => Results.Conflict(),
                    _ => Results.NotFound(),
                };
            })
            .WithName("CreateProject")
            .WithSummary("Brings a project into existence, which is the only way one comes about.")
            .Produces<ProjectResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        operatorSurface.MapGet(string.Empty, async (
                ListProjects list,
                CancellationToken cancellationToken) =>
            {
                var held = await list.ExecuteAsync(cancellationToken);

                // No count of entries beside a project. That is a query over the
                // largest table in the database for a number nobody asked for,
                // and it is what makes this a list rather than a dashboard.
                // When it last received one is the opposite kind of read — one
                // lookup at the end of an index — and it is the fact the row is
                // read for.
                return Results.Ok(held.Select(project => new ListedProjectResponse(
                    project.Id,
                    project.Name,
                    project.GroupId,
                    project.HostId,
                    project.Retention.Days,
                    project.CreatedAt,
                    project.IngestTokens,
                    project.LastReceivedAt,
                    project.Muted)));
            })
            .WithName("ListProjects")
            .WithSummary("Every project the installation holds.")
            .Produces<IEnumerable<ListedProjectResponse>>();

        operatorSurface.MapGet("/{id:guid}", async (
                Guid id,
                ReadProject read,
                CancellationToken cancellationToken) =>
            {
                var project = await read.ExecuteAsync(id, cancellationToken);

                return project is null ? Results.NotFound() : Results.Ok(Shown(project));
            })
            .WithName("ReadProject")
            .WithSummary("One project, which is what its settings are reached by.")
            .Produces<ProjectResponse>()
            .Produces(StatusCodes.Status404NotFound);

        operatorSurface.MapPatch("/{id:guid}", async (
                Guid id,
                RenameProjectRequest request,
                RenameProject rename,
                CancellationToken cancellationToken) =>
            {
                if (!IsAName(request.Name))
                {
                    return NotAName();
                }

                // A rename moves nothing: entries, tokens and queries are
                // attached to the identity, so no sender notices and nothing has
                // to be redeployed.
                return await rename.ExecuteAsync(id, request.Name!, cancellationToken) switch
                {
                    RenameOutcome.Renamed => Results.NoContent(),
                    RenameOutcome.NameTaken => Results.Conflict(),
                    _ => Results.NotFound(),
                };
            })
            .WithName("RenameProject")
            .WithSummary("Gives a project another name; the identity is not one.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        operatorSurface.MapPut("/{id:guid}/group", async (
                Guid id,
                ProjectGroupRequest request,
                MoveProjectToGroup move,
                CancellationToken cancellationToken) =>
                // A move changes the heading the project is listed under and
                // nothing else: entries, tokens and queries are attached to the
                // identity, so no sender notices. A name already taken where it
                // is going is refused rather than resolved — renaming a project
                // the operator did not ask to rename is not this route's to do.
                await move.ExecuteAsync(id, request.GroupId, cancellationToken) switch
                {
                    MoveProjectOutcome.Moved => Results.NoContent(),
                    MoveProjectOutcome.NameTaken => Results.Conflict(),
                    _ => Results.NotFound(),
                })
            .WithName("MoveProjectToGroup")
            .WithSummary("Lists a project under another group, or under none.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        operatorSurface.MapPut("/{id:guid}/host", async (
                Guid id,
                ProjectHostRequest request,
                PutProjectOnHost put,
                CancellationToken cancellationToken) =>
                // It moves nothing: entries, tokens and queries are attached to
                // the identity, so no sender notices and nothing is redeployed.
                // What it changes is whether there is a band to draw over this
                // project's entries.
                //
                // Unlike the group above there is no name to be taken. A
                // project's name is unique within its group and a host is not a
                // group — two projects called `api` may perfectly well run on one
                // machine, because the host is not where they are listed and not
                // a scope they are found in (`docs/metrics.md`).
                await put.ExecuteAsync(id, request.HostId, cancellationToken) switch
                {
                    PutProjectOnHostOutcome.PutOn => Results.NoContent(),
                    _ => Results.NotFound(),
                })
            .WithName("PutProjectOnHost")
            .WithSummary("Says which machine a project runs on, or that none is tracked.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        operatorSurface.MapPut("/{id:guid}/muted", async (
                Guid id,
                ProjectMuteRequest request,
                MuteAProject mute,
                CancellationToken cancellationToken) =>
                // It changes what is evaluated and nothing else. What a muted
                // project receives, keeps and answers is exactly what it was:
                // the tally is still written and the sweep still runs, and the
                // hourly pass simply does not ask about it.
                //
                // There is no route beside this one for a single condition. The
                // switch and the mute are the whole of what is adjustable about
                // alerting (ADR 0050), and a mute per condition would be the
                // beginning of what that decision refuses.
                await mute.ExecuteAsync(id, request.Muted, cancellationToken) switch
                {
                    MuteAProjectOutcome.Muted => Results.NoContent(),
                    _ => Results.NotFound(),
                })
            .WithName("MuteAProject")
            .WithSummary("Takes a project out of the alert conditions, or puts it back in.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        operatorSurface.MapGet("/{id:guid}/retention/outside", async (
                Guid id,
                int retentionDays,
                CountEntriesOutsideWindow count,
                CancellationToken cancellationToken) =>
            {
                // Refused where every other window is. There is no answering
                // "and this is what two years would keep", because that is not a
                // window an installation has (ADR 0020).
                if (!RetentionWindow.TryOfDays(retentionDays, out var proposed))
                {
                    return NotAWindow();
                }

                var outside = await count.ExecuteAsync(id, proposed, cancellationToken);

                return outside is null
                    ? Results.NotFound()
                    : Results.Ok(new EntriesOutsideWindowResponse(retentionDays, outside.Value));
            })
            .WithName("CountEntriesOutsideWindow")
            .WithSummary("How many entries a retention window would remove, before it is applied.")
            .Produces<EntriesOutsideWindowResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        operatorSurface.MapGet("/{id:guid}/retention/footprint", async (
                Guid id,
                int retentionDays,
                ReadTheFootprint footprint,
                CancellationToken cancellationToken) =>
            {
                // Refused where every other window is, for the reason the count
                // beside it is: what a window costs is asked about windows this
                // installation has.
                if (!RetentionWindow.TryOfDays(retentionDays, out var proposed))
                {
                    return NotAWindow();
                }

                // Asked on every keystroke, and it stays cheap on purpose: one
                // call for the size of the store, a handful of tally rows, and
                // the newest report of one host. Nothing here grows with the
                // entries (ADR 0048).
                var cost = await footprint.OfProjectAsync(id, proposed, cancellationToken);

                return cost is null
                    ? Results.NotFound()
                    : Results.Ok(FootprintResponse.Of(retentionDays, cost));
            })
            .WithName("ReadProjectFootprint")
            .WithSummary("What a retention window will cost, before it is applied.")
            .Produces<FootprintResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        operatorSurface.MapPut("/{id:guid}/retention", async (
                Guid id,
                RetentionWindowRequest request,
                ChangeRetentionWindow change,
                CancellationToken cancellationToken) =>
            {
                if (!RetentionWindow.TryOfDays(request.RetentionDays, out var retention))
                {
                    return NotAWindow();
                }

                // Lowering it puts entries outside the window and the sweep
                // removes them. How many is the read above, and it is a route of
                // its own rather than a flag on this one: the warning is a
                // screen in front of this act, and this stays a write with no
                // reading behaviour in it.
                return await change.ExecuteAsync(id, retention, cancellationToken)
                    ? Results.NoContent()
                    : Results.NotFound();
            })
            .WithName("ChangeRetentionWindow")
            .WithSummary("Changes how long a project keeps its entries.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        operatorSurface.MapDelete("/{id:guid}", async (
                Guid id,
                DeleteProject delete,
                CancellationToken cancellationToken) =>
                // Immediate and irreversible: the project, its tokens and its
                // visibility go at once, and its entries follow in the
                // background (ADR 0019). A project already gone is 404, which is
                // a second click or another tab and not a failure.
                await delete.ExecuteAsync(id, cancellationToken)
                    ? Results.NoContent()
                    : Results.NotFound())
            .WithName("DeleteProject")
            .WithSummary("Ends a project, immediately and irreversibly.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static ProjectResponse Shown(Project project) => new(
        project.Id,
        project.Name,
        project.GroupId,
        project.HostId,
        project.Retention.Days,
        project.CreatedAt,
        project.Muted);

    /// <summary>
    /// The domain refuses a name that is not one as a backstop; a caller taking
    /// it from a person says so first, and this is that.
    /// </summary>
    private static bool IsAName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= Project.NameMaxLength;

    private static IResult NotAName() => Results.ValidationProblem(
        new Dictionary<string, string[]>
        {
            ["name"] =
            [
                "A project has a name, of at most "
                + $"{Project.NameMaxLength} characters.",
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
