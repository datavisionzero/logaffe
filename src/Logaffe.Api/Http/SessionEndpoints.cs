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
/// The two ends of a session.
/// </summary>
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

        return endpoints;
    }
}
