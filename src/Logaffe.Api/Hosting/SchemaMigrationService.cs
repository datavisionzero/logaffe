using Logaffe.Infrastructure.Persistence;

namespace Logaffe.Api.Hosting;

/// <summary>
/// Runs the migrations before the installation serves anything.
/// </summary>
/// <remarks>
/// A failed migration stops the installation: it does not start half-migrated
/// and it does not serve requests. Throwing out of <see cref="StartAsync"/> is
/// exactly that — the host does not come up, and the failure is in logaffe's own
/// log, which is where every other failure of this kind is already written
/// (ADR 0002).
/// </remarks>
public sealed class SchemaMigrationService(
    IServiceScopeFactory scopeFactory,
    ILogger<SchemaMigrationService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var migrator = scope.ServiceProvider.GetRequiredService<SchemaMigrator>();

        try
        {
            await migrator.ApplyAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "Migration failed; the installation will not start.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
