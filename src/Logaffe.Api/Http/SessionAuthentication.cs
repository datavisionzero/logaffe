using System.Security.Claims;
using System.Text.Encodings.Web;
using Logaffe.Api.Hosting;
using Logaffe.Application.Operations;
using Logaffe.Domain.Identities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Logaffe.Api.Http;

/// <summary>
/// The human door: a cookie holding a session secret, and
/// <see cref="AuthenticateSession"/> deciding whose it is.
/// </summary>
/// <remarks>
/// <para>
/// This is the first authenticated surface in the binary and deliberately the
/// narrow kind. There is no sign-in redirect, no return URL and no cookie
/// holding a serialized identity: the single-page application owns the sign-in
/// screen, so a request that is not admitted is answered <c>401</c> and the
/// script decides what to show. Nothing is carried in the cookie except the
/// secret, and the row is what says who it belongs to.
/// </para>
/// <para>
/// The framework's own cookie authentication was the alternative and buys the
/// wrong half: it holds the identity inside the cookie so that a request costs
/// no read, which is exactly what would make ending a session from its owner's
/// list — or deactivating an account — stop being immediate.
/// </para>
/// </remarks>
public static class SessionAuthentication
{
    /// <summary>What the endpoints name when they ask to be behind the door.</summary>
    public const string Scheme = "Session";

    /// <summary>
    /// The one role there is. It says who runs the installation and nothing
    /// about what may be read (ADR 0055).
    /// </summary>
    public const string Administrator = "administrator";

    private const string SessionItemKey = "logaffe.session";

    private const string UserItemKey = "logaffe.user";

    public static IServiceCollection AddLogaffeSessionAuthentication(
        this IServiceCollection services)
    {
        services
            .AddAuthentication(Scheme)
            .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(Scheme, null);

        return services.AddAuthorization();
    }

    /// <summary>
    /// The session this request was admitted by, which the acts that end one or
    /// keep one need in hand.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The request was not admitted by a session. Every caller of this sits
    /// behind the scheme, so reaching it without one is a routing mistake rather
    /// than an unauthenticated request.
    /// </exception>
    public static Session CurrentSession(this HttpContext context) =>
        context.Items[SessionItemKey] as Session
        ?? throw new InvalidOperationException("This request was not admitted by a session.");

    /// <summary>
    /// Whose request this is. Resolved once by the door rather than by every act
    /// behind it, which is what makes a deactivated account stop admitting
    /// anything the moment it is deactivated (ADR 0052).
    /// </summary>
    /// <inheritdoc cref="CurrentSession" path="/exception"/>
    public static User CurrentUser(this HttpContext context) =>
        context.Items[UserItemKey] as User
        ?? throw new InvalidOperationException("This request was not admitted by a session.");

    internal static void SetCurrentIdentity(this HttpContext context, Session session, User user)
    {
        context.Items[SessionItemKey] = session;
        context.Items[UserItemKey] = user;
    }
}

/// <inheritdoc cref="SessionAuthentication"/>
public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var presented = Request.Cookies[SessionCookie.Name];
        if (presented is null)
        {
            // Not a failure — most requests to this installation carry no cookie
            // at all, and the health endpoint is one of them.
            return AuthenticateResult.NoResult();
        }

        var admitted = await Context.RequestServices
            .GetRequiredService<AuthenticateSession>()
            .ExecuteAsync(presented, Context.SeenFrom(), Context.RequestAborted);

        if (admitted is null)
        {
            // A secret naming no live session: signed out elsewhere, revoked
            // from the list, past one of its two deadlines, or belonging to an
            // account that has been deactivated. The browser is told to drop it
            // rather than presenting it on every request until it expires on its
            // own.
            SessionCookie.Clear(Response);

            return AuthenticateResult.Fail("The session secret admits nothing.");
        }

        if (admitted.DeadlineMoved)
        {
            SessionCookie.Issue(Response, presented);
        }

        Context.SetCurrentIdentity(admitted.Session, admitted.User);

        // What identifies the row, and the one role there is. Project access is
        // deliberately not here: it is a set that every read narrows to and not
        // a claim on a principal, and a claim would be a copy of it that could
        // go stale inside one request (ADR 0055).
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, admitted.User.Id.ToString()),
                new Claim(ClaimTypes.Sid, admitted.Session.Id.ToString()),
                new Claim(ClaimTypes.Name, admitted.User.Name),
            ],
            Scheme.Name);

        if (admitted.User.Administrator)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, SessionAuthentication.Administrator));
        }

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    /// <remarks>
    /// A status code and no body. Redirecting to a sign-in page would answer the
    /// script with an HTML document it has no use for, and a
    /// <c>WWW-Authenticate</c> header would put a browser's own password prompt
    /// in front of a product that has its own.
    /// </remarks>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }
}
