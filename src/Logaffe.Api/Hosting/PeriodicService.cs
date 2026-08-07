namespace Logaffe.Api.Hosting;

/// <summary>
/// A job that runs on a fixed interval for as long as the installation is up.
/// </summary>
/// <remarks>
/// <para>
/// It is a base rather than one service running every job, because the jobs this
/// product has are not the same shape: sweeping expired sessions is one
/// statement a day, and the retention sweep of <c>docs/operations.md</c> deletes
/// in bounded portions over the largest table in the database and paces itself
/// against it. One service for both would make the interval of one the interval
/// of the other, and would put the two in the same failure.
/// </para>
/// <para>
/// <b>A pass that throws does not end the job.</b> A background sweep is not
/// worth the installation over, and the next interval is a retry: what a failed
/// pass leaves behind is rows that live a day longer than they had to, which is
/// what would have happened anyway had the installation been down. It is logged
/// as an error, into the file log rather than into logaffe itself (ADR 0002).
/// </para>
/// <para>
/// The first pass happens on start, so an installation brought up after a long
/// stop cleans up without waiting a full interval for permission.
/// </para>
/// </remarks>
public abstract class PeriodicService(
    IServiceScopeFactory scopeFactory,
    ILogger logger,
    TimeProvider clock) : BackgroundService
{
    /// <summary>How long between passes.</summary>
    protected abstract TimeSpan Interval { get; }

    /// <summary>What the job is called in the one log line it ever writes.</summary>
    protected abstract string Name { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);

        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    /// <summary>
    /// One pass, resolved in a scope of its own because the acts and everything
    /// under them are scoped to a unit of work and this service is not.
    /// </summary>
    protected abstract Task RunOnceAsync(
        IServiceProvider services, CancellationToken cancellationToken);

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();

            await RunOnceAsync(scope.ServiceProvider, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The installation is stopping, which is not a failed pass.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The {Job} pass failed and will run again.", Name);
        }
    }

    private static async Task<bool> WaitAsync(
        PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
