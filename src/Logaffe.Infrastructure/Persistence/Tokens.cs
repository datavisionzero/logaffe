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

    public async Task RecordUseAsync(IngestToken token, CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);

    public async Task RecordUseAsync(AgentToken token, CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);
}
