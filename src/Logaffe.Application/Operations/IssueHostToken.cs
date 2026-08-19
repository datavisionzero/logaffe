using Logaffe.Application.Ports;
using Logaffe.Domain.Tokens;

namespace Logaffe.Application.Operations;

/// <summary>How issuing a host token ended.</summary>
public enum IssueHostTokenOutcome
{
    /// <summary>The host has a token it did not have before.</summary>
    Issued,

    /// <summary>
    /// There is no such host. Another browser tab deleted it, or the address was
    /// typed.
    /// </summary>
    NoSuchHost,

    /// <summary>
    /// The host already holds the two that rotation is made of, and a third is
    /// refused rather than queued.
    /// </summary>
    AlreadyHoldsTwo,
}

/// <summary>
/// The end of an issue, and the token it hands over once when it succeeded.
/// </summary>
public sealed record HostTokenAttempt(IssueHostTokenOutcome Outcome, IssuedToken? Token);

/// <summary>
/// Gives a host a token its collector can report on, and gives it the second one
/// that rotation is made of.
/// </summary>
/// <remarks>
/// <para>
/// The ingest token's act pointed at a machine, and the same in every respect
/// that matters: draw a token, seal its secret with the key on the host volume,
/// keep the identifier in the clear so the row can be found again, and hand the
/// token over once. What the row keeps is only what <see cref="ISecretCipher"/>
/// made of it (ADR 0022), and the operator can read it back at any time — which
/// on a fleet of machines is the difference between looking a value up and going
/// round every one of them.
/// </para>
/// <para>
/// It is an operator act and is unreachable over MCP (ADR 0018).
/// </para>
/// </remarks>
public sealed class IssueHostToken(
    IHosts hosts, ITokens tokens, ISecretCipher cipher, TimeProvider clock)
{
    public async Task<HostTokenAttempt> ExecuteAsync(
        Guid hostId, CancellationToken cancellationToken)
    {
        if (await hosts.FindAsync(hostId, cancellationToken) is null)
        {
            return new HostTokenAttempt(IssueHostTokenOutcome.NoSuchHost, null);
        }

        var held = await tokens.ListHostTokensAsync(hostId, cancellationToken);
        if (held.Count >= HostToken.MaximumPerHost)
        {
            return new HostTokenAttempt(IssueHostTokenOutcome.AlreadyHoldsTwo, null);
        }

        var minted = TokenText.Mint(TokenKind.Host);
        var issuedAt = clock.GetUtcNow();
        var token = HostToken.Issue(
            hostId, minted.Identifier, cipher.Encrypt(minted.Secret), issuedAt);

        await tokens.AddAsync(token, cancellationToken);

        return new HostTokenAttempt(
            IssueHostTokenOutcome.Issued, new IssuedToken(token.Id, minted, issuedAt));
    }
}
