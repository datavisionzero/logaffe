using Logaffe.Api.Hosting;
using Logaffe.Application.Operations;
using Logaffe.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Logaffe.Api.Cli;

/// <summary>
/// <c>docker compose run --rm logaffe logaffe restore &lt; logaffe-backup.tar</c>
/// </summary>
/// <remarks>
/// <para>
/// <b><c>run</c>, not <c>exec</c>.</b> <c>backup</c> is safe beside a serving
/// installation and stays an <c>exec</c>; a restore is not. Taking the migrator's
/// advisory lock would guard against another migration and not against the
/// server reading and writing through the replay, so the answer is structural: a
/// one-off container while the serving one is down makes the dangerous case
/// impossible rather than merely unlikely.
/// </para>
/// <para>
/// <b>It replaces what is here</b> (ADR 0024), and says so before it does it.
/// The gate is <c>recover</c>'s, with one difference it cannot avoid: standard
/// input is the artifact, so there is no terminal to answer a question from and
/// <c>--yes</c> is the only way to say yes.
/// </para>
/// </remarks>
public static class RestoreCommand
{
    /// <summary>Nothing ran, because the operator did not say to.</summary>
    private const int Declined = 1;

    /// <summary>It ran and did not finish.</summary>
    private const int Failed = 2;

    public static async Task<int> RunAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(HostConfiguration.ForAVerb());
        var volumePath = HostConfiguration.VolumePath(builder.Configuration);

        builder.Services.AddLogaffeInfrastructure(builder.Configuration);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<RestoreABackup>();

        builder.Logging.ClearProviders();

        using var log = new LoggerConfiguration()
            .WriteTo.WriteToLogaffeFile(volumePath)
            .CreateLogger();

        if (!Agreed(args))
        {
            return Declined;
        }

        try
        {
            using var host = builder.Build();
            await using var scope = host.Services.CreateAsyncScope();
            await using var artifact = Console.OpenStandardInput();

            var restored = await scope.ServiceProvider
                .GetRequiredService<RestoreABackup>()
                .ExecuteAsync(artifact, CancellationToken.None);

            log.Warning(
                "Restored this installation from an artifact taken by logaffe "
                + "{Logaffe} at {TakenAt:u}, schema {Migration}: {Files} file(s) onto "
                + "the volume and {Tables} table(s) replayed. Whatever was here before "
                + "is gone.",
                restored.Manifest.Logaffe,
                restored.Manifest.TakenAt,
                restored.Manifest.Migration,
                restored.Files,
                restored.Tables);

            Console.Error.WriteLine(
                $"\nRestored from a backup taken by logaffe {restored.Manifest.Logaffe} "
                + $"at {restored.Manifest.TakenAt:u}: {restored.Files} file(s) onto the "
                + $"volume, {restored.Tables} table(s) replayed"
                + (restored.Manifest.Entries
                    ? "."
                    : ", without the log entries — the artifact was taken without them.")
                + "\nStart the installation. It will migrate the schema the rest of the "
                + "way if this logaffe is newer than the one that took the artifact.");

            return 0;
        }
        catch (ArtifactRefusedException refusal)
        {
            log.Error("The artifact was refused. {Reason}", refusal.Message);

            Console.Error.WriteLine($"\n{refusal.Message}");

            return Failed;
        }
        catch (Exception exception)
        {
            log.Error(exception, "The restore did not finish.");

            Console.Error.WriteLine(
                $"\nThe restore did not finish: {exception.Message}\n"
                + "This installation is in whatever state the restore reached, which "
                + "is not one to serve from. Run it again against the same artifact. "
                + $"The whole of it is in logaffe's own log, under {volumePath}.");

            return Failed;
        }
    }

    /// <summary>
    /// Says what this does and is told to do it.
    /// </summary>
    /// <remarks>
    /// <c>recover</c> can offer a prompt because its standard input is free.
    /// Here it is the artifact — so the sentence is printed either way and
    /// <c>--yes</c> is what answers it. A terminal on standard input means there
    /// is no artifact at all, which is worth its own sentence rather than a
    /// failure ten lines later.
    /// </remarks>
    private static bool Agreed(string[] args)
    {
        Console.Error.WriteLine(
            """
            This replaces the installation in front of it.

            The database is dropped and rebuilt from the artifact, and the artifact's
            key material is written over the volume's. Whatever this installation holds
            now — its operator, its projects, its tokens and its entries — is gone, and
            what the artifact holds takes its place.

            Nothing here is a server: run this while the serving container is down.
            """);

        if (!Console.IsInputRedirected)
        {
            Console.Error.WriteLine(
                "\nThere is no artifact on standard input. Point one at it:\n"
                + "\n    docker compose run --rm logaffe logaffe restore --yes "
                + "< logaffe-backup.tar\n");

            return false;
        }

        if (args.Contains("--yes") || args.Contains("-y"))
        {
            return true;
        }

        Console.Error.WriteLine(
            "\nStandard input is the artifact, so there is no terminal to confirm "
            + "from. Pass --yes if this is what you meant. Nothing was changed.");

        return false;
    }
}
