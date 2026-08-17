using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;
using Microsoft.EntityFrameworkCore;

namespace Logaffe.Infrastructure.Persistence;

/// <summary>
/// The account row and the backup codes beside it.
/// </summary>
/// <remarks>
/// Every statement here is against a table holding one row or ten, so nothing in
/// it is a performance question. What it does have to get right is the claim:
/// the account and its first set of codes are one <c>SaveChanges</c>, which is
/// one transaction, so a claim either happened or did not (ADR 0014).
/// </remarks>
public sealed class Operators(LogaffeDbContext context) : IOperators
{
    public Task<bool> IsClaimedAsync(CancellationToken cancellationToken) =>
        context.Operators.AnyAsync(cancellationToken);

    public Task<Operator?> FindAsync(CancellationToken cancellationToken) =>
        context.Operators.SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> TryClaimAsync(
        Operator theOperator, CancellationToken cancellationToken)
    {
        context.Operators.Add(theOperator);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // The installation was claimed while this claimant was walking the
            // flow, and the unique index on the account table is what said so.
            // Nothing was written — the whole thing was one transaction — and
            // this context is not used again: the request it belongs to ends
            // with the screen ADR 0014 describes.
            return false;
        }
    }

    public async Task RecordAsync(Operator theOperator, CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);

    public async Task RemoveAsync(Operator theOperator, CancellationToken cancellationToken)
    {
        context.Operators.Remove(theOperator);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BackupCode>> ListBackupCodesAsync(
        CancellationToken cancellationToken) =>
        await context.BackupCodes.OrderBy(c => c.Id).ToListAsync(cancellationToken);

    public async Task ReplaceBackupCodesAsync(
        IReadOnlyList<BackupCode> backupCodes, CancellationToken cancellationToken)
    {
        // Read, remove, add, save: one transaction, so there is no moment at
        // which the operator holds no codes at all. It is a set of ten, which is
        // why this can afford to be the plain thing.
        context.BackupCodes.RemoveRange(await context.BackupCodes.ToListAsync(cancellationToken));
        context.BackupCodes.AddRange(backupCodes);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordConsumptionAsync(
        BackupCode code, CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);
}
