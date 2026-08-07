using System.Text.Json;
using Logaffe.Domain.Tokens;

namespace Logaffe.Api.Http;

/// <summary>
/// The finished client configuration an agent token is handed over in: the
/// address and the token already in place.
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/mcp.md</c> promises the configuration rather than the bare token,
/// because assembling one by hand out of an address, a header name and a string
/// is the fiddliest part of connecting an agent and the part most likely to be
/// got wrong in a way that reports nothing useful. It is the same move the
/// first-run guide makes for the Serilog sink.
/// </para>
/// <para>
/// It is assembled here rather than in the act that issues the token, for the
/// one reason that keeps it out of the layer below: the installation's own
/// address is something only an adapter knows, and it knows it from the request
/// it is answering. Behind a reverse proxy that is the forwarded address, which
/// is why <see cref="Hosting.RequestSource"/> reads the host and the scheme
/// along with the caller.
/// </para>
/// </remarks>
public static class AgentClientConfiguration
{
    /// <summary>
    /// Where the four MCP tools answer. It is written down here because it is
    /// what goes into every agent's configuration, so it is a promise to
    /// everything already connected rather than a route that can be moved.
    /// </summary>
    public const string McpPath = "/mcp";

    /// <summary>
    /// What the operator pastes into their agent's configuration file.
    /// </summary>
    /// <remarks>
    /// Indented, because it is read by a person before it is read by anything
    /// else.
    /// </remarks>
    public static string For(HttpRequest request, TokenText token) =>
        JsonSerializer.Serialize(
            new
            {
                mcpServers = new
                {
                    logaffe = new
                    {
                        type = "http",
                        url = $"{request.Scheme}://{request.Host}{McpPath}",
                        headers = new { Authorization = $"Bearer {token.Text}" },
                    },
                },
            },
            new JsonSerializerOptions { WriteIndented = true });
}
