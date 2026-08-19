namespace Logaffe.Api.Hosting;

/// <summary>
/// Says it once, at the start, when logaffe's own log cannot be written.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0002 puts the record of everything worth diagnosing in a file on the host
/// volume, and the command line says as much out loud: a backup or a Host
/// Recovery that did not finish tells the operator that the whole of it is in
/// that log. A volume that cannot be written to makes both of those sentences
/// point at nothing.
/// </para>
/// <para>
/// <b>It starts anyway.</b> Refusing would be the other defensible answer, and
/// it is the wrong one here: an installation that cannot write its own log can
/// still take deliveries, still answer the operator, and still be fixed from the
/// screen this complaint is on — while refusing to start would take the
/// installation down over its diary. What is not defensible is the silence
/// before this, which is what the same volume used to buy.
/// </para>
/// </remarks>
public sealed class FileLogService(IConfiguration configuration, ILogger<FileLogService> logger)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var volumePath = HostConfiguration.VolumePath(configuration);

        if (FileLog.WhyItCannotBeWritten(volumePath) is { } why)
        {
            logger.LogCritical(
                "logaffe's own log cannot be written under {VolumePath}: {Reason} Nothing "
                + "this installation has to say about itself is being kept, and a backup "
                + "or a Host Recovery that fails will have nowhere to leave the detail. "
                + "The installation is starting anyway.",
                volumePath,
                why);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
