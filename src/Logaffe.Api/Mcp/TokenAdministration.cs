using System.ComponentModel;
using Logaffe.Api.Http;
using Logaffe.Application.Operations;
using Logaffe.Domain.Tokens;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace Logaffe.Api.Mcp;

/// <summary>
/// The four token acts: two credentials issued, and each of them revoked.
/// </summary>
/// <remarks>
/// <para>
/// <b>A token is issued and never read back.</b> Its value reaches an agent at
/// the moment it is created and never again — there is no tool over
/// <c>ReadTokenBack</c> on this surface, not directly and not through the snippet
/// that carries the token inside it. An operator who has lost one reads it back
/// in their browser, where they always could (ADR 0022).
/// </para>
/// <para>
/// <b>Issuing where a token already exists is rotation, and it is allowed
/// outright.</b> The narrow rule — only into something that holds none — does
/// not survive the fact that revoking is not destructive: an agent would revoke
/// the live token, issue a fresh one, and be where the rule said it could not go.
/// Allowing it costs nothing those two steps did not, and it buys the whole
/// cycle: issue the second, hand it over, revoke the first (ADR 0046).
/// </para>
/// <para>
/// <b>Revoking is two tools where the installation has one act that takes any
/// token.</b> The split is the whole point: an agent token cannot be reached from
/// here at all, because there is no tool that names one — absent rather than
/// refused inside a call, which is what "never reachable" has to mean to be worth
/// saying. An agent that could revoke an agent token could revoke every token but
/// its own, and one that could issue one would grant itself the kind and the flag
/// the operator withheld.
/// </para>
/// <para>
/// <b>None of the four is destructive.</b> Revoking stops a sender delivering,
/// and the entries that would have arrived meanwhile never exist — but nothing
/// that is already stored is gone afterwards, and another token closes the gap.
/// What it does buy an agent is a live write credential into a project the
/// operator trusts, which is stated plainly in ADR 0046 rather than softened:
/// what bounds it is that this token reads no entry, so the sentence asking for
/// the credential never enters its context, and that an ingest token is
/// write-only, so nothing is read back out through one.
/// </para>
/// </remarks>
[Authorize(Policy = AgentAuthentication.AdministeringPolicy)]
[McpServerToolType]
public static class TokenAdministration
{
    [McpServerTool(
        Name = "issue_ingest_token",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        """
        Gives a project a token to receive entries on, and hands back the token
        itself together with a finished curl command that delivers with it.

        This is the one moment the value exists to be handed over. Nothing on
        this surface can produce it again — get_settings says a project holds
        tokens and when each was last used, never what they are. Give it to the
        operator now.

        It works on any project, not only one just created. Issuing a second
        alongside a live one is how a rotation is done without a gap: issue,
        hand over, then revoke the old one. A project holds at most two.
        """)]
    public static async Task<IssuedTokenAnswer> IssueIngestAsync(
        IssueIngestToken issue,
        IHttpContextAccessor requests,
        [Description("The project that should be able to receive, as get_settings gives it.")]
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var attempt = await issue.ExecuteAsync(projectId, cancellationToken);

        return attempt.Outcome switch
        {
            IssueOutcome.Issued => IssuedTokenAnswer.Of(
                attempt.Token!,
                DeliverySnippet.For(Asked(requests), attempt.Token!.Token)),
            IssueOutcome.AlreadyHoldsTwo => throw Refused.AlreadyHoldsTwo(
                IngestToken.MaximumPerProject, "revoke_ingest_token"),
            _ => throw Refused.NoSuchProject(projectId),
        };
    }

    [McpServerTool(
        Name = "revoke_ingest_token",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        """
        Ends an ingest token, immediately. A sender still holding it is answered
        401 from its next delivery and, being fire-and-forget, will keep writing
        to its own local files without noticing.

        Nothing already stored is removed by this — the project keeps every entry
        it has — so it is not one of the four acts that destroy data. Make sure a
        replacement has been handed over first, or the project stops receiving.
        """)]
    public static async Task<RevokedAnswer> RevokeIngestAsync(
        RevokeToken revoke,
        [Description("The token to end, as get_settings gives it on the project that holds it.")]
        Guid tokenId,
        CancellationToken cancellationToken = default) =>
        await revoke.IngestTokenAsync(tokenId, cancellationToken)
            ? new RevokedAnswer { Id = tokenId }
            : throw Refused.NoSuchToken(tokenId);

    [McpServerTool(
        Name = "issue_host_token",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        """
        Gives a machine a token its collector reports on, and hands back the
        token together with the finished docker command that starts the
        collector with it.

        This is the one moment the value exists to be handed over; nothing here
        can produce it again. The command still has to be run on the machine
        itself, which is the operator's errand and not an agent's — nothing on
        this surface reaches out to a host.

        A host holds at most two, and a second alongside a live one is a
        rotation.
        """)]
    public static async Task<IssuedTokenAnswer> IssueHostAsync(
        IssueHostToken issue,
        IHttpContextAccessor requests,
        [Description("The machine that should be able to report, as get_settings gives it.")]
        Guid hostId,
        CancellationToken cancellationToken = default)
    {
        var attempt = await issue.ExecuteAsync(hostId, cancellationToken);

        return attempt.Outcome switch
        {
            IssueHostTokenOutcome.Issued => IssuedTokenAnswer.Of(
                attempt.Token!,
                CollectorCommand.For(Asked(requests), attempt.Token!.Token)),
            IssueHostTokenOutcome.AlreadyHoldsTwo => throw Refused.AlreadyHoldsTwo(
                HostToken.MaximumPerHost, "revoke_host_token"),
            _ => throw Refused.NoSuchHost(hostId),
        };
    }

    [McpServerTool(
        Name = "revoke_host_token",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        """
        Ends a host token, immediately. A collector still holding it is answered
        401 from its next reading and stops being able to report.

        The samples it has already delivered stay, so this is not one of the four
        acts that destroy data.
        """)]
    public static async Task<RevokedAnswer> RevokeHostAsync(
        RevokeToken revoke,
        [Description("The token to end, as get_settings gives it on the host that holds it.")]
        Guid tokenId,
        CancellationToken cancellationToken = default) =>
        await revoke.HostTokenAsync(tokenId, cancellationToken)
            ? new RevokedAnswer { Id = tokenId }
            : throw Refused.NoSuchToken(tokenId);

    /// <summary>
    /// The request the tool call arrived on, which is where the address in a
    /// snippet comes from.
    /// </summary>
    /// <remarks>
    /// The snippet says the name the caller reached this installation by, so an
    /// installation behind a reverse proxy hands out the proxy's name rather
    /// than its own container's (<c>docs/operations.md</c>). That is the same
    /// reading the operator's screen makes, from the same helpers — and it is why
    /// this reaches for the HTTP request at all, which is the only thing in this
    /// adapter that does.
    /// </remarks>
    private static HttpRequest Asked(IHttpContextAccessor requests) =>
        requests.HttpContext?.Request
        ?? throw new InvalidOperationException(
            "A tool call arrived without an HTTP request behind it.");
}
