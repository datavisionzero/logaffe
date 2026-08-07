using Logaffe.Application.Operations;
using Logaffe.Domain.Operators;
using Microsoft.AspNetCore.RateLimiting;

namespace Logaffe.Api.Http;

/// <summary>
/// A new password, and the current one to prove it is the operator choosing it.
/// </summary>
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

/// <summary>
/// The password again, for the one act that hands over ten ways past the second
/// factor.
/// </summary>
public sealed record IssueBackupCodesRequest(string? Password);

/// <summary>
/// A sheet of backup codes, shown once and kept by nobody afterwards
/// (ADR 0032).
/// </summary>
/// <param name="Codes">
/// Ten codes, grouped for the sheet the operator prints. They replace whatever
/// set was there, spent codes and unspent ones alike.
/// </param>
public sealed record BackupCodesResponse(IReadOnlyList<string> Codes);

/// <summary>
/// Everything the last step of a re-enrolment needs, in one request.
/// </summary>
/// <param name="SecondFactorCode">
/// Six digits from the authenticator in use, or left out when
/// <paramref name="BackupCode"/> is given instead — which is the case of the
/// phone that is already gone.
/// </param>
/// <param name="NewSecondFactorCode">
/// Six digits from the authenticator just enrolled, which is what proves it
/// holds the secret in the ticket.
/// </param>
/// <param name="Ticket">
/// The sealed enrolment the previous step handed over. There is nothing in it
/// the operator can read (ADR 0035).
/// </param>
public sealed record ReEnrolSecondFactorRequest(
    string? Password,
    string? SecondFactorCode,
    string? BackupCode,
    string? NewSecondFactorCode,
    string? Ticket);

/// <summary>
/// The operator's own credentials: the password, the second factor and the
/// backup codes.
/// </summary>
/// <remarks>
/// <para>
/// All of it is behind the session and under the operator's rate limit, and none
/// of it is reachable over MCP — as an absence from that interface rather than
/// as a permission (ADR 0018). It is not behind the sign-in throttle: that
/// throttle exists because the sign-in is the one place a password can be
/// guessed at by anyone who can reach the installation, and these routes are
/// already behind a session that guessing cannot produce.
/// </para>
/// <para>
/// <b>Every one of them requires the password again</b>, which is what makes
/// them the operator's acts rather than those of whoever is sitting at an
/// unlocked browser — and what makes an act that ends every other session worth
/// reaching for after a cookie has gone somewhere it should not have.
/// </para>
/// <para>
/// <b>The second factor cannot be turned off, only replaced</b> (ADR 0016).
/// There is no route here that removes one, and a re-enrolment is a replacement
/// that leaves no moment in between.
/// </para>
/// </remarks>
public static class OperatorEndpoints
{
    public static IEndpointRouteBuilder MapOperator(this IEndpointRouteBuilder endpoints)
    {
        var operatorSurface = endpoints
            .MapGroup(string.Empty)
            .RequireAuthorization()
            .RequireRateLimiting(PublicRateLimits.Operator);

        operatorSurface.MapPut("/password", async (
                ChangePasswordRequest request,
                ChangePassword change,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                var outcome = await change.ExecuteAsync(
                    request.CurrentPassword,
                    request.NewPassword,
                    context.OperatorSession(),
                    cancellationToken);

                return outcome switch
                {
                    PasswordChangeOutcome.CurrentPasswordRefused => NotRight(
                        "currentPassword", "That is not your current password."),
                    PasswordChangeOutcome.ChosenPasswordNotOne => NotRight(
                        "newPassword", APasswordIs),
                    _ => Results.NoContent(),
                };
            })
            .WithName("ChangePassword")
            .WithSummary("Takes a new password, and ends every other session.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        operatorSurface.MapPost("/backup-codes", async (
                IssueBackupCodesRequest request,
                IssueBackupCodes issue,
                CancellationToken cancellationToken) =>
            {
                var codes = await issue.ExecuteAsync(request.Password, cancellationToken);

                // The one response body in the product that carries ten
                // credentials at once, which is why the request log records no
                // body anywhere (ADR 0002).
                return codes is null
                    ? NotRight("password", "That is not your password.")
                    : Results.Ok(new BackupCodesResponse(
                        [.. codes.Select(code => code.Display)]));
            })
            .WithName("IssueBackupCodes")
            .WithSummary("Replaces the backup codes with a fresh set, shown once.")
            .Produces<BackupCodesResponse>()
            .ProducesValidationProblem();

        operatorSurface.MapPost("/second-factor/enrolment", async (
                BeginReEnrolment begin, HttpContext context, CancellationToken cancellationToken) =>
            {
                // The name an authenticator app will show in its list is the
                // address the operator reached this installation by, which only
                // an adapter knows — and behind a reverse proxy it is the
                // forwarded one.
                var enrolment = await begin.ExecuteAsync(
                    context.Request.Host.Value ?? "logaffe", cancellationToken);

                // No account behind a live session is Host Recovery a moment
                // ago. The session it left behind admits nothing either, so the
                // next request is a sign-in screen.
                return enrolment is null
                    ? Results.Unauthorized()
                    : Results.Ok(new EnrolmentResponse(
                        enrolment.SecondFactorSecret,
                        enrolment.EnrolmentUri,
                        [.. enrolment.BackupCodes.Select(code => code.Display)],
                        enrolment.Ticket));
            })
            .WithName("BeginReEnrolment")
            .WithSummary("Draws a second factor and a sheet of backup codes, and stores neither.")
            .Produces<EnrolmentResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        operatorSurface.MapPut("/second-factor", async (
                ReEnrolSecondFactorRequest request,
                ReEnrolTheSecondFactor reEnrol,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                var outcome = await reEnrol.ExecuteAsync(
                    request.Password,
                    request.SecondFactorCode,
                    request.BackupCode,
                    request.NewSecondFactorCode,
                    request.Ticket,
                    context.OperatorSession(),
                    cancellationToken);

                return RefusedBy(outcome);
            })
            .WithName("ReEnrolSecondFactor")
            .WithSummary(
                "Replaces the second factor, issues fresh backup codes, and ends every "
                + "other session.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        return endpoints;
    }

    private static string APasswordIs =>
        $"A password is between {Password.MinimumLength} and "
        + $"{Password.MaximumLength} characters.";

    /// <summary>
    /// Which step refused, said by field. There is nobody here but the operator
    /// — they proved it with the session — and somebody replacing their second
    /// factor with a phone in one hand has to know which of the three
    /// credentials did not take.
    /// </summary>
    private static IResult RefusedBy(ReEnrolmentOutcome outcome) => outcome switch
    {
        ReEnrolmentOutcome.PasswordRefused => NotRight("password", "That is not your password."),
        ReEnrolmentOutcome.SecondFactorRefused => NotRight(
            "secondFactorCode",
            "That is neither a code your current authenticator produces now nor an "
            + "unspent backup code."),
        ReEnrolmentOutcome.EnrolmentNotOurs => NotRight(
            "ticket",
            "This enrolment is not one this installation handed out, or it was drawn too "
            + "long ago. Start again."),
        ReEnrolmentOutcome.NewSecondFactorRefused => NotRight(
            "newSecondFactorCode",
            "That is not a code the authenticator you just enrolled produces now. Check "
            + "that the app scanned this installation's code and that the phone's clock "
            + "is right."),
        _ => Results.NoContent(),
    };

    /// <inheritdoc cref="ClaimEndpoints"/>
    private static IResult NotRight(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
