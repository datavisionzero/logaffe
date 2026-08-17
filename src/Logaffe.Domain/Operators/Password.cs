namespace Logaffe.Domain.Operators;

/// <summary>
/// A password as the operator typed it, on its way to being hashed.
/// </summary>
/// <remarks>
/// <para>
/// A minimum length and nothing else — no composition rules, no forced rotation,
/// and no check against an outside service (<c>docs/sign-in.md</c>). Length is
/// the property that matters, and on an installation whose operator enrolled no
/// second factor it is the only property there is (ADR 0042).
/// </para>
/// <para>
/// This is what the rule lives in; what turns it into bytes is a port, because
/// the hash is an algorithm that gets replaced and the minimum is a decision
/// that does not (ADR 0032). Nothing here is ever stored: the value is held for
/// the length of one request and the row holds what the hasher wrote.
/// </para>
/// <para>
/// A class rather than a record on purpose, for the same reason as
/// <see cref="Tokens.TokenText"/>: a password is proved by the hasher and never
/// by equality.
/// </para>
/// </remarks>
public sealed class Password
{
    /// <summary>
    /// Sixteen characters, which is a passphrase — three words and a separator —
    /// rather than a rule about symbols.
    /// </summary>
    /// <remarks>
    /// It was twelve while the second factor stood behind it. The second factor
    /// is the operator's to enrol (ADR 0041), so this may be the only credential
    /// on the account, and length is the property the product can actually set:
    /// a stolen dump is where this credential is attacked without limit, and
    /// what stands there is this number and the hasher's cost (ADR 0042).
    /// </remarks>
    public const int MinimumLength = 16;

    /// <summary>
    /// The upper bound is not a rule about passwords, it is a bound on the work
    /// the hasher does. Hashing is deliberately slow and the sign-in surface is
    /// public and pre-authentication, so a megabyte of input has to be refused
    /// before it reaches PBKDF2 rather than after.
    /// </summary>
    public const int MaximumLength = 256;

    private Password(string text) => Text = text;

    /// <summary>The characters themselves, which go to the hasher and nowhere else.</summary>
    public string Text { get; }

    public static Password Create(string? value) =>
        TryCreate(value, out var password)
            ? password
            : throw new ArgumentException(
                $"A password is between {MinimumLength} and {MaximumLength} characters.",
                nameof(value));

    /// <summary>
    /// Reads a password the operator is <b>choosing</b>, and refuses one that is
    /// not long enough. It is deliberately not trimmed and not normalized: what
    /// the operator typed is what they will type again, and quietly dropping a
    /// leading space would make a password that works today fail against a
    /// client that does not.
    /// </summary>
    public static bool TryCreate(string? value, out Password password)
    {
        if (value is null || value.Length < MinimumLength || value.Length > MaximumLength)
        {
            password = null!;
            return false;
        }

        password = new Password(value);
        return true;
    }

    /// <summary>
    /// Reads a password the operator is <b>presenting</b>, which is bounded by
    /// what the hasher may be asked to do and by nothing else.
    /// </summary>
    /// <remarks>
    /// <see cref="MinimumLength"/> is a rule about choosing a password and not
    /// about proving one. Applying it here would mean that raising the minimum
    /// locks out every operator whose password was long enough when they set it —
    /// a rule that arrives with an upgrade and takes the installation with it,
    /// where the honest answer is that their password is simply either right or
    /// wrong. A short one reaches the hasher and fails there, which costs one
    /// verification on a throttled surface (ADR 0017).
    /// </remarks>
    public static bool TryRead(string? value, out Password password)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumLength)
        {
            password = null!;
            return false;
        }

        password = new Password(value);
        return true;
    }

    /// <summary>
    /// Redacted, so that a password reaching a log line or an exception message
    /// by way of an interpolation carries nothing at all.
    /// </summary>
    public override string ToString() => "…";
}
