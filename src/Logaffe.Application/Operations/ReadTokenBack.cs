using Logaffe.Application.Ports;
using Logaffe.Domain.Tokens;

namespace Logaffe.Application.Operations;

/// <summary>
/// The token that is in the row, put together again.
/// </summary>
/// <remarks>
/// <para>
/// This is ADR 0022 being what it was decided for. A mislaid token is looked up
/// rather than rotated and redeployed: there is one account, it can do
/// everything, and a token grants strictly less than the session asking for
/// it — an agent token reads logs the operator is already reading, and an ingest
/// token writes to a project they own. Hiding it would protect nothing and cost
/// the re-issue cycle every time.
/// </para>
/// <para>
/// It is the only place a stored secret comes back into the clear, which is why
/// it is an act of its own and not something a list does on the way past. A
/// screen that shows six agent tokens has not read six secrets; it has read six
/// names.
/// </para>
/// </remarks>
public sealed class ReadTokenBack(ITokens tokens, ISecretCipher cipher)
{
    /// <summary>
    /// The whole token of an ingest row, or <c>null</c> when there is no such
    /// row.
    /// </summary>
    /// <remarks>
    /// A row this cipher cannot open throws rather than answering <c>null</c>,
    /// and that is deliberate: it is a corrupt row or a lost key — an
    /// installation-level fault the startup check exists to catch — and
    /// answering "no such token" would tell the operator to reissue a token that
    /// is sitting right there.
    /// </remarks>
    public async Task<TokenText?> IngestTokenAsync(Guid id, CancellationToken cancellationToken)
    {
        var token = await tokens.FindIngestTokenAsync(id, cancellationToken);

        return token is null
            ? null
            : TokenText.From(
                TokenKind.Ingest, token.Identifier, cipher.Decrypt(token.EncryptedSecret));
    }

    /// <inheritdoc cref="IngestTokenAsync"/>
    public async Task<TokenText?> AgentTokenAsync(Guid id, CancellationToken cancellationToken)
    {
        var token = await tokens.FindAgentTokenAsync(id, cancellationToken);

        return token is null
            ? null
            : TokenText.From(
                TokenKind.Agent, token.Identifier, cipher.Decrypt(token.EncryptedSecret));
    }

    /// <inheritdoc cref="IngestTokenAsync"/>
    /// <remarks>
    /// This one is read back more than the others are. What the operator wants
    /// is rarely the token on its own but the command that starts a collector
    /// with it in place, and that is assembled from this every time it is asked
    /// for (<c>docs/metrics.md</c>).
    /// </remarks>
    public async Task<TokenText?> HostTokenAsync(Guid id, CancellationToken cancellationToken)
    {
        var token = await tokens.FindHostTokenAsync(id, cancellationToken);

        return token is null
            ? null
            : TokenText.From(
                TokenKind.Host, token.Identifier, cipher.Decrypt(token.EncryptedSecret));
    }
}
