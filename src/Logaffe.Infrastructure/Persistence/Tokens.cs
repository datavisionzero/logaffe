using Logaffe.Application.Ports;
using Logaffe.Domain.Tokens;
using Microsoft.EntityFrameworkCore;

namespace Logaffe.Infrastructure.Persistence;

/// <summary>
/// The token rows, out of the two tables that hold them.
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

    public Task<IngestToken?> FindIngestTokenAsync(Guid id, CancellationToken cancellationToken) =>
        context.IngestTokens.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<AgentToken?> FindAgentTokenAsync(Guid id, CancellationToken cancellationToken) =>
        context.AgentTokens.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<IngestToken>> ListIngestTokensAsync(
        Guid projectId, CancellationToken cancellationToken) =>
        await context.IngestTokens
            .Where(t => t.ProjectId == projectId)
            .OrderBy(t => t.IssuedAt)
            .ToListAsync(cancellationToken);

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

    public async Task RecordRenameAsync(AgentToken token, CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);

    public async Task RecordUseAsync(IngestToken token, CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);

    public async Task RecordUseAsync(AgentToken token, CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);
}
