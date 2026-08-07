using Logaffe.Domain.Tokens;

namespace Logaffe.Application.Ports;

/// <summary>
/// The token rows an installation holds, found by the identifier a presented
/// token carries and by the identity the operator's own acts name.
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
/// <para>
/// The operator's acts reach a row by its identity rather than by the identifier
/// in a token's text, because the operator is holding a list and not a
/// credential: revoking a token they have mislaid must not require them to have
/// it. Revoking is <see cref="RemoveAsync(IngestToken, CancellationToken)"/> and
/// nothing else — a revoked token is removed rather than marked, which is
/// <c>docs/projects.md</c>.
/// </para>
/// </remarks>
public interface ITokens
{
    Task<IngestToken?> FindIngestTokenAsync(
        TokenIdentifier identifier, CancellationToken cancellationToken);

    Task<AgentToken?> FindAgentTokenAsync(
        TokenIdentifier identifier, CancellationToken cancellationToken);

    /// <summary>
    /// The row the operator named, or <c>null</c> when there is none — which is
    /// what a token revoked in another browser tab looks like.
    /// </summary>
    Task<IngestToken?> FindIngestTokenAsync(Guid id, CancellationToken cancellationToken);

    /// <inheritdoc cref="FindIngestTokenAsync(Guid, CancellationToken)"/>
    Task<AgentToken?> FindAgentTokenAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// What one project holds — one token, or two while it is being rotated —
    /// oldest first, so that the one being rotated away is the one at the top.
    /// </summary>
    Task<IReadOnlyList<IngestToken>> ListIngestTokensAsync(
        Guid projectId, CancellationToken cancellationToken);

    /// <summary>
    /// Every agent token in the installation, oldest first. There is no project
    /// to scope this by: an agent token reads all of them (ADR 0021).
    /// </summary>
    Task<IReadOnlyList<AgentToken>> ListAgentTokensAsync(CancellationToken cancellationToken);

    Task AddAsync(IngestToken token, CancellationToken cancellationToken);

    /// <inheritdoc cref="AddAsync(IngestToken, CancellationToken)"/>
    Task AddAsync(AgentToken token, CancellationToken cancellationToken);

    Task RemoveAsync(IngestToken token, CancellationToken cancellationToken);

    /// <inheritdoc cref="RemoveAsync(IngestToken, CancellationToken)"/>
    Task RemoveAsync(AgentToken token, CancellationToken cancellationToken);

    /// <summary>
    /// Writes back the name just given to <paramref name="token"/>.
    /// </summary>
    Task RecordRenameAsync(AgentToken token, CancellationToken cancellationToken);

    /// <summary>
    /// Writes back the use just recorded on <paramref name="token"/>.
    /// </summary>
    Task RecordUseAsync(IngestToken token, CancellationToken cancellationToken);

    /// <inheritdoc cref="RecordUseAsync(IngestToken, CancellationToken)"/>
    Task RecordUseAsync(AgentToken token, CancellationToken cancellationToken);
}
