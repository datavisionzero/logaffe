using Logaffe.Application.Operations;
using Logaffe.Domain.Tokens;
using Microsoft.AspNetCore.RateLimiting;

namespace Logaffe.Api.Http;

/// <summary>
/// A token as it comes back from being issued.
/// </summary>
/// <remarks>
/// The token itself is here and in no other response: a list carries none, and
/// reading one back is its own request. What makes handing it over affordable is
/// that this is not the only chance to see it (ADR 0022).
/// </remarks>
/// <param name="Id">What revoking or reading this token back names it by.</param>
/// <param name="DeliverySnippet">
/// One delivery to this installation with this token in it, ready to paste —
/// the snippet the first-run guide hands over and the one an empty project
/// shows (<c>docs/setup.md</c>, <c>docs/ui.md</c>).
/// </param>
public sealed record IssuedIngestTokenResponse(
    Guid Id, string Token, string DeliverySnippet, DateTimeOffset IssuedAt);

/// <summary>
/// One of a project's tokens as the operator sees it in a list, carrying no
/// secret and nothing sealed.
/// </summary>
/// <param name="Identifier">
/// The non-secret middle of the token's text, which is how the operator tells
/// the two tokens of a rotation apart — an ingest token has no name.
/// </param>
/// <param name="LastUsedAt">
/// Null until a delivery has presented it, and accurate to within five minutes
/// (ADR 0033). It is what says a rotation is finished: the old token's last use
/// stops moving.
/// </param>
public sealed record ListedIngestTokenResponse(
    Guid Id, string Identifier, DateTimeOffset IssuedAt, DateTimeOffset? LastUsedAt);

/// <summary>A token put together again out of its row and the key.</summary>
/// <remarks>
/// It carries the snippet as well, because reading a token back and being able
/// to use it are one errand — the same reason the agent token carries its
/// configuration on read-back and not only when it was issued.
/// </remarks>
/// <param name="DeliverySnippet">
/// One delivery to this installation with this token in it, ready to paste.
/// </param>
public sealed record ReadIngestTokenResponse(string Token, string DeliverySnippet);

/// <param name="Name">
/// The operator's name for the token, conventionally the client it is being
/// issued for. It is a label for the list and nothing the server acts on.
/// </param>
/// <param name="Kind">
/// Reading or administering, and reading when it is left out — which is what
/// <c>VISION.md</c>'s read-only by default means where it is applied rather than
/// asserted. It is settled here and by nothing afterwards: there is no act that
/// changes what a token is (ADR 0046).
/// </param>
/// <param name="MayDestroy">
/// Whether an administering token may delete a project or a host, or lower a
/// retention window. Off when it is left out, and refused outright on a reading
/// token — which changes nothing at all, so the flag there is not a smaller
/// request but a nonsense one.
/// </param>
public sealed record IssueAgentTokenRequest(
    string? Name, AgentTokenKind? Kind, bool? MayDestroy);

/// <inheritdoc cref="IssueAgentTokenRequest"/>
public sealed record RenameAgentTokenRequest(string? Name);

/// <inheritdoc cref="IssuedIngestTokenResponse"/>
/// <param name="ClientConfiguration">
/// What the operator pastes into their agent, with this token and this
/// installation's address already in it — the one paste <c>docs/mcp.md</c>
/// promises.
/// </param>
public sealed record IssuedAgentTokenResponse(
    Guid Id,
    string Name,
    AgentTokenKind Kind,
    bool MayDestroy,
    string Token,
    string ClientConfiguration,
    DateTimeOffset IssuedAt);

/// <summary>One agent token as the operator sees it in a list.</summary>
/// <param name="Kind">
/// What this token is. The list is where an operator decides what to revoke, and
/// a credential whose powers are not visible there is one that is never revoked
/// for being too strong.
/// </param>
/// <param name="MayDestroy">
/// Whether it may delete a project or a host, or lower a retention window. Never
/// true of a reading token.
/// </param>
/// <param name="LastUsedAt">
/// The load-bearing field of ADR 0021: a token that has not been used in months
/// is one to revoke, and this list is the only place that fact is visible.
/// </param>
public sealed record ListedAgentTokenResponse(
    Guid Id,
    string Name,
    AgentTokenKind Kind,
    bool MayDestroy,
    DateTimeOffset IssuedAt,
    DateTimeOffset? LastUsedAt);

/// <inheritdoc cref="ReadIngestTokenResponse"/>
public sealed record ReadAgentTokenResponse(string Token, string ClientConfiguration);

