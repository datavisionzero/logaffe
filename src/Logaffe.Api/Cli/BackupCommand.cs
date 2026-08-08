using Logaffe.Api.Hosting;
using Logaffe.Application.Operations;
using Logaffe.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Logaffe.Api.Cli;

/// <summary>
/// <c>docker compose exec logaffe logaffe backup &gt; logaffe-backup.tar</c>
/// </summary>
/// <remarks>
/// <para>
/// The artifact has to contain <em>both</em> halves — the database and the key
/// material on the host volume — because neither is useful without the other,
/// and an operator who backs up one and believes they are covered discovers it
/// at the moment they go looking for a token (ADR 0024).
/// </para>
/// <para>
/// It is safe beside a serving installation, which is why the documented form is
/// an <c>exec</c>: it reads, it takes no lock, and an entry arriving while it
/// runs is simply an entry the artifact does not have. A restore is the opposite
/// and runs in a container of its own.
/// </para>
/// <para>
/// <b>Everything human goes to stderr.</b> stdout is the artifact, so a sentence
/// written to the wrong stream would be a sentence inside the tar.
/// </para>
/// </remarks>
public static class BackupCommand
{
    /// <summary>Nothing was written.</summary>
    private const int Failed = 2;

    /// <summary>
    /// Entries are expendable — short lived by design and additive to the
    /// applications' own files — while the account, the configuration and the
    /// tokens are not. An operator who keeps only the small, slow-changing half
    /// is making a legitimate choice (<c>docs/operations.md</c>), so the command
    /// supports it rather than making them explain it afterwards.
    /// </summary>
    private const string WithoutEntries = "--without-entries";

    public static async Task<int> RunAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(HostConfiguration.ForAVerb());
        var volumePath = HostConfiguration.VolumePath(builder.Configuration);

        builder.Services.AddLogaffeInfrastructure(builder.Configuration);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<TakeABackup>();

        builder.Logging.ClearProviders();

        using var log = new LoggerConfiguration()
            .WriteTo.WriteToLogaffeFile(volumePath)
            .CreateLogger();

        // A tar on a terminal is a screen of control characters and a shell the
        // operator has to reset. They forgot the redirect; saying so costs
        // nothing and running anyway costs them the session.
        if (!Console.IsOutputRedirected)
        {
            Console.Error.WriteLine(
                "This writes a tar to standard output. Redirect it somewhere:\n"
                + "\n    docker compose exec logaffe logaffe backup > logaffe-backup.tar\n");

            return Failed;
        }

        var withEntries = !args.Contains(WithoutEntries);

        try
        {
            using var host = builder.Build();
            await using var scope = host.Services.CreateAsyncScope();
            await using var artifact = Console.OpenStandardOutput();

            var manifest = await scope.ServiceProvider
                .GetRequiredService<TakeABackup>()
                .ExecuteAsync(artifact, Build.Version, withEntries, CancellationToken.None);

            log.Information(
                "Wrote a backup of this installation, taken at {TakenAt:u} against "
                + "migration {Migration}, {WithEntries} the log entries.",
                manifest.TakenAt,
                manifest.Migration,
                manifest.Entries ? "with" : "without");

            Console.Error.WriteLine(
                $"Wrote a backup of this installation: logaffe {manifest.Logaffe}, "
                + $"schema {manifest.Migration}, {manifest.Tables.Count} table(s), "
                + (manifest.Entries
                    ? "log entries included."
                    : "log entries left out.")
                + "\nIt holds the key material, so it is as sensitive as the "
                + "installation itself.");

            return 0;
        }
        catch (Exception exception)
        {
            log.Error(exception, "The backup did not finish.");

            Console.Error.WriteLine(
                $"\nThe backup did not finish: {exception.Message}\n"
                + "Whatever reached standard output is not a backup and should be "
                + $"thrown away. The whole of it is in logaffe's own log, under "
                + $"{volumePath}.");

            return Failed;
        }
    }
}
