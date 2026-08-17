using Logaffe.Api.Hosting;
using Logaffe.Application.Operations;
using Logaffe.Domain.Operators;
using Microsoft.AspNetCore.RateLimiting;

namespace Logaffe.Api.Http;

/// <summary>
/// Whether this installation belongs to anybody, which is what decides the
/// first screen the single-page application shows.
/// </summary>
/// <param name="CanBeClaimed">
/// Whether a claim would be considered at all — false once the installation has
/// an operator, and in window mode once the window has closed, which is the
/// screen that names the host command.
/// </param>
/// <param name="NeedsSecret">
/// Whether the screen has to ask for the claim secret, which it does on every
/// installation guarded by one (ADR 0040).
/// </param>
/// <param name="ClosesAt">
/// When the window shuts, so that the screen can count down to it, and
/// <c>null</c> when there is nothing to count down to — which is every
/// installation guarded by a secret.
/// </param>
public sealed record ClaimStateResponse(
    bool IsClaimed, bool CanBeClaimed, bool NeedsSecret, DateTimeOffset? ClosesAt);

/// <summary>
/// The whole claim, which is one request (ADR 0014).
/// </summary>
/// <param name="Secret">
/// The claim secret, on an installation guarded by one, read out of the file the
/// installation wrote it to or out of the compose file that set it. Left out in
/// window mode, where there is none to present.
/// </param>
public sealed record ClaimRequest(string? Password, string? Secret);

/// <summary>
/// The whole reachable surface of an installation nobody owns.
/// </summary>
/// <remarks>
/// <para>
/// There is no ingestion, no MCP, nothing to read and nothing to configure until
/// somebody claims it: ingestion needs a token, a token needs a project, and a
/// project needs an operator (<c>docs/setup.md</c>). These two routes are it.
/// </para>
/// <para>
/// <b>What stands in front of them is whichever guard the installation was
/// brought up with</b> (ADR 0040): a claim secret, which is the default and has
/// no deadline, or an open window of thirty minutes. They are anonymous either
/// way — there is no account yet to authenticate against — and the throttle is
/// on both.
/// </para>
/// <para>
/// <b>The claim establishes a password and nothing else</b> (ADR 0041). The
/// second factor is enrolled afterwards, from the settings, by an operator who
/// has one to enrol; the routes for it are <see cref="OperatorEndpoints"/>.
/// </para>
/// <para>
/// <b>Every refusal says which step failed</b>, which is the opposite of the
/// sign-in's one answer for everything. The person on the other end is setting up
/// their own installation, and what a refusal says about the secret is only ever
/// whether the one presented was right.
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
                    state.IsClaimed, state.CanBeClaimed, state.NeedsSecret, state.ClosesAt));
            })
            .WithName("CheckTheClaim")
            .WithSummary("Whether this installation has an operator, and how it can be claimed.")
            .RequireRateLimiting(PublicRateLimits.ClaimState)
            .AllowAnonymous()
            .Produces<ClaimStateResponse>()
            .Produces(StatusCodes.Status429TooManyRequests);

        endpoints.MapPost("/claim", async (
                ClaimRequest request,
                ClaimTheInstallation claim,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                var attempt = await claim.ExecuteAsync(
                    request.Password,
                    request.Secret,
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

    private static IResult RefusedBy(ClaimOutcome outcome) => outcome switch
    {
        ClaimOutcome.AlreadyClaimed => AlreadyClaimed(),
        ClaimOutcome.WindowClosed or ClaimOutcome.NoSecretToPresentTo => Shut(),
        ClaimOutcome.SecretRefused => NotRight(
            "secret",
            "That is not this installation's claim secret. It is in "
            + "`claim-secret.txt` on the host volume, or it is the one the compose "
            + "file names."),
        ClaimOutcome.PasswordNotOne => NotRight(
            "password",
            $"A password is at least {Password.MinimumLength} and at most "
            + $"{Password.MaximumLength} characters."),
        _ => Results.NoContent(),
    };

    /// <summary>
    /// A conflict with what the installation holds rather than anything wrong
    /// with the request: it has an operator, and there is no re-claim while
    /// claimed. The only route back is the host (ADR 0013).
    /// </summary>
    private static IResult AlreadyClaimed() => Results.Conflict();

    /// <summary>
    /// The window is up, or there is no secret to present to. Either way no
    /// request will ever be right again until the host opens the way in — which
    /// is a refusal to act rather than a conflict.
    /// </summary>
    private static IResult Shut() => Results.StatusCode(StatusCodes.Status403Forbidden);

    /// <summary>
    /// Named by field, because this is a form the operator is filling in and
    /// they have to be told which box is wrong.
    /// </summary>
    private static IResult NotRight(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
