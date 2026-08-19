using System.Runtime.InteropServices;
using Logaffe.Collector;

// A collector reads a machine and posts numbers, once a minute, and does
// nothing else (ADR 0043). This is the whole of it: read the three settings,
// take a reading, post it, wait.
//
// It holds no state that outlives the process, opens no port and takes no
// inbound connection. There is nothing to back up before an upgrade, and
// nothing a restart loses beyond the minute it was in.

if (!CollectorSettings.TryRead(Environment.GetEnvironmentVariable, out var settings, out var reason))
{
    Say.Line(reason);

    // Exiting rather than carrying on. A revoked token is a thing that fixes
    // itself and is logged once (`Installation`); a command that was pasted
    // wrong is not, and a container that keeps restarting is the signal
    // `docker ps` shows an operator who is looking for one.
    return 1;
}

var machine = new ProcMachine(settings.ProcPath);
var filesystems = new MountedFilesystems(settings.RootPath, settings.Mounts);

using var stopping = new CancellationTokenSource();

// SIGTERM is what `docker stop` sends and the only shutdown that matters here;
// SIGINT is what a person running this in a terminal sends. Both end the
// current wait rather than the process, so a delivery in flight is either
// finished or abandoned deliberately.
using var term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stop);
using var quit = PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop);

void Stop(PosixSignalContext context)
{
    context.Cancel = true;
    stopping.Cancel();
}

// The processor is a share of an interval, so there is no such thing as one
// reading of it: this is the first of the two the first share is a difference
// between. It is also the check that the `/proc` mount is there at all, which
// is the misconfiguration worth failing on rather than reporting zeros for.
try
{
    _ = machine.Read();
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
    or FormatException)
{
    Say.Line(
        $"The machine cannot be read at {settings.ProcPath}: {exception.Message} "
        + $"Set {CollectorSettings.ProcVariable}, or check that the host's /proc is mounted "
        + $"at {CollectorSettings.ProcByDefault}.");

    return 1;
}

Say.Line(
    $"Reporting to {settings.Endpoint} every {Sampling.Interval.TotalSeconds:0} seconds"
    + $"{(settings.Mounts.Count == 0 ? ", watching no filesystem" : $", watching {string.Join(", ", settings.Mounts)}")}.");

using var client = Installation.Client();
var installation = new Installation(client, settings.Endpoint, settings.Token);

// A second, not a minute, before the first one. The check that a collector
// worked is the host reporting in the installation's settings
// (`docs/deployment.md`), and an operator who has just pasted a command should
// not have to wait a minute to be told whether they pasted it right. Every
// share after this one covers the minute before it.
try
{
    await Task.Delay(TimeSpan.FromSeconds(1), stopping.Token);

    await ReportAsync();

    using var timer = new PeriodicTimer(Sampling.Interval);

    while (await timer.WaitForNextTickAsync(stopping.Token))
    {
        await ReportAsync();
    }
}
catch (OperationCanceledException)
{
    // The signal, and the ordinary end of this program.
}

Say.Line("Stopped.");

return 0;

async Task ReportAsync()
{
    MachineReading? read;

    try
    {
        read = machine.Read();
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
        or FormatException)
    {
        // It was readable a moment ago, so this is not a misconfiguration and
        // not something to exit over. The minute is a gap, and the band draws
        // it as one.
        Say.Line($"This reading could not be taken: {exception.Message}");
        return;
    }

    // Never, after the priming read above: it is here because the first reading
    // of a counter since boot is not a share of anything, and that answer has
    // to exist for the one call that gets it.
    if (read is null)
    {
        return;
    }

    // A delivery is refused whole or taken whole, so the filesystems are read
    // beside the machine rather than posted separately: half a sample is a band
    // with a hole in it that looks like data (`docs/metrics.md`).
    await installation.DeliverAsync(
        new Reading(
            read.Cpu,
            read.MemoryUsed,
            read.MemoryTotal,
            read.Load1,
            read.Load5,
            read.Load15,
            filesystems.Read()),
        stopping.Token);
}
