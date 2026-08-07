using Logaffe.Application.Operations;

namespace Logaffe.Api.Hosting;

/// <summary>
/// Writes the installation's first run, and says in the container log whether
/// anyone can still claim it.
/// </summary>
/// <remarks>
/// <para>
/// It runs after <see cref="KeyFitsService"/> — registration order is start
/// order — so that an installation which is about to refuse to start does not
/// first arm a window it will never serve. An operator who fixes the key an hour
/// later gets their thirty minutes from the start that works.
/// </para>
/// <para>
/// The log line is the point of doing this out loud. An operator bringing an
/// installation up for the first time is reading <c>docker compose logs</c>, and
/// the deadline they are racing is a fact about their next half hour.
/// </para>
/// </remarks>
public sealed class ClaimWindowService(
    IServiceScopeFactory scopeFactory,
    ILogger<ClaimWindowService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        await scope.ServiceProvider
            .GetRequiredService<OpenTheClaimWindow>()
            .ExecuteAsync(cancellationToken);

        var state = await scope.ServiceProvider
            .GetRequiredService<CheckTheClaim>()
            .ExecuteAsync(cancellationToken);

        if (state.IsClaimed)
        {
            // Nothing to say. This is every start of an installation in
            // ordinary use.
            return;
        }

        if (state.WindowIsOpen)
        {
            logger.LogWarning(
                "This installation belongs to nobody and can be claimed by anyone who "
                + "can reach it until {ClosesAt:u}. Claim it now.",
                state.ClosesAt);
        }
        else
        {
            logger.LogWarning(
                "This installation belongs to nobody and its claim window has closed, so "
                + "it cannot be claimed over the network. Run `logaffe recover` in this "
                + "container to open a fresh one.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
