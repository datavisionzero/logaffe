using System.Security.Claims;
using System.Text.Encodings.Web;
using Logaffe.Application.Operations;
using Logaffe.Domain.Tokens;
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
/// <b>Both kinds of agent token arrive here</b>, and what a successful
/// authentication carries out is which of the two this one is. That is the whole
/// of what the tools branch on: a reading token is offered the five tools and no
/// setting, an administering token the settings and no entry, and the two lists
/// do not meet (ADR 0046). It is a claim rather than something read again inside
/// a call, because the row was already fetched to verify the secret and asking
/// twice would be a second lookup on every call an agent makes.
/// </para>
/// </remarks>
public static class AgentAuthentication
{
    /// <summary>What the MCP endpoint names when it asks to be behind the door.</summary>
    public const string Scheme = "Agent";

    /// <summary>
    /// Which kind of agent token was presented, spelled as
    /// <see cref="AgentTokenKind"/> names it.
    /// </summary>
    public const string KindClaim = "logaffe:agent-kind";

    /// <summary>
    /// Present, and only present, on an administering token issued to destroy.
    /// Nothing is written when the flag is off: an absent claim and a claim
    /// saying <c>false</c> are one fact, and one of them is harder to get wrong.
    /// </summary>
    public const string MayDestroyClaim = "logaffe:may-destroy";

    /// <summary>
    /// What the five reading tools ask for. A token that is not a reading one is
    /// not offered them at all — the tool is absent from the list rather than
    /// present and refusing, which is what makes the split legible to the agent
    /// holding the token.
    /// </summary>
    public const string ReadingPolicy = "logaffe:agent-reads";

    /// <summary>
    /// What the seventeen tools every administering token earns ask for, and
    /// what a reading token is refused. It is the mirror of
    /// <see cref="ReadingPolicy"/> and not a superset of it: neither kind is
    /// offered the other's list, so the two never meet on one token (ADR 0046).
    /// </summary>
    public const string AdministeringPolicy = "logaffe:agent-administers";

    /// <summary>
    /// What the four that remove stored data ask for: an administering token
    /// that was issued saying so.
    /// </summary>
    /// <remarks>
    /// It asks for the kind as well as the flag rather than for the flag alone.
    /// Nothing writes <see cref="MayDestroyClaim"/> onto a reading token — the
    /// domain refuses the combination at the moment of issue — but a policy that
    /// only asked for the flag would be one line away from admitting one if that
    /// ever stopped holding, and the sentence this surface rests on is that the
    /// kinds do not meet.
    /// </remarks>
    public const string DestroyingPolicy = "logaffe:agent-destroys";

    public static IServiceCollection AddLogaffeAgentAuthentication(
        this IServiceCollection services)
    {
        // No default scheme is named here on purpose: the operator's session is
        // the default and stays it, so an endpoint that asks for nothing in
        // particular is still asking for the session.
        services
            .AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, AgentAuthenticationHandler>(Scheme, null);

        services
            .AddAuthorizationBuilder()
            .AddPolicy(ReadingPolicy, policy => policy
                .AddAuthenticationSchemes(Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim(KindClaim, nameof(AgentTokenKind.Reading)))
            .AddPolicy(AdministeringPolicy, policy => policy
                .AddAuthenticationSchemes(Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim(KindClaim, nameof(AgentTokenKind.Administering)))
            .AddPolicy(DestroyingPolicy, policy => policy
                .AddAuthenticationSchemes(Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim(KindClaim, nameof(AgentTokenKind.Administering))
                .RequireClaim(MayDestroyClaim, bool.TrueString));

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
            .AdmittedAgentAsync(presented, Context.RequestAborted);

        if (admitted is null)
        {
            // Revoked, never issued, an ingest token pasted into an agent
            // configuration, or a token whose prefix says one kind where its row
            // says the other. Which of them it was is not said here and is not
            // knowable from the answer (ADR 0031).
            return AuthenticateResult.Fail("The presented token admits nothing.");
        }

        var claims = new List<Claim>
        {
            new(AgentAuthentication.KindClaim, admitted.Kind.ToString()),
        };

        if (admitted.MayDestroy)
        {
            claims.Add(new Claim(AgentAuthentication.MayDestroyClaim, bool.TrueString));
        }

        return AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name));
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
