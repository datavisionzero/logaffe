using Logaffe.Api.Hosting;
using Logaffe.Application.Operations;
using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;
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
/// its projects, ingest tokens and entries (ADR 0013). It is the only route back
/// into a claimed installation, and every use is written to logaffe's own file
/// log, which is the one place a record of it survives the reset it performs
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
        // Everything this needs — the connection string and the volume — comes
        // from the environment and the settings files beside the binary, read
        // the way the server reads them (see HostConfiguration).
        var builder = Host.CreateApplicationBuilder(HostConfiguration.ForAVerb());

        var volumePath = HostConfiguration.VolumePath(builder.Configuration);

        // Read here as the server reads it, because this command opens whichever
        // door the installation is configured for and a verb that read it
        // differently would open the other one (ADR 0040). A refusal is said
        // plainly: the operator is at a keyboard and the fix is in a file they
        // have open.
        ClaimSettings claim;
        try
        {
            claim = HostConfiguration.Claim(builder.Configuration);
        }
        catch (InvalidOperationException cause)
        {
            Console.Error.WriteLine($"\n{cause.Message}");

            return Failed;
        }

        builder.Services.AddLogaffeInfrastructure(builder.Configuration);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(claim);
        builder.Services.AddScoped<Recover>();

        // The operator ran a command and wants two lines back, not the SQL it
        // took to get there. What is worth keeping goes to the file log below,
        // which is where a record of this has to survive anyway (ADR 0002).
        builder.Logging.ClearProviders();

        using var log = new LoggerConfiguration()
            .WriteTo.WriteToLogaffeFile(volumePath)
            .CreateLogger();

        if (!Agreed(args, claim.Mode))
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

            var handoverPath = scope.ServiceProvider
                .GetRequiredService<IClaimSecretHandover>()
                .Path;

            // Written before anything is said on the terminal, because the
            // terminal is not where this has to survive. The secret itself is
            // not written here: the file log is the one place a record of this
            // command survives, and a record is not a place for a live
            // credential.
            log.Warning(
                "Host Recovery returned this installation to unclaimed. There "
                + "{ThereWasAnOperator} an operator account, and it is gone along with its "
                + "sessions and backup codes. {AgentTokens} agent tokens were removed with "
                + "it. Projects, ingest tokens and entries are untouched. {HowItIsGuarded}",
                recovered.ThereWasAnOperator ? "was" : "was no",
                recovered.AgentTokensRemoved,
                recovered.DrawnSecret is not null
                    ? "A fresh claim secret was drawn and printed on the terminal."
                    : claim.Mode is ClaimMode.Secret
                        ? "It is claimable by whoever presents the claim secret the "
                        + "configuration names."
                        : "It can be claimed by anyone who can reach it until "
                        + $"{recovered.Guard.WindowClosesAt:u}.");

            Console.WriteLine(
                recovered.ThereWasAnOperator
                    ? "The operator account is gone, along with its sessions and backup codes."
                    : "There was no operator account; this installation was already unclaimed.");

            // Said as its own line and with the number in it, because it is the
            // one consequence of this command that leaves work behind: each of
            // these is a client configuration to go and replace.
            if (recovered.AgentTokensRemoved > 0)
            {
                Console.WriteLine(
                    recovered.AgentTokensRemoved == 1
                        ? "Its one agent token went with it, and that agent reads nothing "
                        + "until it is given a new one."
                        : $"Its {recovered.AgentTokensRemoved} agent tokens went with it, and "
                        + "those agents read nothing until they are given new ones.");
            }

            if (recovered.DrawnSecret is not null)
            {
                // The one moment this value is ever handed over. The operator
                // running this command is at the keyboard, which is the whole
                // reason it can be said out loud here and nowhere else.
                Console.WriteLine(
                    $"\nThis installation is claimed by presenting its claim secret, and a "
                    + $"fresh one has been drawn:\n\n    {recovered.DrawnSecret.Text}\n\n"
                    + $"It is also in {handoverPath}, and the previous one no longer opens "
                    + "anything. There is no deadline.");
            }
            else if (claim.Mode is ClaimMode.Secret)
            {
                Console.WriteLine(
                    "\nThis installation is claimed by presenting the claim secret its "
                    + "configuration names, which this command does not change. There is no "
                    + "deadline.");
            }
            else
            {
                Console.WriteLine(
                    $"\nAnyone who can reach this installation can claim it until "
                    + $"{recovered.Guard.WindowClosesAt:u}. Claim it now.");
            }

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
    private static bool Agreed(string[] args, ClaimMode mode)
    {
        Console.Error.WriteLine(
            """
            This does not reset a password.

            It removes the operator account. The sessions, the backup codes and the
            agent tokens go with it, so every agent connected to this installation
            stops reading until it is given a new one. Projects, ingest tokens and log
            entries are untouched — the installation changes hands, it does not lose
            what it holds, and an application shipping logs through it does not
            notice.
            """);

        // Which door this is about to open, said before it is opened, because the
        // two are a different thing to agree to: one of them is a deadline the
        // operator is about to start racing.
        Console.Error.WriteLine(
            mode is ClaimMode.Secret
                ? "\nThe installation then belongs to nobody, and whoever holds its claim\n"
                + "secret can claim it. There is no deadline."
                : "\nThe installation then belongs to nobody for the next 30 minutes, and "
                + "anyone\nwho can reach it in that time can claim it.");

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
