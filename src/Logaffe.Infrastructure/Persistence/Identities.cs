using Logaffe.Application.Ports;
using Logaffe.Domain.Identities;
using Microsoft.EntityFrameworkCore;

namespace Logaffe.Infrastructure.Persistence;

/// <summary>
/// The identity table, and the backup codes hanging off a user.
/// </summary>
/// <remarks>
/// An installation of this size holds a handful of these rows, so nothing here
/// is a performance question. What it does have to get right is the uniqueness
/// of an address, and that is the database's job rather than this class's: a
/// write that collides comes back as <c>false</c> from
/// <see cref="TryAddAsync"/> rather than as a check run first and wrong a moment
/// later.
/// </remarks>
public sealed class Identities(LogaffeDbContext context) : IIdentities
{
    public Task<bool> AnyAsync(CancellationToken cancellationToken) =>
        context.Identities.AnyAsync(cancellationToken);

    public Task<User?> FindByEmailAsync(
        string normalizedEmail, CancellationToken cancellationToken) =>
        context.Identities
            .OfType<User>()
            .SingleOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

    public Task<User?> FindUserAsync(Guid id, CancellationToken cancellationToken) =>
        context.Identities
            .OfType<User>()
            .SingleOrDefaultAsync(u => u.Id == id, cancellationToken);

    public async Task<Identity?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Identities.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);

    public async Task<IReadOnlyList<User>> ListUsersAsync(CancellationToken cancellationToken) =>
        await context.Identities
            .OfType<User>()
            .OrderBy(u => u.Name)
            .ToListAsync(cancellationToken);

    public Task<int> CountActiveAdministratorsAsync(CancellationToken cancellationToken) =>
        context.Identities
            .OfType<User>()
            .CountAsync(u => u.Administrator && u.State == UserState.Active, cancellationToken);

    public async Task<bool> TryAddAsync(Identity identity, CancellationToken cancellationToken)
    {
        context.Identities.Add(identity);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // The address is already held, or the name is. Nothing was written —
            // it was one statement — and this context is not used again: the
            // request it belongs to ends by saying so.
            return false;
        }
    }

    public async Task RecordAsync(Identity identity, CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);

    public async Task<int> RemoveEveryIdentityAsync(CancellationToken cancellationToken)
    {
        // One statement, and the sessions, backup codes and project assignments
        // follow on the cascade. This is the only thing in the product that
        // deletes an identity, and it deletes all of them (ADR 0058).
        var removed = await context.Identities.ExecuteDeleteAsync(cancellationToken);

        return removed;
    }

    public async Task<IReadOnlyList<BackupCode>> ListBackupCodesAsync(
        Guid userId, CancellationToken cancellationToken) =>
        await context.BackupCodes
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Id)
            .ToListAsync(cancellationToken);

    public async Task ReplaceBackupCodesAsync(
        Guid userId, IReadOnlyList<BackupCode> backupCodes, CancellationToken cancellationToken)
    {
        // Read, remove, add, save: one transaction, so there is no moment at
        // which the user holds no codes at all. It is a set of ten, which is why
        // this can afford to be the plain thing.
        context.BackupCodes.RemoveRange(
            await context.BackupCodes.Where(c => c.UserId == userId)
                .ToListAsync(cancellationToken));
        context.BackupCodes.AddRange(backupCodes);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordConsumptionAsync(
        BackupCode code, CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);
}
