using Logaffe.Domain.Tokens;

namespace Logaffe.Application.Ports;

/// <summary>
/// The token rows an installation holds, found by the identifier a presented
/// token carries.
/// </summary>
/// <remarks>
/// <para>
/// One lookup on a unique index and nothing else: a token names its own row, so
/// how many tokens an installation holds is not a question the ingest path asks
/// (ADR 0031). The two kinds are two methods rather than one with a kind
/// parameter, because they are two tables and the prefix has already said which
/// one is meant before anything gets here.
/// </para>
/// <para>
/// Recording a use is separate and deliberate. The rule about which uses are
/// worth writing lives in <see cref="Operations.AuthenticateToken"/> beside the
/// reason for it (ADR 0033); what is left here is the writing itself.
/// </para>
/// </remarks>
public interface ITokens
{
    Task<IngestToken?> FindIngestTokenAsync(
        TokenIdentifier identifier, CancellationToken cancellationToken);

    Task<AgentToken?> FindAgentTokenAsync(
        TokenIdentifier identifier, CancellationToken cancellationToken);

    /// <summary>
    /// Writes back the use just recorded on <paramref name="token"/>.
    /// </summary>
    Task RecordUseAsync(IngestToken token, CancellationToken cancellationToken);

    /// <inheritdoc cref="RecordUseAsync(IngestToken, CancellationToken)"/>
    Task RecordUseAsync(AgentToken token, CancellationToken cancellationToken);
}
