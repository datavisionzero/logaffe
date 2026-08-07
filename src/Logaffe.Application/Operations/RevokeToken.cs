using Logaffe.Application.Ports;

namespace Logaffe.Application.Operations;

/// <summary>
/// Ends a token, immediately.
/// </summary>
/// <remarks>
/// <para>
/// <b>Revoking removes the row.</b> A revoked token is not kept as a revoked
/// one: the <c>401</c> a sender gets is the same whether the token was revoked
/// this morning or never existed, so a marked row would be a history that
/// answers no question the product asks — and it would leave the sealed secret
/// of a dead credential lying in the database for as long as the installation
/// lives.
/// </para>
/// <para>
/// Immediately is the whole promise. There is no cache of admitted tokens
/// anywhere between here and authentication, which looks each presented token up
/// as it comes (ADR 0031), so the next delivery on a revoked token is refused by
/// the same lookup that would have found it.
/// </para>
/// <para>
/// A sender still holding a revoked token neither retries nor notices — it keeps
/// writing its own local file, which is where its logs were before logaffe
/// existed. That is why a rotation done carelessly costs a gap in the central
/// copy and nothing else.
/// </para>
/// </remarks>
public sealed class RevokeToken(ITokens tokens)
{
    /// <summary>
    /// Whether there was a token to revoke. <c>false</c> is a token already gone
    /// — a second click, or a project deleted in another tab — and not a
    /// failure of anything.
    /// </summary>
    /// <remarks>
    /// Revoking a project's last token is allowed and leaves it receiving
    /// nothing until one is issued again. The operator is entitled to close a
    /// project's door without deleting the project.
    /// </remarks>
    public async Task<bool> IngestTokenAsync(Guid id, CancellationToken cancellationToken)
    {
        var token = await tokens.FindIngestTokenAsync(id, cancellationToken);
        if (token is null)
        {
            return false;
        }

        await tokens.RemoveAsync(token, cancellationToken);
        return true;
    }

    /// <inheritdoc cref="IngestTokenAsync"/>
    public async Task<bool> AgentTokenAsync(Guid id, CancellationToken cancellationToken)
    {
        var token = await tokens.FindAgentTokenAsync(id, cancellationToken);
        if (token is null)
        {
            return false;
        }

        await tokens.RemoveAsync(token, cancellationToken);
        return true;
    }
}
