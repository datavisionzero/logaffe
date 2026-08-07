using Logaffe.Api.Hosting;
using Logaffe.Application.Operations;
using Microsoft.AspNetCore.RateLimiting;

namespace Logaffe.Api.Http;

/// <summary>
/// What the operator gives to get in, which is a password and one second
/// factor.
/// </summary>
/// <remarks>
/// There is nothing naming which account is meant: an installation has exactly
/// one operator, with no username and no email address (ADR 0015).
/// </remarks>
/// <param name="SecondFactorCode">
/// The six digits from the authenticator app. Left out when
/// <paramref name="BackupCode"/> is given instead.
/// </param>
/// <param name="BackupCode">
/// A backup code standing in for the second factor, read however it was typed —
/// spacing, grouping and capitals are all forgiven.
/// </param>
public sealed record SignInRequest(
    string? Password, string? SecondFactorCode, string? BackupCode);

/// <summary>
/// What a sign-in answers, which is not the session.
/// </summary>
/// <remarks>
/// The secret went into the cookie and is in no response body anywhere. What is
/// left to say is the one thing <c>docs/sign-in.md</c> requires be said.
/// </remarks>
/// <param name="BackupCodesRemaining">
/// How many codes are left, when one was spent getting in, and <c>null</c> when
/// the authenticator app was used.
/// </param>
public sealed record SignInResponse(int? BackupCodesRemaining);

/// <summary>
/// One of the operator's signed-in browsers, as they see it in the list.
/// </summary>
/// <remarks>
/// It carries no secret and nothing that could be presented: a session is
/// admitted by the value in the cookie, and the row holds only a fast hash of it
/// (ADR 0032).
/// </remarks>
/// <param name="LastSeenFrom">
/// The address it last acted from, or <c>unknown</c> where there was none to
/// read. With no email anywhere in the product (ADR 0015) this column is the
/// only way the operator can ever notice a session that is not theirs.
/// </param>
/// <param name="LastUsedAt">
/// When it last acted, accurate to within five minutes (ADR 0033) and not to be
/// shown as though it were finer.
/// </param>
/// <param name="IsCurrent">
/// Whether this is the browser asking. The server says so because nothing else
/// can: the list carries no secret and the cookie carries nothing but one, so
/// there is nothing the interface could compare — and without it "end all
/// others" is a guess and revoking a row signs the operator out of the screen
/// they are on.
/// </param>
public sealed record ListedSessionResponse(
    Guid Id,
    string LastSeenFrom,
    DateTimeOffset StartedAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt,
    bool IsCurrent);

/// <summary>
/// A session, its list, and the ways it ends that are not a sign-out.
/// </summary>
/// <remarks>
/// <b>Ending a session is removing the row, never marking it.</b> The list is
/// what the operator acts on, and one they ended has to be gone from it rather
/// than greyed out (<c>docs/sign-in.md</c>). It takes effect on the next
/// request, because the session authentication reads the row every time and
/// holds no cache in front of it.
/// </remarks>
public static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessions(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/sign-in", async (
                SignInRequest request,
                SignIn signIn,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                var signedIn = await signIn.ExecuteAsync(
                    request.Password,
                    request.SecondFactorCode,
                    request.BackupCode,
                    context.SeenFrom(),
                    cancellationToken);

                // One refusal for every way of not getting in: a wrong password,
                // a wrong code, a code already spent, and an installation with
                // no operator at all. The screen says one thing, and which of
                // them it was is not something this surface hands over.
                if (signedIn is null)
                {
                    return Results.Unauthorized();
                }

                SessionCookie.Issue(context.Response, signedIn.Secret.Text);

                return Results.Ok(new SignInResponse(signedIn.BackupCodesRemaining));
            })
            .WithName("SignIn")
            .WithSummary("Starts a session for the operator.")
            .RequireRateLimiting(PublicRateLimits.SignIn)
            .AllowAnonymous()
            .Produces<SignInResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests);

        endpoints.MapPost("/sign-out", async (
                SignOut signOut,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                await signOut.ExecuteAsync(context.OperatorSession(), cancellationToken);
                SessionCookie.Clear(context.Response);

                return Results.NoContent();
            })
            .WithName("SignOut")
            .WithSummary("Ends the session this request was made with.")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        endpoints.MapTheList();

        return endpoints;
    }

    private static void MapTheList(this IEndpointRouteBuilder endpoints)
    {
        var operatorSurface = endpoints
            .MapGroup(string.Empty)
            .RequireAuthorization()
            .RequireRateLimiting(PublicRateLimits.Operator);

        operatorSurface.MapGet("/sessions", async (
                ListSessions list, HttpContext context, CancellationToken cancellationToken) =>
            {
                var current = context.OperatorSession();
                var held = await list.ExecuteAsync(cancellationToken);

                return Results.Ok(held.Select(session => new ListedSessionResponse(
                    session.Id,
                    session.LastSeenFrom,
                    session.StartedAt,
                    session.LastUsedAt,
                    session.ExpiresAt,
                    session.Id == current.Id)));
            })
            .WithName("ListSessions")
            .WithSummary("The operator's signed-in browsers.")
            .Produces<IEnumerable<ListedSessionResponse>>();

        operatorSurface.MapDelete("/sessions/others", async (
                EndEveryOtherSession endOthers,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                // Every other, never every one: the browser doing this stays
                // signed in, or securing the installation would sign the
                // operator out of the screen they secured it from.
                await endOthers.ExecuteAsync(context.OperatorSession(), cancellationToken);

                return Results.NoContent();
            })
            .WithName("EndEveryOtherSession")
            .WithSummary("Ends every session but this one.")
            .Produces(StatusCodes.Status204NoContent);

        operatorSurface.MapDelete("/sessions/{id:guid}", async (
                Guid id,
                RevokeSession revoke,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                if (!await revoke.ExecuteAsync(id, cancellationToken))
                {
                    // Already gone: a second click, another tab, or a sweep.
                    return Results.NotFound();
                }

                // Ending your own from the list is a sign-out by another name,
                // and the cookie has to go with it — otherwise the browser keeps
                // presenting a secret whose row is not there any more.
                if (id == context.OperatorSession().Id)
                {
                    SessionCookie.Clear(context.Response);
                }

                return Results.NoContent();
            })
            .WithName("RevokeSession")
            .WithSummary("Ends one session, immediately.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);
    }
}
