namespace Logaffe.Domain.Tokens;

/// <summary>
/// The read-only secret an agent presents to MCP.
/// </summary>
/// <remarks>
/// It reads every project and grants no write of any kind, several exist at
/// once, and each is revocable on its own. It is the same shape as an ingest
/// token deliberately: one credential model for a machine, pointing in two
/// directions (ADR 0021).
/// </remarks>
public sealed class AgentToken
{
    public const int NameMaxLength = 100;

    private AgentToken()
    {
        // EF Core materializes through this; every other route goes through Issue.
    }

    private AgentToken(
        Guid id,
        string name,
        TokenIdentifier identifier,
        byte[] encryptedSecret,
        DateTimeOffset issuedAt)
    {
        Id = id;
        Name = name;
        Identifier = identifier;
        EncryptedSecret = encryptedSecret;
        IssuedAt = issuedAt;
    }

    public Guid Id { get; private init; }

    /// <summary>
    /// What the operator called this token, conventionally the client it was
    /// issued for, and renameable. It is a label for the operator's list and
    /// nothing more — it does not identify the token to the server, which is
    /// what <see cref="Identifier"/> is for, and changing it changes nothing
    /// else. Two agents may share one.
    /// </summary>
    public string Name { get; private set; } = null!;

    /// <summary>
    /// What the presented token is found by. Unique across the agent tokens, and
    /// public: it names this row and admits nothing.
    /// </summary>
    public TokenIdentifier Identifier { get; private init; } = null!;

    /// <summary>
    /// The secret half under the key that lives on the host volume and never in
    /// the database. Verifying a request decrypts this once and compares in
    /// constant time (ADR 0031).
    /// </summary>
    public byte[] EncryptedSecret { get; private init; } = null!;

    public DateTimeOffset IssuedAt { get; private init; }

    /// <summary>
    /// When an agent last presented this token, and null until one has.
    /// </summary>
    /// <remarks>
    /// The load-bearing field of ADR 0021: it is what turns "which of these can
    /// I revoke" from a guess into a reading, and without it a list of
    /// long-lived credentials is a list nobody prunes.
    /// </remarks>
    public DateTimeOffset? LastUsedAt { get; private set; }

    public static AgentToken Issue(
        string name,
        TokenIdentifier identifier,
        byte[] encryptedSecret,
        DateTimeOffset issuedAt) =>
        new(
            Guid.CreateVersion7(),
            Normalize(name),
            identifier,
            TokenSecret.Encrypted(encryptedSecret, nameof(encryptedSecret)),
            issuedAt);

    public void Rename(string name) => Name = Normalize(name);

    /// <inheritdoc cref="IngestToken.WasUsedAt"/>
    public void WasUsedAt(DateTimeOffset when)
    {
        if (LastUsedAt is null || when > LastUsedAt)
        {
            LastUsedAt = when;
        }
    }

    private static string Normalize(string name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("An agent token has a name.", nameof(name));
        }

        return trimmed.Length > NameMaxLength
            ? throw new ArgumentException(
                $"An agent token name is at most {NameMaxLength} characters.", nameof(name))
            : trimmed;
    }
}
