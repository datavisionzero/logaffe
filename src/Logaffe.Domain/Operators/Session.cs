using System.Security.Cryptography;

namespace Logaffe.Domain.Operators;

/// <summary>
/// One signed-in browser's standing permission to act as the operator.
/// </summary>
/// <remarks>
/// <para>
/// Several exist at once, because one person with a desktop and a laptop is the
/// normal case; each is listed with where and when it was last used, and each
/// can be ended on its own (<c>docs/sign-in.md</c>). With no email in the
/// product that list is the only way the operator can ever notice a session that
/// is not theirs, which makes <see cref="LastSeenFrom"/> a security surface
/// rather than a decoration.
/// </para>
/// <para>
/// The session <em>is</em> the remembering: there is no "trust this browser"
/// beside it, and nothing here is bound to a device — a second mechanism whose
/// purpose was skipping the second factor would weaken the thing that makes
/// public exposure defensible.
/// </para>
/// <para>
/// Ending one is removing the row, as revoking a token is: a session that is
/// signed out, revoked from the list, or left behind by a password change is
/// gone rather than marked. What expiry does is make a row that nobody removed
/// stop admitting anything.
/// </para>
/// </remarks>
public sealed class Session
{
    /// <summary>
    /// Thirty days, and every use pushes the deadline forward, so an
    /// installation in regular use is not a place where the operator keeps
    /// re-authenticating.
    /// </summary>
    public static readonly TimeSpan SlidingLifetime = TimeSpan.FromDays(30);

    /// <summary>
    /// Enough for an IPv6 address written out in full, which is what this holds.
    /// </summary>
    public const int SeenFromMaxLength = 45;

    private Session()
    {
        // EF Core materializes through this; every other route goes through Start.
    }

    private Session(
        Guid id,
        Guid operatorId,
        byte[] secretHash,
        string seenFrom,
        DateTimeOffset startedAt)
    {
        Id = id;
        OperatorId = operatorId;
        SecretHash = secretHash;
        LastSeenFrom = seenFrom;
        StartedAt = startedAt;
        LastUsedAt = startedAt;
    }

    public Guid Id { get; private init; }

    /// <summary>
    /// Whose session this is. There is exactly one operator, so this says
    /// nothing about which account — it is what makes Host Recovery removing the
    /// account take every session with it (<c>docs/setup.md</c>).
    /// </summary>
    public Guid OperatorId { get; private init; }

    /// <inheritdoc cref="SessionSecret.Hash"/>
    public byte[] SecretHash { get; private init; } = null!;

    public DateTimeOffset StartedAt { get; private init; }

    /// <summary>
    /// When this session last acted. It is both halves of what the operator is
    /// shown and what decides whether the session is still alive, since the
    /// deadline is measured from here.
    /// </summary>
    public DateTimeOffset LastUsedAt { get; private set; }

    /// <summary>
    /// Where it last acted from, as the address the request arrived with. It is
    /// what a person recognizes as theirs or does not, and it is written down
    /// for that reason alone.
    /// </summary>
    public string LastSeenFrom { get; private set; } = null!;

    /// <summary>
    /// When this session stops admitting anything if nothing touches it — the
    /// sliding deadline, derived rather than stored so that it cannot disagree
    /// with the last use it is measured from.
    /// </summary>
    public DateTimeOffset ExpiresAt => LastUsedAt + SlidingLifetime;

    public bool HasExpiredAt(DateTimeOffset when) => when >= ExpiresAt;

    /// <summary>
    /// Starts a session for a browser that has just proved both factors.
    /// </summary>
    public static Session Start(
        Guid operatorId, SessionSecret secret, string? seenFrom, DateTimeOffset startedAt) =>
        new(
            Guid.CreateVersion7(),
            operatorId,
            secret.Hash,
            Normalize(seenFrom),
            startedAt);

    /// <summary>
    /// Whether <paramref name="presented"/> is this session's secret, compared
    /// in constant time.
    /// </summary>
    /// <remarks>
    /// It says nothing about expiry: an expired session matches exactly as a
    /// live one does, and refusing it is the caller's, so that a stale cookie
    /// costs what a current one costs.
    /// </remarks>
    public bool Matches(SessionSecret presented) =>
        CryptographicOperations.FixedTimeEquals(SecretHash, presented.Hash);

    /// <summary>
    /// Records a use, which is what pushes the deadline forward.
    /// </summary>
    /// <remarks>
    /// Time only moves forward here, as it does on a token, so two requests
    /// arriving out of order cannot make a session look older than it is. How
    /// often a use is worth writing back is the caller's question and has the
    /// same answer as ADR 0033's: this row exists to be read by one human,
    /// occasionally, and a live-tailing browser asking every few seconds must
    /// not be an <c>UPDATE</c> every few seconds.
    /// </remarks>
    public void WasUsedAt(DateTimeOffset when, string? seenFrom)
    {
        if (when <= LastUsedAt)
        {
            return;
        }

        LastUsedAt = when;
        LastSeenFrom = Normalize(seenFrom);
    }

    /// <summary>
    /// An address the product could not read is <c>unknown</c> rather than
    /// nothing: the list has to say something in that column, and a blank one
    /// reads as a bug in the row rather than as a fact about the request.
    /// </summary>
    private static string Normalize(string? seenFrom)
    {
        var trimmed = seenFrom?.Trim();

        return string.IsNullOrEmpty(trimmed)
            ? "unknown"
            : trimmed.Length > SeenFromMaxLength
                ? trimmed[..SeenFromMaxLength]
                : trimmed;
    }
}