/// <inheritdoc cref="IssuedIngestTokenResponse"/>
/// <param name="CollectorCommand">
/// The finished command that starts this host's collector, with this
/// installation's address, this token and the two mounts a container needs to
/// see its machine already in it — the one paste <c>docs/metrics.md</c>
/// promises.
/// </param>
public sealed record IssuedHostTokenResponse(
    Guid Id, string Token, string CollectorCommand, DateTimeOffset IssuedAt);

/// <summary>
/// One of a host's tokens as the operator sees it in a list, carrying no secret
/// and nothing sealed.
/// </summary>
/// <param name="LastUsedAt">
/// Null until a collector has presented it, and accurate to within five minutes
/// (ADR 0033). It is what says a rotation is finished — the old token's last use
/// stops moving — and it is not the same fact as when the host last reported,
/// which is read off the newest sample.
/// </param>
public sealed record ListedHostTokenResponse(
    Guid Id, string Identifier, DateTimeOffset IssuedAt, DateTimeOffset? LastUsedAt);

/// <inheritdoc cref="ReadIngestTokenResponse"/>
public sealed record ReadHostTokenResponse(string Token, string CollectorCommand);

/// <summary>
/// The operator's token acts, reached over HTTP.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these is behind the operator's session. The agent token's are
/// reachable from nowhere else at all — not as a permission but as an absence
/// from the other interface, which is what keeps the rest of ADR 0046 coherent:
/// an agent that could issue an agent token would grant itself the kind and the
/// flag the operator withheld, and both would be decoration. A log entry that
/// asks an agent to mint one has to find nothing to call.
/// </para>
/// <para>
/// <b>A token appears in a response body and nowhere else.</b> The request log
/// records the method, the path and the status and never a body, and
/// <see cref="TokenText.ToString"/> is redacted so that one reaching a log line
/// by way of an interpolation carries the part that identifies it and not the
/// part that admits anything. Three of these bodies carry a token a second time,
/// in the middle of a string a person is meant to paste —
/// <see cref="DeliverySnippet"/>,
/// <see cref="AgentClientConfiguration"/> and
/// <see cref="CollectorCommand"/> — which the redaction cannot help with, and
/// which is the whole reason the rule is about bodies rather than about tokens.
/// </para>
/// <para>
/// The three tables are three sets of routes rather than one with a kind in it,
/// for the same reason the store is three methods: they are refused at each
/// other's doors by the prefix, and each belongs somewhere different — an ingest
/// token to a project, a host token to a host, an agent token to the
/// installation (<c>docs/ui.md</c>). The two kinds an agent token comes in do
/// share these routes, because they are one table and one list: what tells them
/// apart is a field the operator sets once, not an address.
/// </para>
/// </remarks>
public static class TokenEndpoints
{
    public static IEndpointRouteBuilder MapTokens(this IEndpointRouteBuilder endpoints)
    {
        var operatorSurface = endpoints
            .MapGroup(string.Empty)
            .RequireAuthorization()
            .RequireRateLimiting(PublicRateLimits.Operator);

        operatorSurface.MapIngestTokens();
        operatorSurface.MapAgentTokens();
        operatorSurface.MapHostTokens();

        return endpoints;
    }

