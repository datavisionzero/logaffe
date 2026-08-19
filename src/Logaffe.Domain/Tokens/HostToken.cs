namespace Logaffe.Domain.Tokens;

/// <summary>
/// The write-only secret that admits a collector's samples to one host.
/// </summary>
/// <remarks>
/// It is the ingest token's model pointed at a host instead of a project: it
/// permits writing and grants no read access of any kind, it is what identifies
/// the host — a delivery never names one — and it records when it was last used.
/// What is held here is the secret <em>encrypted</em>, so that a stolen database
/// backup yields nothing usable and the operator can read the token back rather
/// than rotating and reconfiguring every machine (ADR 0022).
/// <para>
/// Because it writes and reads nothing, it survives Host Recovery. An agent
/// token does not, and the difference is the whole of what that distinction is:
/// a credential that reads everything must not outlive the operator who issued
/// it, and one that can only add numbers to a machine's own history carries
/// nothing out of an installation it no longer belongs to (ADR 0013).
/// </para>
/// </remarks>
public sealed class HostToken
{
    /// <summary>
    /// A host holds one token, and two while it is being rotated — the ingest
    /// token's rule, for the ingest token's reason: a hard cutover would put a
    /// gap into every machine still holding the old value.
    /// </summary>
    public const int MaximumPerHost = 2;

    private HostToken()
    {
        // EF Core materializes through this; every other route goes through Issue.
    }

    private HostToken(
        Guid id,
        Guid hostId,
        TokenIdentifier identifier,
        byte[] encryptedSecret,
        DateTimeOffset issuedAt)
    {
        Id = id;
        HostId = hostId;
        Identifier = identifier;
        EncryptedSecret = encryptedSecret;
        IssuedAt = issuedAt;
    }

    public Guid Id { get; private init; }

    /// <summary>
    /// The host this admits samples to, by the identity that survives every
    /// rename.
    /// </summary>
    public Guid HostId { get; private init; }

    /// <summary>
    /// What the presented token is found by. Unique across the host tokens, and
    /// public: it names this row and admits nothing.
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
    /// When a collector last presented this token, and null until one has. It is
    /// what makes a rotation finishable — the old token is done when its last
    /// use stops moving — and it is not the same fact as when the host last
    /// reported, which is read off the newest sample.
    /// </summary>
    public DateTimeOffset? LastUsedAt { get; private set; }

    public static HostToken Issue(
        Guid hostId,
        TokenIdentifier identifier,
        byte[] encryptedSecret,
        DateTimeOffset issuedAt) =>
        new(
            Guid.CreateVersion7(),
            hostId,
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
