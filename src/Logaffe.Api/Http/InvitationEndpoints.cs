using Logaffe.Application.Operations;
using Logaffe.Domain.Identities;
using Microsoft.AspNetCore.RateLimiting;

namespace Logaffe.Api.Http;

/// <summary>Who is being invited, and whether they administer.</summary>
public sealed record InviteRequest(string? Name, string? Email, bool Administrator);

/// <summary>The address a link goes to, on the one form a stranger can send.</summary>
public sealed record RecoveryRequest(string? Email);

/// <summary>A link, and the password it is being exchanged for.</summary>
public sealed record RedeemRequest(string? Secret, string? Password);

/// <summary>The password again, and the address being moved to.</summary>
public sealed record ChangeAddressRequest(string? Password, string? Email);

/// <summary>
/// The three links: an invitation, a password recovery and a change of address
/// (ADR 0053).
/// </summary>
/// <remarks>
/// <para>
/// <b>Two of these are public and two are behind a session.</b> Asking for a
/// recovery and redeeming any of the three are reachable by anyone — a person
/// redeeming an invitation has no account yet, and one recovering a password
/// cannot sign in — so they carry the sign-in throttle. Inviting and starting a
/// change of address are not.
/// </para>
/// <para>
/// <b>Asking for a recovery answers the same thing whatever happens.</b> An
/// address nobody holds, one belonging to somebody who never arrived, and one
/// whose account has been deactivated all get <c>204</c>: saying otherwise would
/// turn a public form into a way of asking who is here.
/// </para>
/// </remarks>
public static class InvitationEndpoints
{
    public static IEndpointRouteBuilder MapInvitations(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/users", async (
                InviteRequest request,
                InviteAUser invite,
                CancellationToken cancellationToken) =>
                await invite.ExecuteAsync(
                    request.Name, request.Email, request.Administrator, cancellationToken)
                switch
                {
                    InviteOutcome.Invited => Results.NoContent(),
                    InviteOutcome.AddressTaken => Results.Conflict(),
                    InviteOutcome.NoMail => NoMail(),
                    InviteOutcome.NotDelivered => NotDelivered(),
                    _ => NotRight("email", "That is not an email address."),
                })
            .WithName("InviteUser")
            .WithSummary("Invites somebody, who begins with an account and no project access.")
            .RequireAuthorization(SessionAuthentication.AdministratorPolicy)
            .RequireRateLimiting(PublicRateLimits.Operator)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status503ServiceUnavailable)
            .ProducesValidationProblem();

        endpoints.MapPost("/users/{id:guid}/invitation", async (
                Guid id,
                ReinviteAUser reinvite,
                CancellationToken cancellationToken) =>
                await reinvite.ExecuteAsync(id, cancellationToken) switch
                {
                    InviteOutcome.Invited => Results.NoContent(),
                    InviteOutcome.NoMail => NoMail(),
                    InviteOutcome.NotDelivered => NotDelivered(),
                    _ => Results.NotFound(),
                })
            .WithName("ReinviteUser")
            .WithSummary("Sends a fresh invitation, and stops the previous link working.")
            .RequireAuthorization(SessionAuthentication.AdministratorPolicy)
            .RequireRateLimiting(PublicRateLimits.Operator)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        endpoints.MapPost("/recovery", async (
                RecoveryRequest request,
                BeginRecovery begin,
                CancellationToken cancellationToken) =>
            {
                await begin.ExecuteAsync(request.Email, cancellationToken);

                // The same answer whatever happened, including nothing.
                return Results.NoContent();
            })
            .WithName("BeginRecovery")
            .WithSummary("Sends a link to that address, if there is an account behind it.")
            .RequireRateLimiting(PublicRateLimits.SignIn)
            .AllowAnonymous()
            .Produces(StatusCodes.Status204NoContent);

        endpoints.MapPost("/invitation/redemption", Redeeming(
                OneTimeSecretPurpose.Invitation))
            .WithName("RedeemInvitation")
            .WithSummary("Sets a first password, which is what an invitation is for.")
            .RequireRateLimiting(PublicRateLimits.SignIn)
            .AllowAnonymous()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        endpoints.MapPost("/recovery/redemption", Redeeming(
                OneTimeSecretPurpose.PasswordRecovery))
            .WithName("RedeemRecovery")
            .WithSummary("Sets a new password, and ends every session that account had.")
            .RequireRateLimiting(PublicRateLimits.SignIn)
            .AllowAnonymous()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        endpoints.MapPost("/address/redemption", Redeeming(
                OneTimeSecretPurpose.EmailChange))
            .WithName("RedeemAddressChange")
            .WithSummary("Confirms the new address, which is when it starts signing in.")
            .RequireRateLimiting(PublicRateLimits.SignIn)
            .AllowAnonymous()
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        endpoints.MapPost("/address", async (
                ChangeAddressRequest request,
                ChangeAddress change,
                HttpContext context,
                CancellationToken cancellationToken) =>
                await change.ExecuteAsync(
                    context.CurrentUser(), request.Password, request.Email, cancellationToken)
                switch
                {
                    ChangeAddressOutcome.Sent => Results.NoContent(),
                    ChangeAddressOutcome.AddressTaken => Results.Conflict(),
                    ChangeAddressOutcome.PasswordRefused => NotRight(
                        "password", "That is not your password."),
                    ChangeAddressOutcome.NoMail => NoMail(),
                    ChangeAddressOutcome.NotDelivered => NotDelivered(),
                    _ => NotRight("email", "That is not an email address."),
                })
            .WithName("ChangeAddress")
            .WithSummary("Sends a link to the new address; nothing changes until it is used.")
            .RequireAuthorization()
            .RequireRateLimiting(PublicRateLimits.Operator)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status503ServiceUnavailable)
            .ProducesValidationProblem();

        return endpoints;
    }

    /// <summary>
    /// The three redemptions, which differ in a purpose and in nothing else.
    /// </summary>
    private static Func<RedeemRequest, RedeemALink, CancellationToken, Task<IResult>> Redeeming(
        OneTimeSecretPurpose purpose) =>
        async (request, redeem, cancellationToken) =>
            await redeem.ExecuteAsync(
                purpose, request.Secret, request.Password, cancellationToken) switch
            {
                RedeemOutcome.Redeemed => Results.NoContent(),
                RedeemOutcome.AddressTaken => Results.Conflict(),
                RedeemOutcome.PasswordNotOne => NotRight("password", APasswordIs),
                _ => NotRight(
                    "secret",
                    "That link does not open anything. It may have been used already, or "
                    + "expired, or been replaced by a newer one."),
            };

    private static string APasswordIs =>
        $"A password is at least {Password.MinimumLength} and at most "
        + $"{Password.MaximumLength} characters.";

    /// <summary>
    /// This installation cannot send. It is the installation's state rather than
    /// anything wrong with the request, and the interface does not offer these
    /// acts when it holds (ADR 0053).
    /// </summary>
    private static IResult NoMail() => Results.Problem(
        "This installation has no SMTP configured, so it cannot send a link. See "
        + "docs/setup.md.",
        statusCode: StatusCodes.Status503ServiceUnavailable);

    /// <summary>
    /// The message did not go out. Said as its own answer because what happened
    /// is real and partly done: nothing is queued, and asking again issues a
    /// fresh link.
    /// </summary>
    private static IResult NotDelivered() => Results.Problem(
        "The message could not be delivered. Nothing was queued — what the mail server "
        + "said is in logaffe's own log, and asking again sends a fresh link.",
        statusCode: StatusCodes.Status503ServiceUnavailable);

    /// <inheritdoc cref="AccountEndpoints"/>
    private static IResult NotRight(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
