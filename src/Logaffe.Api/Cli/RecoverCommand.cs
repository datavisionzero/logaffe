using Logaffe.Api.Hosting;
using Logaffe.Application.Operations;
using Logaffe.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Logaffe.Api.Cli;

/// <summary>
/// <c>docker compose exec logaffe logaffe recover</c>
/// </summary>
/// <remarks>
/// <para>
/// Returns the installation to unclaimed and arms a fresh claim window, keeping
/// its projects, tokens and entries (ADR 0013). It is the only route back into a
/// claimed installation, and every use is written to logaffe's own file log,
/// which is the one place a record of it survives the reset it performs
/// (ADR 0002).
/// </para>
/// <para>
/// <b>It asks first.</b> Somebody reading the command name will expect the
/// smaller thing — a password reset — and this removes the account, so the
/// product says plainly what it does before it does it. <c>--yes</c> answers the
/// question for a caller that has no terminal to answer it from.
/// </para>
/// <para>
/// It builds its own host rather than the web one: there is no server here, no
/// endpoint that reaches this, and nothing listening on a port while it runs.
/// That is the whole of its security property.
/// </para>
/// </remarks>
public static class RecoverCommand
{
    /// <summary>What the operator types to say they meant it.</summary>
    private const string Confirmation = "recover";

    /// <summary>Nothing ran, because the operator said no.</summary>
    private const int Declined = 1;

    /// <summary>It ran and did not finish.</summary>
    private const int Failed = 2;

    public static async Task<int> RunAsync(string[] args)
    {
        // Without args: a bare verb is not a configuration argument, and the
        // command line provider refuses one. Everything this needs — the
        // connection string and the volume — comes from the environment and the
        // settings file beside the binary.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory,
        });

        var volumePath = builder.Configuration["Logaffe:VolumePath"]
            ?? throw new InvalidOperationException("Logaffe:VolumePath is not configured.");

        builder.Services.AddLogaffeInfrastructure(builder.Configuration);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<Recover>();

        // The operator ran a command and wants two lines back, not the SQL it
        // took to get there. What is worth keeping goes to the file log below,
        // which is where a record of this has to survive anyway (ADR 0002).
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

            var recovered = await scope.ServiceProvider
                .GetRequiredService<Recover>()
                .ExecuteAsync(CancellationToken.None);

            // Written before anything is said on the terminal, because the
            // terminal is not where this has to survive.
            log.Warning(
                "Host Recovery returned this installation to unclaimed. There "
                + "{ThereWasAnOperator} an operator account, and it is gone along with its "
                + "sessions and backup codes. Projects, tokens and entries are untouched. "
                + "The installation can be claimed by anyone who can reach it until "
                + "{ClosesAt:u}.",
                recovered.ThereWasAnOperator ? "was" : "was no",
                recovered.Window.ClosesAt);

            Console.WriteLine(
                recovered.ThereWasAnOperator
                    ? "The operator account is gone, along with its sessions and backup codes."
                    : "There was no operator account; this installation was already unclaimed.");

            Console.WriteLine(
                $"Anyone who can reach this installation can claim it until "
                + $"{recovered.Window.ClosesAt:u}. Claim it now.");

            return 0;
        }
        catch (Exception exception)
        {
            // A database that cannot be reached, most likely. The operator is at
            // the keyboard of a container they own, so they get the sentence and
            // the log file gets the rest.
            log.Error(exception, "Host Recovery did not finish.");

            Console.Error.WriteLine(
                $"\nHost Recovery did not finish: {exception.Message}\n"
                + $"Nothing may have changed, and running it again is safe. The whole of "
                + $"it is in logaffe's own log, under {volumePath}.");

            return Failed;
        }
    }

    /// <summary>
    /// Says what this does and waits to be told to do it.
    /// </summary>
    /// <remarks>
    /// A caller with no terminal — a script, a CI step — has to pass
    /// <c>--yes</c>, because a prompt nobody can answer would hang the container
    /// rather than protect anything.
    /// </remarks>
    private static bool Agreed(string[] args)
    {
        Console.Error.WriteLine(
            """
            This does not reset a password.

            It removes the operator account. The sessions and the backup codes go with
            it. Projects, ingest tokens and log entries are untouched — the installation
            changes hands, it does not lose what it holds.

            The installation then belongs to nobody for the next 30 minutes, and anyone
            who can reach it in that time can claim it.
            """);

        if (args.Contains("--yes") || args.Contains("-y"))
        {
            return true;
        }

        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine(
                "\nThere is no terminal to confirm from. Pass --yes if this is what you "
                + "meant.");

            return false;
        }

        Console.Error.Write($"\nType `{Confirmation}` to continue: ");

        if (Console.ReadLine()?.Trim() == Confirmation)
        {
            return true;
        }

        Console.Error.WriteLine("Nothing was changed.");

        return false;
    }
}
