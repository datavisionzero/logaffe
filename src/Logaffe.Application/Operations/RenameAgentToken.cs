using Logaffe.Application.Ports;
using Logaffe.Domain.Tokens;

namespace Logaffe.Application.Operations;

/// <summary>
/// Gives an agent token another label.
/// </summary>
/// <remarks>
/// It changes nothing else, and that is the point of it being a whole act rather
/// than a field: the name does not identify the token to the server — the
/// identifier does — so an agent whose token is renamed does not notice, and
/// nothing has to be reconnected. It exists because a name chosen while wiring a
/// client up — "claude-code", whatever was in front of the operator that
/// afternoon — is not what they will want to read in the list six months later.
/// </remarks>
public sealed class RenameAgentToken(ITokens tokens)
{
    /// <summary>
    /// Whether there was a token to rename.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is not a name — it is blank, or longer than
    /// <see cref="AgentToken.NameMaxLength"/>.
    /// </exception>
    public async Task<bool> ExecuteAsync(
        Guid id, string name, CancellationToken cancellationToken)
    {
        var token = await tokens.FindAgentTokenAsync(id, cancellationToken);
        if (token is null)
        {
            return false;
        }

        token.Rename(name);
        await tokens.RecordRenameAsync(token, cancellationToken);

        return true;
    }
}
