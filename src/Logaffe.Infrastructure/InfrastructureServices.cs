using Logaffe.Application.Ports;
using Logaffe.Infrastructure.Alerts;
using Logaffe.Infrastructure.Persistence;
using Logaffe.Infrastructure.Persistence.Log;
using Logaffe.Infrastructure.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Logaffe.Infrastructure;

/// <summary>
/// What this layer offers the composition root.
/// </summary>
public static class InfrastructureServices
{
    public static IServiceCollection AddLogaffeInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Read when the context is first built rather than when it is
        // registered: registering must stay free of side effects, because the
        // OpenAPI tooling builds the host at compile time and has no database.
        services.AddDbContext<LogaffeDbContext>(options => options.UseNpgsql(
            configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.")));

        services.AddScoped<IDatabaseProbe, DatabaseProbe>();

        // What the store occupies, which is a different question from whether it
        // can be reached and is asked by a different screen (ADR 0048).
        services.AddScoped<IStoreFootprint, StoreFootprint>();
        services.AddScoped<ISealedSecrets, SealedSecrets>();
        services.AddScoped<IProjects, Projects>();
        services.AddScoped<IGroups, Groups>();
        services.AddScoped<IHosts, Hosts>();
        services.AddScoped<ITokens, Tokens>();
        services.AddScoped<IInstallation, Installation>();
        services.AddScoped<IOperators, Operators>();
        services.AddScoped<ISessions, Sessions>();
        services.AddScoped<SchemaMigrator>();

        // The database as something that can be written out and read back. It is
        // registered here rather than only where the verbs build their host,
        // because what answers it is this layer's business either way (ADR 0037).
        services.AddScoped<IDatabaseDump, PostgresDump>();

        // The one table EF Core declares and does not serve (ADR 0003). It is
        // registered beside the stores because the layer above asks for it the
        // same way; what is different is on the other side of the interface.
        services.AddScoped<ISamples, Samples>();
        services.AddScoped<IEntries, Entries>();

        // What the entries were counted into on their way past. It is an
        // ordinary store because the volume is ordinary — a row per project per
        // hour — and it is beside the two above because the act that writes it
        // runs on a timer rather than in a request (ADR 0047).
        services.AddScoped<ITallies, Tallies>();

        // What the conditions of ADR 0050 remember between passes. It is a
        // table for one reason: an installation that restarts hourly must not
        // notify hourly.
        services.AddScoped<IConditionStates, ConditionStates>();

        // Where an alert goes. The address it links to is deployment
        // configuration and the rest of the notifier is a row, so this is read
        // per send rather than bound once: an operator who fixes a topic at
        // half past has it fixed on the hour.
        services.AddSingleton(provider => AlertLinks.From(
            configuration, provider.GetRequiredService<ILogger<AlertLinks>>()));

        // The one timeout in the alerting path, and it is short on purpose: the
        // hourly pass has other projects to evaluate, and a notifier that hangs
        // must cost this alert rather than the pass (docs/alerts.md).
        services.AddHttpClient<IAlertNotifier, NtfyNotifier>(
            client => client.Timeout = TimeSpan.FromSeconds(10));

        // The other half of it, and the one this layer takes Dapper for: the
        // write and the sweep had nothing to map, and a filtered page does.
        services.AddScoped<IEntryReader, EntryReader>();
        services.AddScoped<ISampleReader, SampleReader>();

        // The counter that gives entries their identities, and the one thing in
        // this layer that has to outlive a request: an installation is a single
        // writer, and a number handed out per scope would hand out the same
        // number twice.
        services.AddSingleton<IEntryIds, EntryIds>();

        // One key for the installation, read from the volume the first time a
        // token is sealed or opened — the same deferral as the connection string
        // above, and for the same reason.
        services.AddSingleton(provider => new HostVolumeKey(
            VolumePath(configuration),
            provider.GetRequiredService<ILogger<HostVolumeKey>>()));

        // The rest of the same directory. The key is what makes an artifact worth
        // having; everything beside it goes into the artifact too, because both
        // halves or neither is the whole of ADR 0024.
        services.AddSingleton<IHostVolume>(_ => new HostVolume(VolumePath(configuration)));
        services.AddSingleton<ISecretCipher, AesGcmSecretCipher>();

        // Where a drawn claim secret is put for the operator to read. It is on
        // the same volume and is not part of it in any other sense: it is written
        // once, read once and removed by the claim (ADR 0040).
        services.AddSingleton<IClaimSecretHandover>(
            _ => new ClaimSecretFile(VolumePath(configuration)));

        // Neither of these holds anything: one is PBKDF2 with its parameters
        // written into every hash it produces, the other is arithmetic over a
        // secret the caller brings.
        services.AddSingleton<IPasswordHasher, FrameworkPasswordHasher>();
        services.AddSingleton<ISecondFactor, Rfc6238SecondFactor>();

        return services;
    }

    private static string VolumePath(IConfiguration configuration) =>
        configuration["Logaffe:VolumePath"]
        ?? throw new InvalidOperationException("Logaffe:VolumePath is not configured.");
}
