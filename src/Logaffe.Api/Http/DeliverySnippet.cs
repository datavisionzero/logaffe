using Logaffe.Domain.Tokens;

namespace Logaffe.Api.Http;

/// <summary>
/// The finished delivery an ingest token is handed over with: this
/// installation's address, this token, and one entry in the format the endpoint
/// reads.
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/setup.md</c> and <c>docs/ui.md</c> both promise this, and they
/// promise it for the same reason <c>docs/mcp.md</c> promises the agent's
/// configuration rather than the bare token: assembling one out of an address, a
/// header name, a content type and a line of JSON is the fiddliest part of a
/// first delivery and the part most likely to be got wrong in a way that reports
/// nothing useful. <c>VISION.md</c> makes this path the adoption barrier.
/// </para>
/// <para>
/// It is assembled here rather than in the act that issues the token, for the
/// one reason that keeps it out of the layer below: the installation's own
/// address is something only an adapter knows, and it knows it from the request
/// it is answering. Behind a reverse proxy that is the forwarded address, which
/// is why <see cref="Hosting.RequestSource"/> reads the host and the scheme
/// along with the caller.
/// </para>
/// <para>
/// <b>It names no logaffe package.</b> The three client packages
/// (<c>docs/codebase.md</c>) are neither written nor published, and a snippet
/// whose first line is a package nobody can install is worse than no snippet at
/// all. So the first version is the plain path <c>docs/ingestion.md</c> already
/// says everything works over — a request, a header and a line — which needs
/// nothing installed, works from any language, and answers exactly what an
/// operator staring at an empty project is asking. The Serilog form arrives with
/// the package it needs.
/// </para>
/// </remarks>
public static class DeliverySnippet
{
    /// <summary>
    /// Where a delivery arrives. It is written down here because it goes into
    /// the configuration of every application that ever delivers, so it is a
    /// promise to everything already sending rather than a route that can be
    /// moved — the same standing <see cref="AgentClientConfiguration.McpPath"/>
    /// has.
    /// </summary>
    public const string IngestPath = "/ingest";

    /// <summary>
    /// Newline-delimited JSON, one CLEF object per entry
    /// (<c>docs/adr/0004-the-ingestion-format-is-clef-and-the-server-renders.md</c>).
    /// </summary>
    public const string ContentType = "application/x-ndjson";

    /// <summary>
    /// What the operator pastes into a terminal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The entry it sends is the smallest one the format allows and still shows
    /// what the format is: <c>@t</c>, which is required, and a message template
    /// with one property in it, so that what comes back rendered demonstrates
    /// where rendering happens. There is no <c>@l</c>, because an absent level
    /// means <c>Information</c> and leaving it out is the affordance
    /// <c>docs/ingestion.md</c> keeps the <c>curl</c> case short with.
    /// </para>
    /// <para>
    /// <b>The timestamp is generated when the line is sent, not when the token
    /// was issued.</b> A snippet carrying a fixed <c>@t</c> would deliver an
    /// entry dated whenever the operator happened to open the settings, and the
    /// UI orders by <c>@t</c> — so the one place this snippet is shown, a
    /// project with no entries, would stay looking empty afterwards, which is
    /// precisely the wrong conclusion <c>docs/ui.md</c> asks that view not to
    /// invite. The cost is that this is a POSIX shell line.
    /// </para>
    /// </remarks>
    public static string For(HttpRequest request, TokenText token) =>
        $$"""
          curl -X POST {{request.Scheme}}://{{request.Host}}{{IngestPath}} \
            -H "Authorization: Bearer {{token.Text}}" \
            -H "Content-Type: {{ContentType}}" \
            --data-binary "{\"@t\":\"$(date -u +%FT%TZ)\",\"@mt\":\"Hello from {Sender}\",\"Sender\":\"curl\"}"
          """;
}
