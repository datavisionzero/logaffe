namespace Logaffe.Domain.Tokens;

/// <summary>
/// Which of the four machine credentials a token is.
/// </summary>
/// <remarks>
/// One credential model pointing in four directions — an ingest token writes to
/// one project, a host token writes to one host, and an agent token either reads
/// every project or administers the installation (ADR 0021, ADR 0046). The kind
/// is carried by the token's prefix and is read before the database is asked
/// anything at all.
/// <para>
/// It is not stored on an ingest or a host row: which table the row is in is
/// what says which of those two it is. The two agent kinds share a table, so
/// there the kind is on the row as well — the prefix is written by whoever
/// presents the token, and a rewritten one must not become another kind
/// (<see cref="AgentTokenKind"/>).
/// </para>
/// </remarks>
public enum TokenKind
{
    Ingest = 0,
    Agent = 1,

    /// <summary>
    /// Admits a collector's samples to one host. It writes and reads nothing,
    /// which is why it survives Host Recovery where an agent token does not.
    /// </summary>
    Host = 2,

    /// <summary>
    /// The agent token that reaches the settings and no entry.
    /// <see cref="Agent"/> stays what it was — the reading token's prefix is
    /// unchanged, so every token issued before this one goes on working
    /// (ADR 0046).
    /// </summary>
    Administering = 3,
}
