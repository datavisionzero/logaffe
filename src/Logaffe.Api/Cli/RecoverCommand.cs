using Logaffe.Api.Hosting;
using Logaffe.Application.Operations;
using Logaffe.Application.Ports;
using Logaffe.Domain.Identities;
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
/// Removes every identity on the installation — every user and every agent
/// token — keeping its projects, groups, ingest tokens, settings and entries
/// (ADR 0058). It is the only route back into an installation nobody can sign in
/// to, and every use is written to logaffe's own file log, which is the one
/// place a record of it survives the removal it performs (ADR 0002).
/// </para>
/// <para>
/// <b>It asks first, and it has more to ask about than it used to.</b> Somebody
/// reading the command name will expect the smaller thing — a password reset —
/// and this removes not one account but all of them, so the product says plainly
/// what it does before it does it. <c>--yes</c> answers the question for a caller
/// that has no terminal to answer it from.
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
        // Everything this needs — the connection string and the volume — comes
        // from the environment and the settings files beside the binary, read
        // the way the server reads them (see HostConfiguration).
        var builder = Host.CreateApplicationBuilder(HostConfiguration.ForAVerb());

        var volumePath = HostConfiguration.VolumePath(builder.Configuration);

        builder.Services.AddLogaffeInfrastructure(builder.Configuration);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<Recover>();

        // Whoever ran a command wants two lines back, not the SQL it took to get
        // there. What is worth keeping goes to the file log below, which is where
        // a record of this has to survive anyway (ADR 0002).
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
                "Host Recovery removed every identity on this installation. "
                + "{Identities} users and agents are gone, along with their sessions, "
                + "backup codes and project assignments, and {AgentTokens} agent tokens "
                + "were removed with them. Projects, groups, ingest tokens, settings and "
                + "entries are untouched. The way back in is the bootstrap from "
                + "configuration.",
                recovered.IdentitiesRemoved,
                recovered.AgentTokensRemoved);

            Console.WriteLine(
                recovered.IdentitiesRemoved == 0
                    ? "There were no identities; this installation was already unreachable."
                    : recovered.IdentitiesRemoved == 1
                        ? "The one identity is gone, along with its sessions, backup codes "
                        + "and project assignments."
                        : $"All {recovered.IdentitiesRemoved} identities are gone, along "
                        + "with their sessions, backup codes and project assignments.");

            // Said as its own line and with the number in it, because it is the
            // one consequence of this command that leaves work behind: each of
            // these is a client configuration to go and replace. What each of
            // them stops doing is not said, because the count covers both kinds
            // and whoever issued them knows which (ADR 0046).
            if (recovered.AgentTokensRemoved > 0)
            {
                Console.WriteLine(
                    recovered.AgentTokensRemoved == 1
                        ? "One agent token went with them, and that agent can do nothing "
                        + "here until it is given a new one."
                        : $"{recovered.AgentTokensRemoved} agent tokens went with them, and "
                        + "those agents can do nothing here until they are given new ones.");
            }

            Console.WriteLine(
                "\nSet Logaffe__Bootstrap__Administrator, Logaffe__Bootstrap__Email and "
                + "Logaffe__Bootstrap__Token\nin the compose file and start the "
                + "installation again. It will create the first\nadministrator from them, "
                + "and the token is exchanged once in the browser for a\npassword. See "
                + "docs/setup.md.");

            return 0;
        }
        catch (Exception exception)
        {
            // A database that cannot be reached, most likely. Whoever ran this is
            // at the keyboard of a container they own, so they get the sentence
            // and the log file gets the rest.
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
            This does not reset a password, and it does not pick an account.

            It removes every user on this installation and every agent token — both
            kinds — so everybody signed in is signed out and every agent connected here
            stops, whether it was reading entries or working the settings, until it is
            given a new one. Sessions, backup codes, second factors and project
            assignments go with the accounts. Projects, groups, ingest tokens, settings
            and log entries are untouched — the installation loses its people, it does
            not lose what it holds, and an application shipping logs through it does not
            notice.

            The way back in afterwards is the bootstrap from configuration: the first
            administrator is created from the environment on the next start.
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
