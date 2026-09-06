using Logaffe.Api.Hosting;
using Logaffe.Application.Operations;
using Logaffe.Domain.Identities;
using Microsoft.AspNetCore.RateLimiting;

namespace Logaffe.Api.Http;

/// <summary>
/// What somebody gives to get in: an address, a password and one second factor.
/// </summary>
/// <remarks>
/// All of it in one request, including the code. Asking for the password first
/// and the code on a second screen would read better and is refused for what it
/// would give away — the installation would be answering <em>that password was
/// right</em> (<c>docs/sign-in.md</c>).
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
    string? Email, string? Password, string? SecondFactorCode, string? BackupCode);

/// <summary>
/// The bootstrap token from the configuration, and the password it is exchanged
/// for.
/// </summary>
/// <remarks>
/// The one act besides the sign-in that needs no session, and it works only
/// while the first administrator has no password — which is what makes it
/// single-use without anything being stored (ADR 0054).
/// </remarks>
public sealed record BootstrapExchangeRequest(string? Token, string? Password);

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
/// One of a user's signed-in browsers, as they see it in their own list.
/// </summary>
/// <remarks>
/// It carries no secret and nothing that could be presented: a session is
/// admitted by the value in the cookie, and the row holds only a fast hash of it
/// (ADR 0057).
/// </remarks>
/// <param name="LastSeenFrom">
/// The address it last acted from, or <c>unknown</c> where there was none to
/// read. This column is how somebody notices a session that is not theirs.
/// </param>
/// <param name="LastUsedAt">
/// When it last acted, accurate to within five minutes (ADR 0033) and not to be
/// shown as though it were finer.
/// </param>
/// <param name="IsCurrent">
/// Whether this is the browser asking. The server says so because nothing else
/// can: the list carries no secret and the cookie carries nothing but one, so
/// there is nothing the interface could compare — and without it "end all
/// others" is a guess and revoking a row signs somebody out of the screen they
/// are on.
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
/// what a person acts on, and one they ended has to be gone from it rather than
/// greyed out (<c>docs/sign-in.md</c>). It takes effect on the next request,
/// because the session authentication reads the row every time and holds no
/// cache in front of it. <b>Nobody sees anybody else's list</b>, not even an
/// administrator (ADR 0055).
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
                    request.Email,
                    request.Password,
                    request.SecondFactorCode,
                    request.BackupCode,
                    context.SeenFrom(),
                    cancellationToken);

                // One refusal for every way of not getting in: an address
                // nobody holds, a wrong password, a wrong code, a code already
                // spent, an account that was invited and never arrived, and one
                // that has been deactivated. The screen says one thing, and
                // which of them it was is not something this surface hands over
                // — in wording or in how long it took (ADR 0056).
                if (signedIn is null)
                {
                    return Results.Unauthorized();
                }

                SessionCookie.Issue(context.Response, signedIn.Secret.Text);

                return Results.Ok(new SignInResponse(signedIn.BackupCodesRemaining));
            })
            .WithName("SignIn")
            .WithSummary("Starts a session for the user behind an address and a password.")
            .RequireRateLimiting(PublicRateLimits.SignIn)
            .AllowAnonymous()
            .Produces<SignInResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests);

        endpoints.MapPost("/bootstrap", async (
                BootstrapExchangeRequest request,
                ExchangeBootstrapToken exchange,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                var exchanged = await exchange.ExecuteAsync(
                    request.Token, request.Password, context.SeenFrom(), cancellationToken);

                if (exchanged.Outcome is not ExchangeOutcome.Exchanged)
                {
                    // Said by field, unlike the sign-in. There is little to
                    // protect and much to lose by being unhelpful: whoever is
                    // here is setting up their own installation with the compose
                    // file open in another window (ADR 0054).
                    return exchanged.Outcome switch
                    {
                        ExchangeOutcome.PasswordNotOne => NotRight("password", APasswordIs),
                        ExchangeOutcome.TokenRefused => NotRight(
                            "token",
                            "That is not the bootstrap token this installation's "
                            + "configuration names."),
                        _ => Results.Conflict(),
                    };
                }

                SessionCookie.Issue(context.Response, exchanged.Secret!.Text);

                return Results.NoContent();
            })
            .WithName("ExchangeBootstrapToken")
            .WithSummary(
                "Exchanges the bootstrap token for the first administrator's password and a "
                + "session.")
            .RequireRateLimiting(PublicRateLimits.SignIn)
            .AllowAnonymous()
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        endpoints.MapPost("/sign-out", async (
                SignOut signOut,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                await signOut.ExecuteAsync(context.CurrentSession(), cancellationToken);
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

    private static string APasswordIs =>
        $"A password is at least {Password.MinimumLength} and at most "
        + $"{Password.MaximumLength} characters.";

    /// <summary>
    /// Which field the request was refused over, said by name — on the one
    /// anonymous act where saying so costs nothing.
    /// </summary>
    private static IResult NotRight(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static void MapTheList(this IEndpointRouteBuilder endpoints)
    {
        var account = endpoints
            .MapGroup(string.Empty)
            .RequireAuthorization()
            .RequireRateLimiting(PublicRateLimits.Operator);

        account.MapGet("/sessions", async (
                ListSessions list, HttpContext context, CancellationToken cancellationToken) =>
            {
                var current = context.CurrentSession();
                var held = await list.ExecuteAsync(current.UserId, cancellationToken);

                return Results.Ok(held.Select(session => new ListedSessionResponse(
                    session.Id,
                    session.LastSeenFrom,
                    session.StartedAt,
                    session.LastUsedAt,
                    session.ExpiresAt,
                    session.Id == current.Id)));
            })
            .WithName("ListSessions")
            .WithSummary("The signed-in user's own browsers.")
            .Produces<IEnumerable<ListedSessionResponse>>();

        account.MapDelete("/sessions/others", async (
                EndEveryOtherSession endOthers,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                // Every other of this user's, never every one and never anybody
                // else's: the browser doing this stays signed in, or securing
                // the account would sign somebody out of the screen they secured
                // it from.
                await endOthers.ExecuteAsync(context.CurrentSession(), cancellationToken);

                return Results.NoContent();
            })
            .WithName("EndEveryOtherSession")
            .WithSummary("Ends every session but this one.")
            .Produces(StatusCodes.Status204NoContent);

        account.MapDelete("/sessions/{id:guid}", async (
                Guid id,
                RevokeSession revoke,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                // Somebody else's session id answers exactly as a session id
                // that never existed does: the act looks in the caller's own
                // list, so there is nothing here that says whether the row is
                // out there under another name.
                if (!await revoke.ExecuteAsync(
                    context.CurrentSession().UserId, id, cancellationToken))
                {
                    // Already gone: a second click, another tab, or a sweep.
                    return Results.NotFound();
                }

                // Ending your own from the list is a sign-out by another name,
                // and the cookie has to go with it — otherwise the browser keeps
                // presenting a secret whose row is not there any more.
                if (id == context.CurrentSession().Id)
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
