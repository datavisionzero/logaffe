using Logaffe.Api.Hosting;
using Logaffe.Application.Operations;
using Logaffe.Domain.Operators;
using Microsoft.AspNetCore.RateLimiting;

namespace Logaffe.Api.Http;

/// <summary>
/// Whether this installation belongs to anybody, which is what decides the
/// first screen the single-page application shows.
/// </summary>
/// <param name="ClosesAt">
/// When the window shuts, so that the screen can count down to it, and
/// <c>null</c> when there is nothing to count down to.
/// </param>
public sealed record ClaimStateResponse(
    bool IsClaimed, bool WindowIsOpen, DateTimeOffset? ClosesAt);

/// <summary>
/// What the claimant needs in hand before they can finish, shown once.
/// </summary>
/// <param name="SecondFactorSecret">
/// The secret in text, under the QR code, for anyone typing it into an app by
/// hand.
/// </param>
/// <param name="EnrolmentUri">The <c>otpauth:</c> address the QR code carries.</param>
/// <param name="BackupCodes">
/// Ten codes, grouped for the sheet the operator prints. This is the only time
/// they exist anywhere but in the operator's hands (ADR 0032).
/// </param>
/// <param name="Ticket">
/// The same material sealed under the installation's key. The claim will not
/// complete without it, and there is nothing in it the claimant can read
/// (ADR 0035).
/// </param>
public sealed record EnrolmentResponse(
    string SecondFactorSecret,
    string EnrolmentUri,
    IReadOnlyList<string> BackupCodes,
    string Ticket);

/// <summary>
/// The last step, which is the only one that stores anything (ADR 0014).
/// </summary>
/// <param name="SecondFactorCode">
/// Six digits out of the authenticator that was just enrolled, which is what
/// proves the enrolment took.
/// </param>
/// <param name="BackupCode">
/// One of the ten, typed back off the sheet — spacing, grouping and capitals are
/// all forgiven.
/// </param>
public sealed record ClaimRequest(
    string? Password, string? Ticket, string? SecondFactorCode, string? BackupCode);

