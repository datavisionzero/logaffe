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
/// Whether a code will be asked for at the next sign-in.
/// </summary>
/// <param name="EnrolledAt">
/// When the second factor became the current one, and <c>null</c> when there is
/// none.
/// </param>
public sealed record SecondFactorResponse(bool IsEnrolled, DateTimeOffset? EnrolledAt);

/// <summary>
/// What the operator needs in hand before an enrolment can be confirmed, shown
/// once.
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
/// The same material sealed under the installation's key. The enrolment will not
/// complete without it, and there is nothing in it the operator can read
/// (ADR 0036).
/// </param>
public sealed record EnrolmentResponse(
    string SecondFactorSecret,
    string EnrolmentUri,
    IReadOnlyList<string> BackupCodes,
    string Ticket);

/// <summary>
/// Everything the last step of an enrolment needs, in one request.
/// </summary>
/// <param name="SecondFactorCode">
/// Six digits from the authenticator in use, or left out when
/// <paramref name="BackupCode"/> is given instead — which is the case of the
/// phone that is already gone — or when there is no second factor in use at all.
/// </param>
/// <param name="NewSecondFactorCode">
/// Six digits from the authenticator just enrolled, which is what proves it
/// holds the secret in the ticket.
/// </param>
/// <param name="Ticket">
/// The sealed enrolment the previous step handed over. There is nothing in it
/// the operator can read (ADR 0036).
/// </param>
public sealed record EnrolSecondFactorRequest(
    string? Password,
    string? SecondFactorCode,
    string? BackupCode,
    string? NewSecondFactorCode,
    string? Ticket);

/// <summary>
/// The credentials that removing the second factor costs, which are the ones
/// enrolling it costs.
/// </summary>
public sealed record TurnOffSecondFactorRequest(
    string? Password, string? SecondFactorCode, string? BackupCode);

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
/// <b>The second factor is enrolled, replaced and removed here, and nowhere
/// else</b> (ADR 0041). Removing it costs exactly what enrolling it costs, so a
/// taken session cannot strip the account down to a password — and the state
/// itself is readable, because an installation running without one has to be able
/// to say so.
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
                var sheet = await issue.ExecuteAsync(request.Password, cancellationToken);

                // The one response body in the product that carries ten
                // credentials at once, which is why the request log records no
                // body anywhere (ADR 0002).
                return sheet.Outcome switch
                {
                    SheetOutcome.PasswordRefused => NotRight(
                        "password", "That is not your password."),
                    SheetOutcome.NoSecondFactor => NoSecondFactor(),
                    _ => Results.Ok(new BackupCodesResponse(
                        [.. sheet.Codes.Select(code => code.Display)])),
                };
            })
            .WithName("IssueBackupCodes")
            .WithSummary("Replaces the backup codes with a fresh set, shown once.")
            .Produces<BackupCodesResponse>()
            .Produces(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        operatorSurface.MapGet("/second-factor", async (
                CheckTheSecondFactor check, CancellationToken cancellationToken) =>
            {
                var state = await check.ExecuteAsync(cancellationToken);

                return state is null
                    ? Results.Unauthorized()
                    : Results.Ok(new SecondFactorResponse(state.IsEnrolled, state.EnrolledAt));
            })
            .WithName("CheckSecondFactor")
            .WithSummary("Whether the operator has a second factor, and since when.")
            .Produces<SecondFactorResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        operatorSurface.MapPost("/second-factor/enrolment", async (
                BeginEnrolment begin, HttpContext context, CancellationToken cancellationToken) =>
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
            .WithName("BeginEnrolment")
            .WithSummary("Draws a second factor and a sheet of backup codes, and stores neither.")
            .Produces<EnrolmentResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        operatorSurface.MapPut("/second-factor", async (
                EnrolSecondFactorRequest request,
                EnrolTheSecondFactor enrol,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                var outcome = await enrol.ExecuteAsync(
                    request.Password,
                    request.SecondFactorCode,
                    request.BackupCode,
                    request.NewSecondFactorCode,
                    request.Ticket,
                    context.OperatorSession(),
                    cancellationToken);

                return RefusedBy(outcome);
            })
            .WithName("EnrolSecondFactor")
            .WithSummary(
                "Enrols or replaces the second factor, issues fresh backup codes, and ends "
                + "every other session.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        // A removal rather than a `DELETE`, because this act carries credentials
        // and a body on a `DELETE` is the one thing on the way between a browser
        // and an installation that nothing guarantees survives.
        operatorSurface.MapPost("/second-factor/removal", async (
                TurnOffSecondFactorRequest request,
                TurnOffTheSecondFactor turnOff,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                var outcome = await turnOff.ExecuteAsync(
                    request.Password,
                    request.SecondFactorCode,
                    request.BackupCode,
                    context.OperatorSession(),
                    cancellationToken);

                return outcome switch
                {
                    TurningOffOutcome.PasswordRefused => NotRight(
                        "password", "That is not your password."),
                    TurningOffOutcome.SecondFactorRefused => NotRight(
                        "secondFactorCode", NeitherACodeNorABackupCode),
                    TurningOffOutcome.NoSecondFactor => NoSecondFactor(),
                    _ => Results.NoContent(),
                };
            })
            .WithName("TurnOffSecondFactor")
            .WithSummary(
                "Removes the second factor and its backup codes, and ends every other "
                + "session.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        return endpoints;
    }

    private static string APasswordIs =>
        $"A password is at least {Password.MinimumLength} and at most "
        + $"{Password.MaximumLength} characters.";

    private static string NeitherACodeNorABackupCode =>
        "That is neither a code your current authenticator produces now nor an "
        + "unspent backup code.";

    /// <summary>
    /// Which step refused, said by field. There is nobody here but the operator
    /// — they proved it with the session — and somebody enrolling with a phone in
    /// one hand has to know which of the credentials did not take.
    /// </summary>
    private static IResult RefusedBy(EnrolmentOutcome outcome) => outcome switch
    {
        EnrolmentOutcome.PasswordRefused => NotRight("password", "That is not your password."),
        EnrolmentOutcome.SecondFactorRefused => NotRight(
            "secondFactorCode", NeitherACodeNorABackupCode),
        EnrolmentOutcome.EnrolmentNotOurs => NotRight(
            "ticket",
            "This enrolment is not one this installation handed out, or it was drawn too "
            + "long ago. Start again."),
        EnrolmentOutcome.NewSecondFactorRefused => NotRight(
            "newSecondFactorCode",
            "That is not a code the authenticator you just enrolled produces now. Check "
            + "that the app scanned this installation's code and that the phone's clock "
            + "is right."),
        _ => Results.NoContent(),
    };

    /// <summary>
    /// A conflict with what the account holds rather than anything wrong with the
    /// request: there is no second factor, so there is nothing to replace the
    /// codes for and nothing to turn off. The screen does not offer either act in
    /// that state.
    /// </summary>
    private static IResult NoSecondFactor() => Results.Conflict();

    /// <inheritdoc cref="ClaimEndpoints"/>
    private static IResult NotRight(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
