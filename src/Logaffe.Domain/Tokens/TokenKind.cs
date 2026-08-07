namespace Logaffe.Domain.Tokens;

/// <summary>
/// Which of the two machine credentials a token is.
/// </summary>
/// <remarks>
/// One credential model pointing in two directions — an ingest token writes to
/// one project, an agent token reads everything (ADR 0021). The kind is carried
/// by the token's prefix and is never stored: which table the row is in is what
/// says which kind it is.
/// </remarks>
public enum TokenKind
{
    Ingest = 0,
    Agent = 1,
}
