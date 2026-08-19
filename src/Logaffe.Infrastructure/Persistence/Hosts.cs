using Logaffe.Application.Ports;
using Logaffe.Domain.Hosts;
using Microsoft.EntityFrameworkCore;

namespace Logaffe.Infrastructure.Persistence;

/// <summary>
/// The host rows.
/// </summary>
/// <remarks>
/// A handful of rows read whole, and single-row lookups on the primary key or on
/// <c>ix_host_name</c>. Deleting is one statement: the tokens go with it by the
/// cascade on <c>fk_host_token_host</c> and the projects are left sitting on no
/// host by the set-null on <c>fk_project_host</c>, which is why neither is read
/// first. The samples are not touched — they follow in the background, as a
/// deleted project's entries do (ADR 0019).
/// </remarks>
public sealed class Hosts(LogaffeDbContext context) : IHosts
{
    public async Task<IReadOnlyList<Host>> ListAsync(CancellationToken cancellationToken) =>
        await context.Hosts.OrderBy(h => h.CreatedAt).ToListAsync(cancellationToken);

    public Task<Host?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        context.Hosts.SingleOrDefaultAsync(h => h.Id == id, cancellationToken);

    public Task<Host?> FindAsync(string name, CancellationToken cancellationToken) =>
        context.Hosts.SingleOrDefaultAsync(h => h.Name == name, cancellationToken);

    public async Task AddAsync(Host host, CancellationToken cancellationToken)
    {
        context.Hosts.Add(host);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordAsync(Host host, CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);

    public async Task RemoveAsync(Host host, CancellationToken cancellationToken)
    {
        context.Hosts.Remove(host);
        await context.SaveChangesAsync(cancellationToken);
    }
}