/// <summary>
/// The whole reachable surface of an installation nobody owns.
/// </summary>
/// <remarks>
/// <para>
/// There is no ingestion, no MCP, nothing to read and nothing to configure until
/// somebody claims it: ingestion needs a token, a token needs a project, and a
/// project needs an operator (<c>docs/setup.md</c>). These three routes are it.
/// </para>
/// <para>
/// <b>Anyone who can reach an unclaimed installation may claim it.</b> There is
/// no setup secret to fetch first — that is settled in <c>VISION.md</c> — and
/// what keeps the exposure bounded is the thirty-minute window and the fact that
/// there is nothing here to take. So these are anonymous by design rather than
/// by omission, and what stands in front of them is the throttle.
/// </para>
/// <para>
/// <b>Every refusal says which step failed</b>, which is the opposite of the
/// sign-in's one answer for everything. There is nothing to give away here: the
/// door is open on purpose, and the person on the other end is setting up their
/// own installation.
/// </para>
/// </remarks>
public static class ClaimEndpoints
{
    public static IEndpointRouteBuilder MapClaim(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/claim", async (
                CheckTheClaim check, CancellationToken cancellationToken) =>
            {
                var state = await check.ExecuteAsync(cancellationToken);

                return Results.Ok(new ClaimStateResponse(
                    state.IsClaimed, state.WindowIsOpen, state.ClosesAt));
            })
            .WithName("CheckTheClaim")
            .WithSummary("Whether this installation has an operator, and whether it can be claimed.")
            .RequireRateLimiting(PublicRateLimits.ClaimState)
            .AllowAnonymous()
            .Produces<ClaimStateResponse>()
            .Produces(StatusCodes.Status429TooManyRequests);

        endpoints.MapPost("/claim/enrolment", async (
                BeginEnrolment begin, HttpContext context, CancellationToken cancellationToken) =>
            {
                // The name an authenticator app will show in its list is the
                // address the operator reached this installation by, which only
                // an adapter knows — and behind a reverse proxy it is the
                // forwarded one.
                var begun = await begin.ExecuteAsync(
                    context.Request.Host.Value ?? "logaffe", cancellationToken);

                if (begun.Enrolment is null)
                {
                    return RefusedBy(begun.State);
                }

                return Results.Ok(new EnrolmentResponse(
                    begun.Enrolment.SecondFactorSecret,
                    begun.Enrolment.EnrolmentUri,
                    [.. begun.Enrolment.BackupCodes.Select(code => code.Display)],
                    begun.Enrolment.Ticket));
            })
            .WithName("BeginEnrolment")
            .WithSummary("Draws a second factor and a sheet of backup codes, and stores neither.")
            .RequireRateLimiting(PublicRateLimits.Claim)
            .AllowAnonymous()
            .Produces<EnrolmentResponse>()
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status429TooManyRequests);

        endpoints.MapPost("/claim", async (
                ClaimRequest request,
                ClaimTheInstallation claim,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                var attempt = await claim.ExecuteAsync(
                    request.Password,
                    request.Ticket,
                    request.SecondFactorCode,
                    request.BackupCode,
                    context.SeenFrom(),
                    cancellationToken);

                if (attempt.Outcome is not ClaimOutcome.Claimed)
                {
                    return RefusedBy(attempt.Outcome);
                }

                // The claim signs them in, because the alternative is a screen
                // that congratulates somebody and then asks them for the
                // password they chose four seconds ago.
                SessionCookie.Issue(context.Response, attempt.Secret!.Text);

                // Nothing to say and nowhere to point: the account has no
                // address of its own, and what the operator wants next is the
                // installation.
                return Results.NoContent();
            })
            .WithName("Claim")
            .WithSummary("Gives this installation an operator, and signs them in.")
            .RequireRateLimiting(PublicRateLimits.Claim)
            .AllowAnonymous()
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status429TooManyRequests)
            .ProducesValidationProblem();

        return endpoints;
    }

    /// <summary>
    /// The two ways an installation refuses to be enrolled against at all,
    /// which are two different screens.
    /// </summary>
    private static IResult RefusedBy(ClaimState state) =>
        state.IsClaimed ? AlreadyClaimed() : WindowClosed();

    private static IResult RefusedBy(ClaimOutcome outcome) => outcome switch
    {
        ClaimOutcome.AlreadyClaimed => AlreadyClaimed(),
        ClaimOutcome.WindowClosed => WindowClosed(),
        ClaimOutcome.PasswordNotOne => NotRight(
            "password",
            $"A password is between {Password.MinimumLength} and "
            + $"{Password.MaximumLength} characters."),
        ClaimOutcome.EnrolmentNotOurs => NotRight(
            "ticket",
            "This enrolment is not one this installation handed out, or it belongs to a "
            + "claim window that has since been replaced. Start again."),
        ClaimOutcome.SecondFactorRefused => NotRight(
            "secondFactorCode",
            "That is not a code the authenticator you just enrolled produces now. Check "
            + "that the app scanned this installation's code and that the phone's clock "
            + "is right."),
        ClaimOutcome.BackupCodeRefused => NotRight(
            "backupCode", "That is not one of the backup codes you were just shown."),
        _ => Results.NoContent(),
    };

    /// <summary>
    /// A conflict with what the installation holds rather than anything wrong
    /// with the request: it has an operator, and there is no re-claim while
    /// claimed. The only route back is the host (ADR 0013).
    /// </summary>
    private static IResult AlreadyClaimed() => Results.Conflict();

    /// <summary>
    /// The thirty minutes are up, and no request will ever be right again until
    /// the host arms a fresh window — which is a refusal to act rather than a
    /// conflict.
    /// </summary>
    private static IResult WindowClosed() => Results.StatusCode(StatusCodes.Status403Forbidden);

    /// <summary>
    /// Named by field, because this is a form the operator is filling in and
    /// they have to be told which box is wrong.
    /// </summary>
    private static IResult NotRight(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
