using Logaffe.Application.Operations;
using Logaffe.Infrastructure.Secrets;

namespace Logaffe.Api.Hosting;

/// <summary>
/// Refuses to start an installation whose key does not open what it holds.
/// </summary>
/// <remarks>
/// It runs after <see cref="SchemaMigrationService"/> — registration order is
/// start order — because the tables it reads may be the ones a migration is
/// about to create. Refusing here is the same move a failed migration makes: the
/// host does not come up, and the reason is in logaffe's own log (ADR 0002).
/// Starting anyway is the worse failure, because an installation that serves
/// with the wrong key answers every delivery with a 401 that looks exactly like
/// a token the operator got wrong.
/// </remarks>
public sealed class KeyFitsService(
    IServiceScopeFactory scopeFactory,
    HostVolumeKey key,
    ILogger<KeyFitsService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Made to exist here rather than the first time a token is issued, so
        // that a volume which cannot be written to fails the start instead of
        // the operator's first project, and so that the backup an operator takes
        // on day one already has both halves in it (ADR 0024).
        _ = key.Material;

        await using var scope = scopeFactory.CreateAsyncScope();
        var check = scope.ServiceProvider.GetRequiredService<CheckTheKeyFits>();

        switch (await check.ExecuteAsync(cancellationToken))
        {
            case KeyFit.DoesNotFit:
                logger.LogCritical(
                    "The encryption key on the host volume does not open the secrets in "
                    + "this database, so the two are not halves of one installation. "
                    + "Restore the backup that holds both, or put the original volume "
                    + "back. The installation will not start.");
                throw new InvalidOperationException(
                    "The encryption key on the host volume does not open the secrets in "
                    + "this database.");

            case KeyFit.NothingSealed:
                // The ordinary first start, and nothing to say about it.
                break;

            case KeyFit.Fits:
                logger.LogInformation("The encryption key opens what this installation holds.");
                break;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
