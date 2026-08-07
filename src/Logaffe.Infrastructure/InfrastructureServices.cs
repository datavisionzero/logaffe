using Logaffe.Application.Ports;
using Logaffe.Infrastructure.Persistence;
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
        services.AddScoped<ISealedSecrets, SealedSecrets>();
        services.AddScoped<ITokens, Tokens>();
        services.AddScoped<IInstallation, Installation>();
        services.AddScoped<IOperators, Operators>();
        services.AddScoped<ISessions, Sessions>();
        services.AddScoped<SchemaMigrator>();

        // One key for the installation, read from the volume the first time a
        // token is sealed or opened — the same deferral as the connection string
        // above, and for the same reason.
        services.AddSingleton(provider => new HostVolumeKey(
            configuration["Logaffe:VolumePath"]
            ?? throw new InvalidOperationException("Logaffe:VolumePath is not configured."),
            provider.GetRequiredService<ILogger<HostVolumeKey>>()));
        services.AddSingleton<ISecretCipher, AesGcmSecretCipher>();

        // Neither of these holds anything: one is PBKDF2 with its parameters
        // written into every hash it produces, the other is arithmetic over a
        // secret the caller brings.
        services.AddSingleton<IPasswordHasher, FrameworkPasswordHasher>();
        services.AddSingleton<ISecondFactor, Rfc6238SecondFactor>();

        return services;
    }
}
