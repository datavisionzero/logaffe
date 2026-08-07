using System.Security.Cryptography;

namespace Logaffe.Domain.Operators;

/// <summary>
/// One of a set of single-use codes, standing in for the second factor when it
/// is unavailable.
/// </summary>
/// <remarks>
/// <para>
/// A set is shown once during the claim and confirmed there, and a fresh set can
/// be generated at any time — which replaces the previous one entirely
/// (<c>docs/sign-in.md</c>). What is held here is a hash and a pair of dates;
/// the code itself exists for as long as it takes to show it.
/// </para>
/// <para>
/// It is <em>consumed by a timestamp rather than by a deletion</em> (ADR 0032),
/// so "how many remain" is a filtered count — which is what the product has to
/// say whenever one is spent — and a code that has been used stays visibly used
/// instead of vanishing.
/// </para>
/// </remarks>
public sealed class BackupCode
{
    /// <summary>
    /// How many are minted at once. Enough that an operator who spends one on a
    /// train does not immediately need a fresh sheet, few enough that the sheet
    /// is one to print.
    /// </summary>
    public const int SetSize = 10;

    /// <summary>SHA-256, so every stored hash is this long.</summary>
    public const int HashLength = 32;

    private BackupCode()
    {
        // EF Core materializes through this; every other route goes through MintSet.
    }

    private BackupCode(Guid id, Guid operatorId, byte[] hash, DateTimeOffset issuedAt)
    {
        Id = id;
        OperatorId = operatorId;
        Hash = hash;
        IssuedAt = issuedAt;
    }

    public Guid Id { get; private init; }

    public Guid OperatorId { get; private init; }

    /// <inheritdoc cref="BackupCodeText.Hash"/>
    public byte[] Hash { get; private init; } = null!;

    /// <summary>
    /// When the set this belongs to was minted. Every code of one set carries
    /// the same date, because they were shown on one sheet and are replaced as
    /// one.
    /// </summary>
    public DateTimeOffset IssuedAt { get; private init; }

    /// <summary>
    /// When this code was spent, and null while it has not been. This is the
    /// whole of what single-use means here.
    /// </summary>
    public DateTimeOffset? UsedAt { get; private set; }

    public bool IsSpent => UsedAt is not null;

    /// <summary>
    /// Draws a whole set: what the operator is shown, and what the installation
    /// keeps of it. They are returned together because they are one act — a set
    /// is minted, shown and stored, and there is no moment at which one of the
    /// two halves is meaningful on its own.
    /// </summary>
    public static MintedBackupCodes MintSet(Guid operatorId, DateTimeOffset issuedAt)
    {
        var shown = new List<BackupCodeText>(SetSize);
        var stored = new List<BackupCode>(SetSize);

        for (var index = 0; index < SetSize; index++)
        {
            var code = BackupCodeText.Mint();
            shown.Add(code);
            stored.Add(new BackupCode(Guid.CreateVersion7(), operatorId, code.Hash, issuedAt));
        }

        return new MintedBackupCodes(shown, stored);
    }

    /// <summary>
    /// Whether <paramref name="presented"/> is this code, compared in constant
    /// time — the property ADR 0032 calls not optional, and not the same
    /// property as the fast hash it is compared through.
    /// </summary>
    /// <remarks>
    /// It says nothing about whether the code is still good: a spent code
    /// matches exactly as a fresh one does, and refusing it is the caller's, so
    /// that a code offered twice costs the same as one offered once.
    /// </remarks>
    public bool Matches(BackupCodeText presented) =>
        CryptographicOperations.FixedTimeEquals(Hash, presented.Hash);

    /// <summary>Spends this code, which can happen once.</summary>
    /// <exception cref="InvalidOperationException">
    /// It has been spent already. Single use is the rule this type exists to
    /// hold, so it is refused here rather than trusted to the caller.
    /// </exception>
    public void ConsumeAt(DateTimeOffset when) =>
        UsedAt = IsSpent
            ? throw new InvalidOperationException("A backup code is used once.")
            : when;
}

/// <summary>
/// A freshly drawn set, in its two forms: <paramref name="Shown"/> is what the
/// operator sees once and <paramref name="Stored"/> is what replaces whatever
/// they had.
/// </summary>
public sealed record MintedBackupCodes(
    IReadOnlyList<BackupCodeText> Shown,
    IReadOnlyList<BackupCode> Stored);
