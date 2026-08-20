using Logaffe.Domain.Tokens;

namespace Logaffe.Application.Ports;

/// <summary>
/// One token row as everything but authentication wants it: what names it, the
/// public middle of its text, when it was issued and when it was last used.
/// </summary>
/// <remarks>
/// <para>
/// <b>The sealed half is not in it.</b> Listing tokens is the operator reading
/// their settings, and none of that shows a credential — the one act that
/// unseals one is <see cref="Operations.ReadTokenBack"/>, which asks for a
/// single token by its identity. So the listings read the columns they answer
/// with and leave the sealed secret in the database, whether they are asked for
/// one project or for every project at once.
/// </para>
/// <para>
/// It carries no owner: a listing for one project is already scoped to it, and
/// the one for all of them is keyed by the project.
/// </para>
/// </remarks>
public sealed record HeldToken(
    Guid Id,
    TokenIdentifier Identifier,
    DateTimeOffset IssuedAt,
    DateTimeOffset? LastUsedAt);

/// <summary>
/// The token rows an installation holds, found by the identifier a presented
/// token carries and by the identity the operator's own acts name.
/// </summary>
/// <remarks>
/// <para>
/// One lookup on a unique index and nothing else: a token names its own row, so
/// how many tokens an installation holds is not a question the ingest path asks
/// (ADR 0031). The three kinds are three methods rather than one with a kind
/// parameter, because they are three tables and the prefix has already said
/// which one is meant before anything gets here.
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

    Task<HostToken?> FindHostTokenAsync(
        TokenIdentifier identifier, CancellationToken cancellationToken);

    /// <summary>
    /// The row the operator named, or <c>null</c> when there is none — which is
    /// what a token revoked in another browser tab looks like.
    /// </summary>
    Task<IngestToken?> FindIngestTokenAsync(Guid id, CancellationToken cancellationToken);

    /// <inheritdoc cref="FindIngestTokenAsync(Guid, CancellationToken)"/>
    Task<AgentToken?> FindAgentTokenAsync(Guid id, CancellationToken cancellationToken);

    /// <inheritdoc cref="FindIngestTokenAsync(Guid, CancellationToken)"/>
    Task<HostToken?> FindHostTokenAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// What one project holds — one token, or two while it is being rotated —
    /// oldest first, so that the one being rotated away is the one at the top.
    /// </summary>
    Task<IReadOnlyList<HeldToken>> ListIngestTokensAsync(
        Guid projectId, CancellationToken cancellationToken);

    /// <summary>
    /// What every project holds, keyed by the project, oldest first within each;
    /// projects holding no token are left out.
    /// </summary>
    /// <remarks>
    /// One statement for the whole installation rather than one per project.
    /// Both the lists the operator opens a session on and the settings tree an
    /// administering agent starts at are answered from here, so there stays one
    /// way to learn what tokens a project holds however many projects are being
    /// asked about.
    /// </remarks>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<HeldToken>>> ListIngestTokensAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// What one host holds — one token, or two while it is being rotated —
    /// oldest first, so that the one being rotated away is the one at the top.
    /// </summary>
    Task<IReadOnlyList<HeldToken>> ListHostTokensAsync(
        Guid hostId, CancellationToken cancellationToken);

    /// <summary>
    /// What every host holds, keyed by the host, oldest first within each; hosts
    /// holding no token are left out.
    /// </summary>
    /// <remarks>
    /// One statement for the whole fleet, for the reason the ingest listing is
    /// one: a host holding no token at all is one no collector can report to,
    /// and finding that out should not cost a read per machine.
    /// </remarks>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<HeldToken>>> ListHostTokensAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Every agent token in the installation, oldest first. There is no project
    /// to scope this by: an agent token reads all of them (ADR 0021).
    /// </summary>
    Task<IReadOnlyList<AgentToken>> ListAgentTokensAsync(CancellationToken cancellationToken);

    Task AddAsync(IngestToken token, CancellationToken cancellationToken);

    /// <inheritdoc cref="AddAsync(IngestToken, CancellationToken)"/>
    Task AddAsync(AgentToken token, CancellationToken cancellationToken);

    /// <inheritdoc cref="AddAsync(IngestToken, CancellationToken)"/>
    Task AddAsync(HostToken token, CancellationToken cancellationToken);

    Task RemoveAsync(IngestToken token, CancellationToken cancellationToken);

    /// <inheritdoc cref="RemoveAsync(IngestToken, CancellationToken)"/>
    Task RemoveAsync(AgentToken token, CancellationToken cancellationToken);

    /// <inheritdoc cref="RemoveAsync(IngestToken, CancellationToken)"/>
    Task RemoveAsync(HostToken token, CancellationToken cancellationToken);

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

    /// <inheritdoc cref="RecordUseAsync(IngestToken, CancellationToken)"/>
    Task RecordUseAsync(HostToken token, CancellationToken cancellationToken);
}
