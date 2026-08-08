using System.Security.Claims;
using System.Text.Encodings.Web;
using Logaffe.Application.Operations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Logaffe.Api.Mcp;

/// <summary>
/// The agent's door: an agent token in an <c>Authorization</c> header, and
/// <see cref="AuthenticateToken"/> deciding what it admits.
/// </summary>
/// <remarks>
/// <para>
/// It is a scheme of its own beside the operator's session and it is named
/// explicitly by the one endpoint that uses it, which is what makes
/// <c>docs/mcp.md</c>'s <i>no second, weaker door</i> structural: a session
/// cookie admits nothing here, and an agent token admits nothing on the
/// operator's surface, because neither endpoint asks the other's scheme.
/// </para>
/// <para>
/// There is nothing to carry out of a successful authentication. An agent token
/// reads every project and writes nothing, so that it was admitted at all is the
/// whole of its permission (ADR 0021) — the principal below holds no claim
/// because there is no fact about the caller that any tool is entitled to
/// branch on.
/// </para>
/// </remarks>
public static class AgentAuthentication
{
    /// <summary>What the MCP endpoint names when it asks to be behind the door.</summary>
    public const string Scheme = "Agent";

    public static IServiceCollection AddLogaffeAgentAuthentication(
        this IServiceCollection services)
    {
        // No default scheme is named here on purpose: the operator's session is
        // the default and stays it, so an endpoint that asks for nothing in
        // particular is still asking for the session.
        services
            .AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, AgentAuthenticationHandler>(Scheme, null);

        return services;
    }
}

/// <inheritdoc cref="AgentAuthentication"/>
public sealed class AgentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var presented = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(presented))
        {
            // Not a failure: a client that has not been configured with a token
            // yet sends nothing, and the challenge below is what tells it so.
            return AuthenticateResult.NoResult();
        }

        var admitted = await Context.RequestServices
            .GetRequiredService<AuthenticateToken>()
            .AdmitsReadAsync(presented, Context.RequestAborted);

        if (!admitted)
        {
            // Revoked, never issued, or an ingest token pasted into an agent
            // configuration. Which of the three it was is not said here and is
            // not knowable from the answer (ADR 0031).
            return AuthenticateResult.Fail("The presented token admits no read.");
        }

        return AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(Scheme.Name)), Scheme.Name));
    }

    /// <remarks>
    /// A status code and no body, and deliberately no <c>WWW-Authenticate</c>
    /// pointing at protected-resource metadata. The token is one the operator
    /// pasted into a configuration file by hand (<c>docs/mcp.md</c>); advertising
    /// an authorization server would send a client looking for a door this
    /// installation does not have.
    /// </remarks>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }
}
