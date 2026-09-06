using Logaffe.Domain.Identities;

namespace Logaffe.Application.Ports;

/// <summary>
/// The identities an installation holds — its users and its agents — and the
/// backup codes hanging off a user.
/// </summary>
/// <remarks>
/// <para>
/// One store for one table (ADR 0052). A user is found by the normalized form
/// of their address, which is what the sign-in has in hand and what the unique
/// index stands on; an identity of either kind is found by its id, which is what
/// a session, a token and a record of a change all carry.
/// </para>
/// <para>
/// The backup codes are here rather than in a store of their own because they
/// cannot exist without a user, are replaced as a set, and are counted rather
/// than fetched one at a time.
/// </para>
/// </remarks>
public interface IIdentities
{
    /// <summary>
    /// Whether this installation holds any identity at all. It is the one
    /// question the bootstrap asks, and it is asked without reading a credential
    /// to answer it (ADR 0054).
    /// </summary>
    Task<bool> AnyAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The user behind a normalized address, whatever state they are in.
    /// Deactivated and invited users come back too: refusing them is the
    /// caller's, so that every way of not getting in costs the same
    /// (ADR 0056).
    /// </summary>
    Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);

    /// <summary>The user with this id, or <c>null</c>.</summary>
    Task<User?> FindUserAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The identity with this id, of either kind, or <c>null</c>.</summary>
    Task<Identity?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Every user, in the order the list is shown: by name. Agents are not here
    /// — they are listed with the tokens they authenticate by.
    /// </summary>
    Task<IReadOnlyList<User>> ListUsersAsync(CancellationToken cancellationToken);

    /// <summary>
    /// How many active administrators there are. It is what holds the rule that
    /// there is always at least one, and it is counted in the database rather
    /// than by reading the list, because it is asked on the way into a write
    /// that would break it (ADR 0052).
    /// </summary>
    Task<int> CountActiveAdministratorsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Writes a new identity. It refuses the address that is already held, on
    /// the unique index rather than on a check this could have run first and
    /// been wrong about a moment later — <c>false</c> is what the loser of that
    /// race gets.
    /// </summary>
    Task<bool> TryAddAsync(Identity identity, CancellationToken cancellationToken);

    /// <summary>
    /// Writes back what was just changed on an identity — a password, a rehash
    /// at the current cost, a re-enrolled second factor, a state, a role.
    /// </summary>
    Task RecordAsync(Identity identity, CancellationToken cancellationToken);

    /// <summary>
    /// Removes every identity on the installation, which is what Host Recovery
    /// does and the only thing that ever deletes one (ADR 0058). The sessions
    /// and backup codes go with them; projects, tokens and entries are
    /// untouched.
    /// </summary>
    Task<int> RemoveEveryIdentityAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Every backup code this user holds, spent ones included — that is what
    /// makes "how many remain" a count and a spent code visibly spent
    /// (ADR 0057). It is a handful of rows, and the presented code is compared
    /// against them in constant time rather than looked up.
    /// </summary>
    Task<IReadOnlyList<BackupCode>> ListBackupCodesAsync(
        Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Puts a fresh set in place of whatever this user had. It replaces
    /// wholesale: nothing of the previous set survives it (ADR 0057).
    /// </summary>
    Task ReplaceBackupCodesAsync(
        Guid userId, IReadOnlyList<BackupCode> backupCodes, CancellationToken cancellationToken);

    /// <summary>Writes back the code just spent.</summary>
    Task RecordConsumptionAsync(BackupCode code, CancellationToken cancellationToken);
}
