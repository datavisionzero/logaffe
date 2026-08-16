using Logaffe.Application.Operations;
using Logaffe.Domain.Projects;
using Microsoft.AspNetCore.RateLimiting;

namespace Logaffe.Api.Http;

/// <param name="Name">
/// Unique within the installation, and the whole of what a group is.
/// </param>
public sealed record GroupRequest(string? Name);

/// <summary>One group, by itself.</summary>
public sealed record GroupResponse(Guid Id, string Name, DateTimeOffset CreatedAt);

/// <summary>
/// One group on the list the operator reads.
/// </summary>
/// <param name="Projects">
/// How many projects it holds. Zero is an ordinary answer — a group made before
/// its first project, or left behind by its last — and it is what the screen says
/// before removing one.
/// </param>
public sealed record ListedGroupResponse(
    Guid Id, string Name, DateTimeOffset CreatedAt, int Projects);

/// <summary>
/// The operator's group acts, reached over HTTP.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these is behind the operator's session and none of them is
/// reachable over MCP (ADR 0018). An agent is told which group a project is in,
/// because that is a fact about the project it is reading; it cannot make one,
/// rename one, or move a project between them.
/// </para>
/// <para>
/// <b>A group carries a name and nothing else</b> (ADR 0039). There is no
/// retention its projects inherit, no token, no colour and no description, and
/// there is nothing here that reads across the projects it holds — a query names
/// one project, and a group is not one.
/// </para>
/// </remarks>
public static class GroupEndpoints
{
    public static IEndpointRouteBuilder MapGroups(this IEndpointRouteBuilder endpoints)
    {
        var operatorSurface = endpoints
            .MapGroup("/groups")
            .RequireAuthorization()
            .RequireRateLimiting(PublicRateLimits.Operator);

        operatorSurface.MapPost(string.Empty, async (
                GroupRequest request,
                CreateGroup create,
                CancellationToken cancellationToken) =>
            {
                if (!IsAName(request.Name))
                {
                    return NotAName();
                }

                var group = await create.ExecuteAsync(request.Name!, cancellationToken);

                return group is null
                    ? Results.Conflict()
                    : Results.Created($"/groups/{group.Id}", Shown(group));
            })
            .WithName("CreateGroup")
            .WithSummary("Makes a group, which is the only way one comes about.")
            .Produces<GroupResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        operatorSurface.MapGet(string.Empty, async (
                ListGroups list,
                CancellationToken cancellationToken) =>
            {
                // A group holding nothing is in this answer rather than left out
                // of it: it is something the operator made, and a list that
                // omitted it would answer where the group they just made went.
                var held = await list.ExecuteAsync(cancellationToken);

                return Results.Ok(held.Select(group => new ListedGroupResponse(
                    group.Id, group.Name, group.CreatedAt, group.Projects)));
            })
            .WithName("ListGroups")
            .WithSummary("Every group the installation holds, with how many projects each holds.")
            .Produces<IEnumerable<ListedGroupResponse>>();

        operatorSurface.MapPatch("/{id:guid}", async (
                Guid id,
                GroupRequest request,
                RenameGroup rename,
                CancellationToken cancellationToken) =>
            {
                if (!IsAName(request.Name))
                {
                    return NotAName();
                }

                // A rename moves no project: a project points at the group's
                // identity rather than at its name, which is what the identity
                // is for (ADR 0039).
                return await rename.ExecuteAsync(id, request.Name!, cancellationToken) switch
                {
                    RenameGroupOutcome.Renamed => Results.NoContent(),
                    RenameGroupOutcome.NameTaken => Results.Conflict(),
                    _ => Results.NotFound(),
                };
            })
            .WithName("RenameGroup")
            .WithSummary("Gives a group another name; the identity is not one.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        operatorSurface.MapDelete("/{id:guid}", async (
                Guid id,
                DeleteGroup delete,
                CancellationToken cancellationToken) =>
                // Nothing is destroyed: the projects that were in it stay, in no
                // group. That is why this route takes no confirmation of any
                // kind, where deleting a project is confirmed by typing its name
                // on the screen in front of it.
                await delete.ExecuteAsync(id, cancellationToken)
                    ? Results.NoContent()
                    : Results.NotFound())
            .WithName("DeleteGroup")
            .WithSummary("Removes a group and leaves its projects in none.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static GroupResponse Shown(Group group) =>
        new(group.Id, group.Name, group.CreatedAt);

    /// <summary>
    /// The domain refuses a name that is not one as a backstop; a caller taking
    /// it from a person says so first, and this is that.
    /// </summary>
    private static bool IsAName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= Group.NameMaxLength;

    private static IResult NotAName() => Results.ValidationProblem(
        new Dictionary<string, string[]>
        {
            ["name"] =
            [
                "A group has a name, of at most "
                + $"{Group.NameMaxLength} characters.",
            ],
        });
}
