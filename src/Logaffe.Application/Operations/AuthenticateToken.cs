using Logaffe.Application.Ports;
using Logaffe.Domain.Identities;
using Logaffe.Domain.Tokens;

namespace Logaffe.Application.Operations;

/// <summary>
/// What an admitted agent may ask for: which half of MCP its token earns,
/// whether it may make a change after which stored data is gone, and who it is
/// acting for.
/// </summary>
/// <remarks>
/// <para>
/// The first two are read off the token's row and neither is negotiable in the
/// call — there is no act anywhere that changes either after the token was
/// issued (ADR 0046).
/// </para>
/// <para>
/// The third is the owner, and it is what everything else about the agent is
/// resolved through (ADR 0052): the projects it can reach are that user's, and
/// the installation-wide acts are within reach only while that user is an
/// administrator. It comes back here because asking for it a second time would
/// be a second lookup on every call an agent makes.
/// </para>
/// </remarks>
public sealed record AdmittedAgent(AgentTokenKind Kind, bool MayDestroy, User Owner);

/// <summary>
/// What a presented token admits: for a delivery of entries, the project it goes
/// to; for a delivery of samples, the host they were read off; for an agent,
/// which half of MCP it earns; and for anything else, nothing.
/// </summary>
/// <remarks>
/// <para>
/// This is the first thing every public endpoint does, and the only place any of
/// them learns who is calling. It is one shape for three doors deliberately —
/// ADR 0021 keeps one credential model pointing in four directions, and the
/// prefix is what refuses each at the others' endpoints, before the database is
/// asked anything at all.
/// </para>
/// <para>
/// The path is the one ADR 0031 describes: parse, refuse the wrong kind, look
/// the row up by the identifier the token carries, decrypt that one row's secret
/// and compare the halves in constant time. What is not on the path matters as
/// much — nothing here tells a token that never existed apart from one that was
/// revoked, and nothing it returns says which it was.
/// </para>
/// </remarks>
public sealed class AuthenticateToken(
    ITokens tokens,
    IIdentities identities,
    ISecretCipher cipher,
    DummySecret dummy,
    TimeProvider clock)
{
    /// <summary>
    /// How stale a token's stored last use may be before another use writes it
    /// again (ADR 0033). A product value, the same in every installation.
    /// </summary>
    public static readonly TimeSpan UseWriteInterval = TimeSpan.FromMinutes(5);

    private const string Scheme = "Bearer";

    /// <summary>
    /// The project a delivery presenting <paramref name="authorization"/> is
    /// admitted to, or <c>null</c> when it is admitted to none — which is the
    /// <c>401</c> of <c>docs/ingestion.md</c> and says nothing further.
    /// </summary>
    public async Task<Guid?> AdmittedProjectAsync(
        string? authorization, CancellationToken cancellationToken)
    {
        if (!TryReadPresented(authorization, TokenKind.Ingest, out var presented))
        {
            return null;
        }

        var token = await tokens.FindIngestTokenAsync(presented.Identifier, cancellationToken);
        if (token is null)
        {
            RefuseAtTheSamePrice(presented);
            return null;
        }

        if (!Matches(presented, token.EncryptedSecret))
        {
            return null;
        }

        var now = clock.GetUtcNow();
        if (IsWorthWriting(token.LastUsedAt, now))
        {
            token.WasUsedAt(now);
            await tokens.RecordUseAsync(token, cancellationToken);
        }

        return token.ProjectId;
    }

    /// <summary>
    /// The host a delivery of samples presenting <paramref name="authorization"/>
    /// is admitted to, or <c>null</c> when it is admitted to none — the same
    /// silent <c>401</c> a delivery of entries gets.
    /// </summary>
    public async Task<Guid?> AdmittedHostAsync(
        string? authorization, CancellationToken cancellationToken)
    {
        if (!TryReadPresented(authorization, TokenKind.Host, out var presented))
        {
            return null;
        }

        var token = await tokens.FindHostTokenAsync(presented.Identifier, cancellationToken);
        if (token is null)
        {
            RefuseAtTheSamePrice(presented);
            return null;
        }

        if (!Matches(presented, token.EncryptedSecret))
        {
            return null;
        }

        var now = clock.GetUtcNow();
        if (IsWorthWriting(token.LastUsedAt, now))
        {
            token.WasUsedAt(now);
            await tokens.RecordUseAsync(token, cancellationToken);
        }

        return token.HostId;
    }

    /// <summary>
    /// What an agent presenting <paramref name="authorization"/> is admitted to
    /// at <c>/mcp</c>, or <c>null</c> when it is admitted to nothing — the same
    /// silent refusal the two deliveries get.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The kind comes back with it because it is what the adapter above hands
    /// out a tool list from, and asking for it a second time would be a second
    /// lookup on every call an agent makes (ADR 0046).
    /// </para>
    /// <para>
    /// <b>So does the owner, and a token whose owner is not active admits
    /// nothing.</b> That is the whole of how deactivating somebody silences
    /// their agents and reactivating them brings back whatever was not revoked
    /// one at a time: there is no second state to set and nothing to remember to
    /// undo (ADR 0052).
    /// </para>
    /// </remarks>
    public async Task<AdmittedAgent?> AdmittedAgentAsync(
        string? authorization, CancellationToken cancellationToken)
    {
        if (!TryReadPresented(authorization, out var presented)
            || !AgentTokenKinds.TryFromTokenKind(presented.Kind, out var presentedKind))
        {
            return null;
        }

        var token = await tokens.FindAgentTokenAsync(presented.Identifier, cancellationToken);
        if (token is null)
        {
            RefuseAtTheSamePrice(presented);
            return null;
        }

        if (!Matches(presented, token.EncryptedSecret))
        {
            return null;
        }

        // The two agent kinds share a table, so the prefix alone cannot settle
        // which one this is: it is written by whoever presents the token, and a
        // reading token with the administering prefix put over it carries a
        // secret that still matches its row. The row is what says what the token
        // is, and the kinds do not meet — which is the sentence the whole of ADR
        // 0046 rests on. It is asked after the comparison rather than before, so
        // that a rewritten prefix costs exactly what a good token costs.
        if (token.Kind != presentedKind)
        {
            return null;
        }

        // The agent, and then the person it acts for. An agent whose owner has
        // been deactivated admits nothing, and neither does one whose identity
        // is gone — which is Host Recovery a moment ago, since nothing else
        // removes one. The rows are left where they are: silencing an agent is
        // the act that deactivated its owner, and this path is a read.
        var owner = await OwnerAsync(token, cancellationToken);
        if (owner is null || !owner.IsActive)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        if (IsWorthWriting(token.LastUsedAt, now))
        {
            token.WasUsedAt(now);
            await tokens.RecordUseAsync(token, cancellationToken);
        }

        return new AdmittedAgent(token.Kind, token.MayDestroy, owner);
    }

    /// <summary>
    /// The user an agent acts for, through the agent identity its token names.
    /// </summary>
    private async Task<User?> OwnerAsync(AgentToken token, CancellationToken cancellationToken)
    {
        var agent = await identities.FindAsync(token.IdentityId, cancellationToken) as Agent;

        return agent is null
            ? null
            : await identities.FindUserAsync(agent.OwnerId, cancellationToken);
    }

    /// <summary>
    /// Whether the presented secret is the one the row holds: one decryption,
    /// and a comparison that takes the same time however much of the secret was
    /// right.
    /// </summary>
    /// <remarks>
    /// A row this cipher cannot open throws, and that is deliberately not caught
    /// here. It is a corrupt row or a lost key — an installation-level fault
    /// rather than an answer about a presented token — and the startup check is
    /// what catches the second. Turning either into a <c>401</c> would hide
    /// both.
    /// </remarks>
    private bool Matches(TokenText presented, byte[] encryptedSecret) =>
        presented.SecretMatches(cipher.Decrypt(encryptedSecret));

    /// <summary>
    /// Pays for a lookup that found nothing what a lookup that found something
    /// would have cost.
    /// </summary>
    /// <remarks>
    /// ADR 0031 requires an identifier matching no row and a secret that
    /// mismatches to cost the same, so that the <c>401</c> stays as silent about
    /// which it was as it is about everything else. Returning early here is
    /// precisely what would tell the two apart, so the decryption and the
    /// comparison happen anyway, against a value belonging to no token, and the
    /// answer is thrown away.
    /// </remarks>
    private void RefuseAtTheSamePrice(TokenText presented) =>
        _ = Matches(presented, dummy.Sealed);

    /// <inheritdoc cref="UseWriteInterval"/>
    private static bool IsWorthWriting(DateTimeOffset? lastUsedAt, DateTimeOffset now) =>
        lastUsedAt is null || now - lastUsedAt.Value >= UseWriteInterval;

    /// <summary>
    /// Reads the token out of an <c>Authorization</c> header value, and refuses
    /// there and then anything that is not a token of <paramref name="kind"/> —
    /// the wrong scheme, the wrong prefix, the wrong shape, a character outside
    /// the alphabet. None of that reaches the database.
    /// </summary>
    private static bool TryReadPresented(
        string? authorization, TokenKind kind, out TokenText presented) =>
        TryReadPresented(authorization, out presented) && presented.Kind == kind;

    /// <inheritdoc cref="TryReadPresented(string?, TokenKind, out TokenText)"/>
    /// <remarks>
    /// Without a kind, for the one door two kinds arrive at: <c>/mcp</c> answers
    /// a reading token and an administering one, and which of them this is is
    /// the caller's to read off <see cref="TokenText.Kind"/>.
    /// </remarks>
    private static bool TryReadPresented(string? authorization, out TokenText presented)
    {
        presented = null!;

        var value = authorization?.Trim();

        // The scheme is case-insensitive by RFC 9110, and a sender writing
        // "bearer" is not a mistake worth being strict about.
        if (value is null
            || value.Length <= Scheme.Length
            || !value.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase)
            || !char.IsWhiteSpace(value[Scheme.Length]))
        {
            return false;
        }

        return TokenText.TryParse(value[Scheme.Length..].TrimStart(), out presented);
    }
}
