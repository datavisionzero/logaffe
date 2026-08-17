using Logaffe.Domain.Operators;

namespace Logaffe.Application.Ports;

/// <summary>
/// The one account row an installation may hold, and the backup codes hanging
/// off it.
/// </summary>
/// <remarks>
/// <para>
/// There is no lookup by anything: the operator is not found, they either exist
/// or the installation is unclaimed (ADR 0015). That question —
/// <see cref="IsClaimedAsync"/> — is asked on paths that must not read a
/// credential to answer it, which is why it is its own method rather than a null
/// check on the row.
/// </para>
/// <para>
/// The backup codes are here rather than in a store of their own because they
/// cannot exist without an operator, are replaced as a set, and are counted
/// rather than fetched one at a time.
/// </para>
/// </remarks>
public interface IOperators
{
    /// <summary>
    /// Whether this installation has an operator. It is a single fact with no
    /// in-between value, which is what lets the claim window, the read paths and
    /// the recovery command all ask it and get an unambiguous answer
    /// (ADR 0014).
    /// </summary>
    Task<bool> IsClaimedAsync(CancellationToken cancellationToken);

    /// <summary>The account, or <c>null</c> while the installation is unclaimed.</summary>
    Task<Operator?> FindAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Writes the account, and answers whether this claim is the one that took
    /// the installation.
    /// </summary>
    /// <remarks>
    /// This is the whole of what the claim stores (ADR 0014): a password, and no
    /// second factor and no backup codes, because those are the operator's to
    /// enrol afterwards (ADR 0041). Two claimants racing both reach here, and
    /// <c>false</c> is what the loser gets — decided by the database holding one
    /// account rather than by a check this could have run first and been wrong
    /// about a moment later.
    /// </remarks>
    Task<bool> TryClaimAsync(Operator theOperator, CancellationToken cancellationToken);

    /// <summary>
    /// Writes back what was just changed on the account — a password, a rehash
    /// at the current cost, a re-enrolled second factor.
    /// </summary>
    Task RecordAsync(Operator theOperator, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the account, which is what Host Recovery does: it returns the
    /// installation to unclaimed rather than resetting anything, and the
    /// sessions and backup codes go with it (ADR 0013). Projects, tokens and
    /// entries are untouched.
    /// </summary>
    Task RemoveAsync(Operator theOperator, CancellationToken cancellationToken);

    /// <summary>
    /// Every backup code the operator holds, spent ones included — that is what
    /// makes "how many remain" a count and a spent code visibly spent
    /// (ADR 0032). It is a handful of rows, and the presented code is compared
    /// against them in constant time rather than looked up.
    /// </summary>
    Task<IReadOnlyList<BackupCode>> ListBackupCodesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Puts a fresh set in place of whatever was there. It replaces wholesale:
    /// nothing of the previous set survives it (ADR 0032).
    /// </summary>
    Task ReplaceBackupCodesAsync(
        IReadOnlyList<BackupCode> backupCodes, CancellationToken cancellationToken);

    /// <summary>Writes back the code just spent.</summary>
    Task RecordConsumptionAsync(BackupCode code, CancellationToken cancellationToken);
}
