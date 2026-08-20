namespace Logaffe.Domain.Tokens;

/// <summary>
/// The secret an agent presents to MCP, which reads every project or
/// administers the installation.
/// </summary>
/// <remarks>
/// Several exist at once and each is revocable on its own. It is the same shape
/// as an ingest token deliberately: one credential model for a machine, pointing
/// in several directions (ADR 0021).
/// <para>
/// What it may do is <see cref="Kind"/> and <see cref="MayDestroy"/> beside it,
/// and both are settled here, at the moment it is issued. There is no act that
/// changes either — a credential that grows new powers after it has been pasted
/// into a client is one the operator cannot reason about from a list, and an
/// editable kind is ADR 0046's checkbox arriving through a side door.
/// </para>
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
        AgentTokenKind kind,
        bool mayDestroy,
        TokenIdentifier identifier,
        byte[] encryptedSecret,
        DateTimeOffset issuedAt)
    {
        Id = id;
        Name = name;
        Kind = kind;
        MayDestroy = mayDestroy;
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
    /// Whether this token reads entries or administers the installation. The
    /// prefix says it as well, so that a token presented to the wrong half of
    /// the surface fails at the door — but the prefix is written by whoever
    /// presents the token, and this is the row's own word for it.
    /// </summary>
    public AgentTokenKind Kind { get; private init; }

    /// <summary>
    /// Whether this token may make a change after which stored data is gone:
    /// deleting a project or a host, and lowering a retention window — a
    /// project's, or the installation's for samples. Off unless it was issued
    /// saying so, and never true of a reading token, which changes nothing at
    /// all (ADR 0046).
    /// </summary>
    public bool MayDestroy { get; private init; }

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

    /// <exception cref="ArgumentException">
    /// <paramref name="mayDestroy"/> is asked of a reading token. That is not a
    /// smaller request than an administering one, it is a nonsense one: a
    /// reading token makes no change of any kind, so it is refused here rather
    /// than stored as a flag that means nothing.
    /// </exception>
    public static AgentToken Issue(
        string name,
        AgentTokenKind kind,
        bool mayDestroy,
        TokenIdentifier identifier,
        byte[] encryptedSecret,
        DateTimeOffset issuedAt) =>
        mayDestroy && kind is not AgentTokenKind.Administering
            ? throw new ArgumentException(
                "Only an administering token can be issued to destroy.", nameof(mayDestroy))
            : new AgentToken(
                Guid.CreateVersion7(),
                Normalize(name),
                kind,
                mayDestroy,
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
