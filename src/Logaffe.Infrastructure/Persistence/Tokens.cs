using Logaffe.Application.Ports;
using Logaffe.Domain.Tokens;
using Microsoft.EntityFrameworkCore;

namespace Logaffe.Infrastructure.Persistence;

/// <summary>
/// The token rows, out of the three tables that hold them.
/// </summary>
/// <remarks>
/// One statement per authentication, on the unique index the identifier column
/// carries (ADR 0031). The row is tracked because the same request may record a
/// use on it, and a use is then the one further statement — never a second read.
/// The operator's acts are the same shape and a great deal rarer: a lookup on
/// the primary key, or a project's handful of rows on
/// <c>ix_ingest_token_project</c>, and one write.
/// </remarks>
public sealed class Tokens(LogaffeDbContext context) : ITokens
{
    public Task<IngestToken?> FindIngestTokenAsync(
        TokenIdentifier identifier, CancellationToken cancellationToken) =>
        context.IngestTokens.SingleOrDefaultAsync(
            t => t.Identifier == identifier, cancellationToken);

    public Task<AgentToken?> FindAgentTokenAsync(
        TokenIdentifier identifier, CancellationToken cancellationToken) =>
        context.AgentTokens.SingleOrDefaultAsync(
            t => t.Identifier == identifier, cancellationToken);

    public Task<HostToken?> FindHostTokenAsync(
        TokenIdentifier identifier, CancellationToken cancellationToken) =>
        context.HostTokens.SingleOrDefaultAsync(
            t => t.Identifier == identifier, cancellationToken);

    public Task<IngestToken?> FindIngestTokenAsync(Guid id, CancellationToken cancellationToken) =>
        context.IngestTokens.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<AgentToken?> FindAgentTokenAsync(Guid id, CancellationToken cancellationToken) =>
        context.AgentTokens.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<HostToken?> FindHostTokenAsync(Guid id, CancellationToken cancellationToken) =>
        context.HostTokens.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

    // The sealed secret is not among the columns selected, here or below: what
    // unseals one is a lookup by identity, and a listing is the operator reading
    // their settings. The projection is what keeps that true in the statement
    // and not only in what the act hands back.
    public async Task<IReadOnlyList<HeldToken>> ListIngestTokensAsync(
        Guid projectId, CancellationToken cancellationToken) =>
        await context.IngestTokens
            .Where(t => t.ProjectId == projectId)
            .OrderBy(t => t.IssuedAt)
            .Select(t => new HeldToken(t.Id, t.Identifier, t.IssuedAt, t.LastUsedAt))
            .ToListAsync(cancellationToken);

    // One statement over the whole ingest-token table rather than one read per
    // project, and there is no index to narrow it by because nothing is being
    // narrowed: the table holds two rows per project at the most
    // (IngestToken.MaximumPerProject), so every row of it is what was asked for.
    // The grouping is done here rather than in the database because a grouped
    // statement would hand back the same rows for the reader to take apart
    // anyway.
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<HeldToken>>>
        ListIngestTokensAsync(CancellationToken cancellationToken)
    {
        var rows = await context.IngestTokens
            .OrderBy(t => t.IssuedAt)
            .Select(t => new
            {
                t.ProjectId,
                Held = new HeldToken(t.Id, t.Identifier, t.IssuedAt, t.LastUsedAt),
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.ProjectId)
            .ToDictionary(
                project => project.Key,
                IReadOnlyList<HeldToken> (project) => [.. project.Select(row => row.Held)]);
    }

    public async Task<IReadOnlyList<HeldToken>> ListHostTokensAsync(
        Guid hostId, CancellationToken cancellationToken) =>
        await context.HostTokens
            .Where(t => t.HostId == hostId)
            .OrderBy(t => t.IssuedAt)
            .Select(t => new HeldToken(t.Id, t.Identifier, t.IssuedAt, t.LastUsedAt))
            .ToListAsync(cancellationToken);

    // One statement over the whole host-token table, for the reason the ingest
    // listing above is one.
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<HeldToken>>>
        ListHostTokensAsync(CancellationToken cancellationToken)
    {
        var rows = await context.HostTokens
            .OrderBy(t => t.IssuedAt)
            .Select(t => new
            {
                t.HostId,
                Held = new HeldToken(t.Id, t.Identifier, t.IssuedAt, t.LastUsedAt),
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.HostId)
            .ToDictionary(
                host => host.Key,
                IReadOnlyList<HeldToken> (host) => [.. host.Select(row => row.Held)]);
    }

    public async Task<IReadOnlyList<AgentToken>> ListAgentTokensAsync(
        CancellationToken cancellationToken) =>
        await context.AgentTokens.OrderBy(t => t.IssuedAt).ToListAsync(cancellationToken);

    public async Task AddAsync(IngestToken token, CancellationToken cancellationToken)
    {
        context.IngestTokens.Add(token);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task AddAsync(AgentToken token, CancellationToken cancellationToken)
    {
        context.AgentTokens.Add(token);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task AddAsync(HostToken token, CancellationToken cancellationToken)
    {
        context.HostTokens.Add(token);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(IngestToken token, CancellationToken cancellationToken)
    {
        context.IngestTokens.Remove(token);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(AgentToken token, CancellationToken cancellationToken)
    {
        context.AgentTokens.Remove(token);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(HostToken token, CancellationToken cancellationToken)
    {
        context.HostTokens.Remove(token);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordRenameAsync(AgentToken token, CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);

    public async Task RecordUseAsync(IngestToken token, CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);

    public async Task RecordUseAsync(AgentToken token, CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);

    public async Task RecordUseAsync(HostToken token, CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);
}
