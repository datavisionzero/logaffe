namespace Logaffe.Domain.Operators;

/// <summary>
/// A password as the operator typed it, on its way to being hashed.
/// </summary>
/// <remarks>
/// <para>
/// A minimum length and nothing else — no composition rules, no forced rotation,
/// and no check against an outside service (<c>docs/sign-in.md</c>). Length is
/// the property that matters, and the second factor is what carries the rest.
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
    /// Twelve characters. Long enough that a human-chosen password is not
    /// trivially ground out of a stolen dump — which ADR 0032 accepts as the
    /// place this credential can be attacked without limit — and short enough
    /// that it is a passphrase somebody actually types on a phone keyboard.
    /// </summary>
    public const int MinimumLength = 12;

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
    /// Reads a typed password, and refuses one that is not long enough. It is
    /// deliberately not trimmed and not normalized: what the operator typed is
    /// what they will type again, and quietly dropping a leading space would
    /// make a password that works today fail against a client that does not.
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
    /// Redacted, so that a password reaching a log line or an exception message
    /// by way of an interpolation carries nothing at all.
    /// </summary>
    public override string ToString() => "…";
}
