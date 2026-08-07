using Logaffe.Application.Operations;
using Logaffe.Domain.Projects;
using Microsoft.AspNetCore.RateLimiting;

namespace Logaffe.Api.Http;

/// <param name="Name">
/// Unique within the installation, and the only thing about a project that a
/// person reads.
/// </param>
/// <param name="RetentionDays">
/// How long the project keeps its entries, counted from receipt time, up to a
/// ceiling no installation can raise (ADR 0020).
/// </param>
public sealed record CreateProjectRequest(string? Name, int RetentionDays);

/// <inheritdoc cref="CreateProjectRequest"/>
public sealed record RenameProjectRequest(string? Name);

/// <inheritdoc cref="CreateProjectRequest"/>
public sealed record RetentionWindowRequest(int RetentionDays);

/// <summary>One project, by itself.</summary>
public sealed record ProjectResponse(
    Guid Id, string Name, int RetentionDays, DateTimeOffset CreatedAt);

/// <summary>
/// One project on the list a session starts at.
/// </summary>
/// <param name="IngestTokens">
/// One ordinarily, two while the project is being rotated, and none for a
/// project whose door is closed — which is the reading this column is on the
/// list for.
/// </param>
public sealed record ListedProjectResponse(
    Guid Id,
    string Name,
    int RetentionDays,
    DateTimeOffset CreatedAt,
    int IngestTokens);

/// <summary>
/// The operator's project acts, reached over HTTP.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these is behind the operator's session and none of them is
/// reachable over MCP — not as a permission but as an absence from that
/// interface, which offers four read tools and nothing else (ADR 0018). An
/// agent reads entries and counts them; it cannot bring a project into
/// existence or end one.
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

                var project = await create.ExecuteAsync(
                    request.Name!, retention, cancellationToken);

                // Two projects called `api` is a trap for the operator reaching
                // for one of them at three in the morning. The name is theirs to
                // change afterwards, so a taken one is a conflict with what the
                // installation already holds rather than a malformed request.
                return project is null
                    ? Results.Conflict()
                    : Results.Created($"/projects/{project.Id}", Shown(project));
            })
            .WithName("CreateProject")
            .WithSummary("Brings a project into existence, which is the only way one comes about.")
            .Produces<ProjectResponse>(StatusCodes.Status201Created)
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
                return Results.Ok(held.Select(project => new ListedProjectResponse(
                    project.Id,
                    project.Name,
                    project.Retention.Days,
                    project.CreatedAt,
                    project.IngestTokens)));
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
                // removes them. What the operator is told first — how many —
                // is a count over the entry table, which does not exist yet.
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

    private static ProjectResponse Shown(Project project) =>
        new(project.Id, project.Name, project.Retention.Days, project.CreatedAt);

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
