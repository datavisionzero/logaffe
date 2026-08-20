using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Logaffe.Api.Http;
using Logaffe.Domain.Entries;
using Microsoft.AspNetCore.RateLimiting;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Logaffe.Api.Mcp;

/// <summary>
/// The second adapter over the read use cases: five tools at <c>/mcp</c>, behind
/// a reading agent token.
/// </summary>
/// <remarks>
/// <para>
/// <c>VISION.md</c> puts agent access on equal footing with the web UI, and this
/// is the door it comes through. The protocol itself — the handshake, the
/// version negotiation, the transport — is the SDK's; what is written here is
/// which tools exist, what they are called and what they answer with.
/// </para>
/// <para>
/// <b>Tools and nothing else.</b> No resources and no prompts: a log store
/// answers parameterized questions, and exposing projects as readable resources
/// would be a second way to ask the same thing with its own caching and its own
/// surface. Nothing is registered here that writes, and nothing reaches a
/// project or a token (ADR 0018).
/// </para>
/// <para>
/// <b>One endpoint, and the tool list is the token's.</b> Both kinds of agent
/// token arrive at <c>/mcp</c> — an MCP client is handed the tools its
/// credential earns, so how many servers an operator wires up is decided by how
/// many tokens they hold rather than by how many addresses exist. The five below
/// are the reading token's; the administering token's surface is not built yet,
/// and until it is, what that token is offered is an empty list (ADR 0046).
/// </para>
/// </remarks>
public static class AgentSurface
{
    public static IServiceCollection AddLogaffeAgentTools(this IServiceCollection services)
    {
        services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = "logaffe",
                    Version = Version,
                };

                // No server instructions. The tool descriptions are the only
                // prose this adapter sends, and a paragraph addressed to the
                // agent about how to read logs would be a second voice beside
                // them saying something nobody had to keep true.
            })
            .WithHttpTransport(transport =>

                // Every call stands alone (`docs/mcp.md`): the installation
                // remembers nothing about what an agent asked before, so there
                // is no session for it to be remembered in.
                transport.Stateless = true)

            // Named one by one rather than swept out of the assembly. There are
            // five tools and there are five here, and adding a sixth is a line
            // in this file rather than a side effect of writing a method
            // somewhere (ADR 0018).
            .WithTools(
                [typeof(ProjectTools), typeof(EntryTools), typeof(HostTools)],
                AgentJson.Options)

            // What makes the `[Authorize]` on those three do anything: the tool
            // list a client is handed is filtered by what the presented token
            // earns, and a tool it does not earn is missing from the list rather
            // than present and refusing. Today that is one list and an empty
            // one — an administering token authenticates here and is offered
            // nothing at all until the surface it earns exists (ADR 0046).
            .AddAuthorizationFilters();

        return services;
    }

    public static IEndpointRouteBuilder MapAgentTools(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapMcp(AgentClientConfiguration.McpPath)

            // Named rather than left to the default, which is the operator's
            // session. A cookie admits nothing here and a token admits nothing
            // there, which is what `docs/mcp.md` means by no second, weaker
            // door.
            .RequireAuthorization(policy => policy
                .AddAuthenticationSchemes(AgentAuthentication.Scheme)
                .RequireAuthenticatedUser())
            .RequireRateLimiting(PublicRateLimits.Agent)

            // It is publicly reachable but it is not the HTTP contract. That
            // document describes what the operator's browser and a sender talk
            // to; the shape of the tools is in the tool list an agent asks for.
            .ExcludeFromDescription();

        // The stream a client opens when it expects the server to speak first.
        // There is nothing to say on it — nothing here is delivered without a
        // call — and a server that does not offer one answers 405. Without this
        // the single-page application's fallback would take the request and
        // hand an agent the operator's web page with a 200 on it, which is the
        // one answer a client cannot make sense of.
        endpoints
            .MapGet(
                AgentClientConfiguration.McpPath,
                () => Results.StatusCode(StatusCodes.Status405MethodNotAllowed))
            .RequireRateLimiting(PublicRateLimits.Agent)
            .ExcludeFromDescription();

        return endpoints;
    }

    /// <summary>
    /// What the installation calls itself in the handshake, so that an operator
    /// reading their agent's logs can tell which build answered.
    /// </summary>
    private static readonly string Version =
        typeof(AgentSurface).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";
}

/// <summary>
/// How the five tools are written on the wire.
/// </summary>
/// <remarks>
/// <para>
/// One difference from the SDK's own: a field carrying nothing is left out
/// rather than written as <c>null</c>. That is what makes the compact shape
/// compact — it exists to keep a broad search from spending an agent's whole
/// context, and seven null fields on each of two hundred entries would spend a
/// good part of what it saved.
/// </para>
/// <para>
/// <b>What that costs the answers is that a field an answer may leave out cannot
/// be required by the schema that declares it.</b> A client validates the
/// structured content of a result against the tool's output schema and throws
/// away an answer that does not match, so a project in no group, an uncapped
/// search with no cursor to hand back, or a count that came back with what to
/// narrow would each be lost — and lost as a client-side failure, with the
/// server having answered correctly. The answers in <c>AgentAnswers</c>
/// therefore say which of their fields are always there, one field at a time,
/// with <c>required</c>. Positional records cannot: every parameter of one is
/// required, including the nullable ones, and the description saying a field is
/// absent sometimes is prose the schema does not read.
/// </para>
/// </remarks>
internal static class AgentJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        // The levels, the groupings, the buckets and the narrowings are closed
        // sets, and their names are what the tool schemas offer and what the
        // answers carry. A number would make an agent guess at a mapping.
        options.Converters.Insert(0, new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        // Except the level, which keeps the spelling it is declared with, ahead
        // of the rule above. An entry answers `"level": "Fatal"` and a count
        // groups under `"Fatal"`, so a schema offering `fatal` to filter by
        // would be one closed set in two spellings — and taking a level out of
        // one answer to narrow the next call is most of the traffic here.
        options.Converters.Insert(0, new JsonStringEnumConverter<Level>());

        options.MakeReadOnly();

        return options;
    }
}
