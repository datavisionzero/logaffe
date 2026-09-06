using Logaffe.Application.Operations;
using Logaffe.Domain.Identities;
using Microsoft.AspNetCore.RateLimiting;

namespace Logaffe.Api.Http;

/// <summary>
/// One account as an administrator sees it.
/// </summary>
public sealed record ListedUserResponse(
    Guid Id,
    string Name,
    string Email,
    UserState State,
    bool Administrator,
    bool HasSecondFactor,
    int Projects,
    DateTimeOffset CreatedAt);

/// <summary>
/// Who is signed in, which is what the account menu and the settings say.
/// </summary>
/// <remarks>
/// It carries the role because the interface offers different acts to an
/// administrator, and nothing else: what may be <em>read</em> is not a claim
/// here, it is the reach every read narrows to
/// ([ADR 0055](../../../docs/adr/0055-project-access-is-one-filter.md)).
/// </remarks>
public sealed record MeResponse(Guid Id, string Name, string Email, bool Administrator);

/// <summary>Whether this account administers the installation.</summary>
public sealed record RoleRequest(bool Administrator);

/// <summary>
/// The people on this installation (ADR 0052).
/// </summary>
/// <remarks>
/// <para>
/// <b>The list and the acts on it are an administrator's.</b> Everybody sees
/// themselves, which is what <c>/me</c> is, and nobody sees anybody else's
/// sessions, credentials or second factor — those are that account's own
/// (<c>docs/sign-in.md</c>).
/// </para>
/// <para>
/// <b>Nothing here deletes.</b> An account that should not be used is
/// deactivated, so that everything pointing at it keeps pointing at something.
/// </para>
/// </remarks>
public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUsers(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/me", (HttpContext context) =>
            {
                var user = context.CurrentUser();

                return Results.Ok(new MeResponse(
                    user.Id, user.Name, user.Email, user.Administrator));
            })
            .WithName("Me")
            .WithSummary("Who this request is signed in as.")
            .RequireAuthorization()
            .RequireRateLimiting(PublicRateLimits.Operator)
            .Produces<MeResponse>();

        var administered = endpoints
            .MapGroup("/users")
            .RequireAuthorization(SessionAuthentication.AdministratorPolicy)
            .RequireRateLimiting(PublicRateLimits.Operator);

        administered.MapGet(string.Empty, async (
                ListUsers list, CancellationToken cancellationToken) =>
                Results.Ok((await list.ExecuteAsync(cancellationToken))
                    .Select(user => new ListedUserResponse(
                        user.Id,
                        user.Name,
                        user.Email,
                        user.State,
                        user.Administrator,
                        user.HasSecondFactor,
                        user.Projects,
                        user.CreatedAt))))
            .WithName("ListUsers")
            .WithSummary("Everybody on this installation.")
            .Produces<IEnumerable<ListedUserResponse>>();

        // A removal rather than a `DELETE` on the user: nothing here deletes an
        // identity, and a verb that reads as though it did would be the one
        // thing this surface must not suggest.
        administered.MapPost("/{id:guid}/deactivation", async (
                Guid id, ChangeAUser change, CancellationToken cancellationToken) =>
                Answer(await change.DeactivateAsync(id, cancellationToken)))
            .WithName("DeactivateUser")
            .WithSummary("Closes an account, ends its sessions and silences its agents.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        administered.MapDelete("/{id:guid}/deactivation", async (
                Guid id, ChangeAUser change, CancellationToken cancellationToken) =>
                Answer(await change.ReactivateAsync(id, cancellationToken)))
            .WithName("ReactivateUser")
            .WithSummary("Opens it again, restoring whatever was not individually revoked.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        administered.MapPut("/{id:guid}/role", async (
                Guid id,
                RoleRequest request,
                ChangeAUser change,
                CancellationToken cancellationToken) =>
                Answer(await change.ChangeRoleAsync(
                    id, request.Administrator, cancellationToken)))
            .WithName("ChangeUserRole")
            .WithSummary("Gives the administrator role or takes it back.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        return endpoints;
    }

    /// <summary>
    /// A conflict with what the installation holds rather than anything wrong
    /// with the request: there is always at least one active administrator, and
    /// this would have been the last (ADR 0052).
    /// </summary>
    private static IResult Answer(UserActOutcome outcome) => outcome switch
    {
        UserActOutcome.Done => Results.NoContent(),
        UserActOutcome.TheLastAdministrator => Results.Conflict(),
        _ => Results.NotFound(),
    };
}
