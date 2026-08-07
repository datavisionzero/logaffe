using Logaffe.Application.Ports;
using Logaffe.Domain.Tokens;

namespace Logaffe.Application.Operations;

/// <summary>
/// How issuing ended.
/// </summary>
public enum IssueOutcome
{
    /// <summary>The project has a token it did not have before.</summary>
    Issued,

    /// <summary>
    /// There is no such project. Another browser tab deleted it, or the address
    /// was typed.
    /// </summary>
    NoSuchProject,

    /// <summary>
    /// The project already holds the two that rotation is made of, and a third
    /// is refused rather than queued.
    /// </summary>
    AlreadyHoldsTwo,
}

/// <summary>
/// The end of an issue, and the token it hands over once when it succeeded.
/// </summary>
public sealed record IssueAttempt(IssueOutcome Outcome, IssuedToken? Token);

/// <summary>
/// Gives a project a token to receive on, and gives it the second one that
/// rotation is made of.
/// </summary>
/// <remarks>
/// <para>
/// The whole of issuing is here: draw a token, seal its secret with the key on
/// the host volume, keep the identifier in the clear so the row can be found
/// again, and hand the token to the operator once. The secret is never held
/// anywhere else in the clear, and the row keeps only what
/// <see cref="ISecretCipher"/> made of it (ADR 0022).
/// </para>
/// <para>
/// It is an operator act and is unreachable over MCP, which is a property of the
/// interface rather than a permission: a log entry that asks an agent to mint a
/// credential must find nothing to call
/// (ADR 0018).
/// </para>
/// </remarks>
public sealed class IssueIngestToken(
    IProjects projects, ITokens tokens, ISecretCipher cipher, TimeProvider clock)
{
    /// <summary>
    /// The token the project may now receive on, or why it got none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The refusal is the rotation model saying what it is for: two tokens exist
    /// so that deployments can be moved over one at a time, and a third would
    /// mean the operator has lost track of which one they are retiring. They
    /// revoke one first, which is immediate.
    /// </para>
    /// <para>
    /// Two issues racing each other could pass the count together and leave the
    /// project holding three. That is one operator racing themselves in two
    /// browser tabs — there is exactly one account (ADR 0015) — and the outcome
    /// is a token too many rather than anything unsafe, so it is not bought off
    /// with a lock the rest of the product would then have to carry.
    /// </para>
    /// <para>
    /// <b>That the project exists is asked first.</b> It has to be: a token
    /// issued into a project that is not there is a foreign key violation
    /// surfacing as a failure of the installation, when what happened is that
    /// the operator named something that is gone. The check does not make the
    /// foreign key redundant — a project deleted between here and the insert is
    /// still the database's to refuse — it makes the ordinary case an answer.
    /// </para>
    /// </remarks>
    public async Task<IssueAttempt> ExecuteAsync(
        Guid projectId, CancellationToken cancellationToken)
    {
        if (await projects.FindAsync(projectId, cancellationToken) is null)
        {
            return new IssueAttempt(IssueOutcome.NoSuchProject, null);
        }

        var held = await tokens.ListIngestTokensAsync(projectId, cancellationToken);
        if (held.Count >= IngestToken.MaximumPerProject)
        {
            return new IssueAttempt(IssueOutcome.AlreadyHoldsTwo, null);
        }

        var minted = TokenText.Mint(TokenKind.Ingest);
        var issuedAt = clock.GetUtcNow();
        var token = IngestToken.Issue(
            projectId, minted.Identifier, cipher.Encrypt(minted.Secret), issuedAt);

        await tokens.AddAsync(token, cancellationToken);

        return new IssueAttempt(
            IssueOutcome.Issued, new IssuedToken(token.Id, minted, issuedAt));
    }
}