    private static void MapIngestTokens(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/projects/{projectId:guid}/ingest-tokens", async (
                Guid projectId,
                IssueIngestToken issue,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                var attempt = await issue.ExecuteAsync(projectId, cancellationToken);

                // A third is refused rather than queued or rotating the oldest
                // out: two is what moving deployments over one at a time needs,
                // and a third means the operator has lost track of which one
                // they are retiring. They revoke one first, which is immediate —
                // so this is a conflict with what the project holds rather than
                // anything wrong with the request. A project that is not there
                // is 404: nothing about the request is wrong either, and the
                // address is what is gone.
                return attempt.Outcome switch
                {
                    IssueOutcome.NoSuchProject => Results.NotFound(),
                    IssueOutcome.AlreadyHoldsTwo => Results.Conflict(),
                    _ => Results.Created(
                        ReadBackOf("ingest-tokens", attempt.Token!.Id),
                        new IssuedIngestTokenResponse(
                            attempt.Token.Id,
                            attempt.Token.Token.Text,
                            DeliverySnippet.For(context.Request, attempt.Token.Token),
                            attempt.Token.IssuedAt)),
                };
            })
            .WithName("IssueIngestToken")
            .WithSummary("Gives a project a token to receive on.")
            .Produces<IssuedIngestTokenResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        endpoints.MapGet("/projects/{projectId:guid}/ingest-tokens", async (
                Guid projectId,
                ListIngestTokens list,
                CancellationToken cancellationToken) =>
            {
                var held = await list.ExecuteAsync(projectId, cancellationToken);

                // A project that is not there is 404 rather than an empty list:
                // a closed door and a deleted project are two different
                // readings, and one of them is the settings of something gone.
                if (held is null)
                {
                    return Results.NotFound();
                }

                // A list decrypts nothing. Opening the settings of a project is
                // not the same act as reading its credential.
                return Results.Ok(held.Select(token => new ListedIngestTokenResponse(
                    token.Id, token.Identifier.Value, token.IssuedAt, token.LastUsedAt)));
            })
            .WithName("ListIngestTokens")
            .WithSummary("What one project can currently receive on.")
            .Produces<IEnumerable<ListedIngestTokenResponse>>()
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapGet("/ingest-tokens/{id:guid}/token", async (
                Guid id,
                ReadTokenBack read,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                var token = await read.IngestTokenAsync(id, cancellationToken);

                return token is null
                    ? Results.NotFound()
                    : Results.Ok(new ReadIngestTokenResponse(
                        token.Text, DeliverySnippet.For(context.Request, token)));
            })
            .WithName("ReadIngestTokenBack")
            .WithSummary("The token that is in the row, and the delivery to paste it in.")
            .Produces<ReadIngestTokenResponse>()
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapDelete("/ingest-tokens/{id:guid}", async (
                Guid id,
                RevokeToken revoke,
                CancellationToken cancellationToken) =>
                await revoke.IngestTokenAsync(id, cancellationToken)
                    ? Results.NoContent()
                    : Results.NotFound())
            .WithName("RevokeIngestToken")
            .WithSummary("Ends a token, immediately.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static void MapAgentTokens(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/agent-tokens", async (
                IssueAgentTokenRequest request,
                IssueAgentToken issue,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                if (!IsAName(request.Name))
                {
                    return NotAName();
                }

                // Reading unless the operator said otherwise, which is where the
                // default of `VISION.md` is applied rather than asserted.
                var kind = request.Kind ?? AgentTokenKind.Reading;
                var mayDestroy = request.MayDestroy ?? false;

                // Refused rather than ignored. A reading token makes no change
                // of any kind, so a destructive flag on one is not a smaller
                // request — it is a request nobody could have meant, and
                // storing it as a flag that means nothing is how a list stops
                // being readable.
                if (mayDestroy && kind is not AgentTokenKind.Administering)
                {
                    return OnlyAnAdministeringTokenDestroys();
                }

                var issued = await issue.ExecuteAsync(
                    request.Name!, kind, mayDestroy, cancellationToken);

                return Results.Created(
                    ReadBackOf("agent-tokens", issued.Id),
                    new IssuedAgentTokenResponse(
                        issued.Id,
                        request.Name!.Trim(),
                        kind,
                        mayDestroy,
                        issued.Token.Text,
                        AgentClientConfiguration.For(context.Request, issued.Token),
                        issued.IssuedAt));
            })
            .WithName("IssueAgentToken")
            .WithSummary("Gives an agent a token to read or to administer with.")
            .Produces<IssuedAgentTokenResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        endpoints.MapGet("/agent-tokens", async (
                ListAgentTokens list,
                CancellationToken cancellationToken) =>
            {
                var held = await list.ExecuteAsync(cancellationToken);

                return Results.Ok(held.Select(token => new ListedAgentTokenResponse(
                    token.Id,
                    token.Name,
                    token.Kind,
                    token.MayDestroy,
                    token.IssuedAt,
                    token.LastUsedAt)));
            })
            .WithName("ListAgentTokens")
            .WithSummary("Every agent token the installation holds.")
            .Produces<IEnumerable<ListedAgentTokenResponse>>();

        endpoints.MapPatch("/agent-tokens/{id:guid}", async (
                Guid id,
                RenameAgentTokenRequest request,
                RenameAgentToken rename,
                CancellationToken cancellationToken) =>
            {
                if (!IsAName(request.Name))
                {
                    return NotAName();
                }

                // Renaming changes nothing else: the name does not identify the
                // token to the server, so an agent whose token is renamed does
                // not notice and nothing has to be reconnected.
                return await rename.ExecuteAsync(id, request.Name!, cancellationToken)
                    ? Results.NoContent()
                    : Results.NotFound();
            })
            .WithName("RenameAgentToken")
            .WithSummary("Gives an agent token another label.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        endpoints.MapGet("/agent-tokens/{id:guid}/token", async (
                Guid id,
                ReadTokenBack read,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                var token = await read.AgentTokenAsync(id, cancellationToken);

                return token is null
                    ? Results.NotFound()
                    : Results.Ok(new ReadAgentTokenResponse(
                        token.Text, AgentClientConfiguration.For(context.Request, token)));
            })
            .WithName("ReadAgentTokenBack")
            .WithSummary("The token that is in the row, and the configuration to paste it in.")
            .Produces<ReadAgentTokenResponse>()
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapDelete("/agent-tokens/{id:guid}", async (
                Guid id,
                RevokeToken revoke,
                CancellationToken cancellationToken) =>
                await revoke.AgentTokenAsync(id, cancellationToken)
                    ? Results.NoContent()
                    : Results.NotFound())
            .WithName("RevokeAgentToken")
            .WithSummary("Ends a token, immediately.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static void MapHostTokens(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/hosts/{hostId:guid}/host-tokens", async (
                Guid hostId,
                IssueHostToken issue,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                var attempt = await issue.ExecuteAsync(hostId, cancellationToken);

                // The ingest token's answers, pointed at a machine. A third is
                // refused rather than queued: two is what moving a fleet over one
                // machine at a time needs, and a third means the operator has
                // lost track of which one they are retiring.
                return attempt.Outcome switch
                {
                    IssueHostTokenOutcome.NoSuchHost => Results.NotFound(),
                    IssueHostTokenOutcome.AlreadyHoldsTwo => Results.Conflict(),
                    _ => Results.Created(
                        ReadBackOf("host-tokens", attempt.Token!.Id),
                        new IssuedHostTokenResponse(
                            attempt.Token.Id,
                            attempt.Token.Token.Text,
                            CollectorCommand.For(context.Request, attempt.Token.Token),
                            attempt.Token.IssuedAt)),
                };
            })
            .WithName("IssueHostToken")
            .WithSummary("Gives a host a token its collector can report on.")
            .Produces<IssuedHostTokenResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        endpoints.MapGet("/hosts/{hostId:guid}/host-tokens", async (
                Guid hostId,
                ListHostTokens list,
                CancellationToken cancellationToken) =>
            {
                var held = await list.ExecuteAsync(hostId, cancellationToken);

                // A host that is not there is 404 rather than an empty list: a
                // machine nothing can deliver to and a deleted host are two
                // different readings.
                if (held is null)
                {
                    return Results.NotFound();
                }

                // A list decrypts nothing, and here that is worth more than
                // anywhere else: this screen is opened for every machine, and the
                // credential is wanted for one.
                return Results.Ok(held.Select(token => new ListedHostTokenResponse(
                    token.Id, token.Identifier.Value, token.IssuedAt, token.LastUsedAt)));
            })
            .WithName("ListHostTokens")
            .WithSummary("What one host can currently receive samples on.")
            .Produces<IEnumerable<ListedHostTokenResponse>>()
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapGet("/host-tokens/{id:guid}/token", async (
                Guid id,
                ReadTokenBack read,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                var token = await read.HostTokenAsync(id, cancellationToken);

                // This one is read back more than the others are: what the
                // operator wants is rarely the token but the command that starts
                // a collector with it in place, and on a fleet that is the
                // difference between looking a value up and going round every
                // machine (ADR 0022).
                return token is null
                    ? Results.NotFound()
                    : Results.Ok(new ReadHostTokenResponse(
                        token.Text, CollectorCommand.For(context.Request, token)));
            })
            .WithName("ReadHostTokenBack")
            .WithSummary("The token that is in the row, and the command to paste it in.")
            .Produces<ReadHostTokenResponse>()
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapDelete("/host-tokens/{id:guid}", async (
                Guid id,
                RevokeToken revoke,
                CancellationToken cancellationToken) =>
                // A collector still holding it neither retries nor notices: it
                // drops each reading and takes the next one a minute later, so a
                // rotation done carelessly costs a gap in the band and nothing
                // else.
                await revoke.HostTokenAsync(id, cancellationToken)
                    ? Results.NoContent()
                    : Results.NotFound())
            .WithName("RevokeHostToken")
            .WithSummary("Ends a token, immediately.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);
    }

    /// <summary>
    /// Where the operator comes back for the token itself, which is the only
    /// address a token has: it is read back through an act of its own rather
    /// than fetched as a row.
    /// </summary>
    private static string ReadBackOf(string collection, Guid id) => $"/{collection}/{id}/token";

    /// <summary>
    /// The domain refuses a name that is not one as a backstop; a caller taking
    /// it from a person says so first, and this is that.
    /// </summary>
    private static bool IsAName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= AgentToken.NameMaxLength;

    private static IResult OnlyAnAdministeringTokenDestroys() => Results.ValidationProblem(
        new Dictionary<string, string[]>
        {
            ["mayDestroy"] =
            [
                "Only an administering token can be issued to destroy: a reading "
                + "token makes no change of any kind.",
            ],
        });

    private static IResult NotAName() => Results.ValidationProblem(
        new Dictionary<string, string[]>
        {
            ["name"] =
            [
                "An agent token has a name, of at most "
                + $"{AgentToken.NameMaxLength} characters.",
            ],
        });
}
