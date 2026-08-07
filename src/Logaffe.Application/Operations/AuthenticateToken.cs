using Logaffe.Application.Ports;
using Logaffe.Domain.Tokens;

namespace Logaffe.Application.Operations;

/// <summary>
/// What a presented token admits: for a delivery, the project it goes to; for an
/// agent, the permission to read; and for anything else, nothing.
/// </summary>
/// <remarks>
/// <para>
/// This is the first thing both public endpoints do, and the only place either
/// of them learns who is calling. It is one shape for two doors deliberately —
/// ADR 0021 keeps one credential model pointing in two directions, and the
/// prefix is what refuses each at the other's endpoint, before the database is
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
    /// Whether <paramref name="authorization"/> admits a read over MCP. There is
    /// nothing further to return: an agent token reads every project and writes
    /// nothing, so that it was admitted at all is the whole of its permission
    /// (ADR 0021).
    /// </summary>
    public async Task<bool> AdmitsReadAsync(
        string? authorization, CancellationToken cancellationToken)
    {
        if (!TryReadPresented(authorization, TokenKind.Agent, out var presented))
        {
            return false;
        }

        var token = await tokens.FindAgentTokenAsync(presented.Identifier, cancellationToken);
        if (token is null)
        {
            RefuseAtTheSamePrice(presented);
            return false;
        }

        if (!Matches(presented, token.EncryptedSecret))
        {
            return false;
        }

        var now = clock.GetUtcNow();
        if (IsWorthWriting(token.LastUsedAt, now))
        {
            token.WasUsedAt(now);
            await tokens.RecordUseAsync(token, cancellationToken);
        }

        return true;
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
        string? authorization, TokenKind kind, out TokenText presented)
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

        return TokenText.TryParse(value[Scheme.Length..].TrimStart(), out presented)
            && presented.Kind == kind;
    }
}
