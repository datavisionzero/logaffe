using Logaffe.Application.Operations;
using Microsoft.AspNetCore.RateLimiting;

namespace Logaffe.Api.Http;

/// <summary>
/// One person's assignment to one project, as the list an administrator works.
/// </summary>
/// <param name="Name">
/// Who it is, so that a list of assignments reads as people rather than as
/// identifiers.
/// </param>
public sealed record ProjectAccessResponse(
    Guid UserId, string Name, string Email, Guid GrantedBy, DateTimeOffset GrantedAt);

/// <summary>
/// Who reaches a project, and the two acts that change it (ADR 0055).
/// </summary>
/// <remarks>
/// <para>
/// <b>It is an administrator's surface and the only one that changes what
/// somebody else can see.</b> Everything else on this installation narrows to
/// the caller's own reach; these three routes are the exception, and they are
/// the reason the role exists.
/// </para>
/// <para>
/// <b>The project is looked up against the installation and not against the
/// administrator's own reach.</b> Handing out access is exactly the case where
/// somebody works on a project they cannot open, and requiring them to assign it
/// to themselves first would make the role self-granting by the back door.
/// </para>
/// <para>
/// Withdrawing takes effect on that person's next request: a reach is resolved
/// per request and nothing caches it in front.
/// </para>
/// </remarks>
public static class ProjectAccessEndpoints
{
    public static IEndpointRouteBuilder MapProjectAccess(this IEndpointRouteBuilder endpoints)
    {
        var administered = endpoints
            .MapGroup("/projects/{id:guid}/access")
            .RequireAuthorization(SessionAuthentication.AdministratorPolicy)
            .RequireRateLimiting(PublicRateLimits.Operator);

        administered.MapGet(string.Empty, async (
                Guid id,
                ListProjectAccess list,
                CancellationToken cancellationToken) =>
            {
                var held = await list.ExecuteAsync(id, cancellationToken);

                return held is null
                    ? Results.NotFound()
                    : Results.Ok(held.Select(access => new ProjectAccessResponse(
                        access.UserId,
                        access.Name,
                        access.Email,
                        access.GrantedBy,
                        access.GrantedAt)));
            })
            .WithName("ListProjectAccess")
            .WithSummary("Who reaches this project.")
            .Produces<IEnumerable<ProjectAccessResponse>>()
            .Produces(StatusCodes.Status404NotFound);

        // A `PUT` rather than a `POST`: assigning a project to somebody who
        // already has it is the state that was asked for rather than a second
        // assignment, and idempotence is what a second click deserves.
        administered.MapPut("/{userId:guid}", async (
                Guid id,
                Guid userId,
                AssignProject assign,
                HttpContext context,
                CancellationToken cancellationToken) =>
                await assign.GrantAsync(
                    id, userId, context.CurrentUser().Id, cancellationToken) switch
                {
                    AssignProjectOutcome.Assigned => Results.NoContent(),
                    _ => Results.NotFound(),
                })
            .WithName("GrantProjectAccess")
            .WithSummary("Puts a project within somebody's reach.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        administered.MapDelete("/{userId:guid}", async (
                Guid id,
                Guid userId,
                AssignProject assign,
                CancellationToken cancellationToken) =>
                await assign.WithdrawAsync(id, userId, cancellationToken) switch
                {
                    AssignProjectOutcome.Withdrawn => Results.NoContent(),
                    _ => Results.NotFound(),
                })
            .WithName("WithdrawProjectAccess")
            .WithSummary("Takes it back, from that person's next request on.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
