namespace Logaffe.Domain.Tokens;

/// <summary>
/// Which of the three machine credentials a token is.
/// </summary>
/// <remarks>
/// One credential model pointing in three directions — an ingest token writes to
/// one project, a host token writes to one host, and an agent token reads
/// everything (ADR 0021). The kind is carried by the token's prefix and is never
/// stored: which table the row is in is what says which kind it is.
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
}
