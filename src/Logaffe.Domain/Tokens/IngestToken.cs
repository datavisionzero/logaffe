namespace Logaffe.Domain.Tokens;

/// <summary>
/// The write-only secret that admits a delivery to one project.
/// </summary>
/// <remarks>
/// It permits writing and grants no read access of any kind, it is what
/// identifies the project — a delivery never names one — and it records when it
/// was last used. What is held here is the secret <em>encrypted</em>, so that a
/// stolen database backup yields nothing usable and the operator can still read
/// the token back rather than rotating and redeploying (ADR 0022).
/// </remarks>
public sealed class IngestToken
{
    /// <summary>
    /// A project holds one token, and two while it is being rotated: the
    /// operator issues the second, moves the applications over, watches the old
    /// one go quiet, and revokes it. A hard cutover would put a gap into
    /// delivery for every application still holding the old value.
    /// </summary>
    public const int MaximumPerProject = 2;

    private IngestToken()
    {
        // EF Core materializes through this; every other route goes through Issue.
    }

    private IngestToken(
        Guid id,
        Guid projectId,
        TokenIdentifier identifier,
        byte[] encryptedSecret,
        DateTimeOffset issuedAt)
    {
        Id = id;
        ProjectId = projectId;
        Identifier = identifier;
        EncryptedSecret = encryptedSecret;
        IssuedAt = issuedAt;
    }

    public Guid Id { get; private init; }

    /// <summary>
    /// The project this admits a delivery to, by the identity that survives
    /// every rename.
    /// </summary>
    public Guid ProjectId { get; private init; }

    /// <summary>
    /// What the presented token is found by. Unique across the ingest tokens,
    /// and public: it names this row and admits nothing.
    /// </summary>
    public TokenIdentifier Identifier { get; private init; } = null!;

    /// <summary>
    /// The secret half under the key that lives on the host volume and never in
    /// the database. Verifying a delivery decrypts this once and compares in
    /// constant time (ADR 0031).
    /// </summary>
    public byte[] EncryptedSecret { get; private init; } = null!;

    public DateTimeOffset IssuedAt { get; private init; }

    /// <summary>
    /// When a delivery last presented this token, and null until one has.
    /// </summary>
    /// <remarks>
    /// Without it, rotation is guesswork: the operator issues a new token, rolls
    /// the deployments over, and then has to decide whether the old one is still
    /// feeding something they forgot. With it, rotation is finished when the old
    /// token's last use stops moving.
    /// </remarks>
    public DateTimeOffset? LastUsedAt { get; private set; }

    public static IngestToken Issue(
        Guid projectId,
        TokenIdentifier identifier,
        byte[] encryptedSecret,
        DateTimeOffset issuedAt) =>
        new(
            Guid.CreateVersion7(),
            projectId,
            identifier,
            TokenSecret.Encrypted(encryptedSecret, nameof(encryptedSecret)),
            issuedAt);

    /// <summary>
    /// Records a use. Time only moves forward here, so a delivery that arrives
    /// out of order behind another cannot make a token look quieter than it is.
    /// </summary>
    public void WasUsedAt(DateTimeOffset when)
    {
        if (LastUsedAt is null || when > LastUsedAt)
        {
            LastUsedAt = when;
        }
    }
}
