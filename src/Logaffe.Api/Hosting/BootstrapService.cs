using Logaffe.Application.Operations;

namespace Logaffe.Api.Hosting;

/// <summary>
/// Runs the bootstrap after the migrations and before the installation serves:
/// the first administrator out of the configuration, once (ADR 0054).
/// </summary>
/// <remarks>
/// <para>
/// Logged either way. Somebody who set the keys on the second start should read
/// that they were ignored, and somebody who set none on the first should read
/// how to proceed — an installation that says nothing here is one whose sign-in
/// screen refuses every attempt for a reason nobody can see.
/// </para>
/// <para>
/// <b>A value the installation will not accept stops the start</b>, the way a
/// failed migration does. Nothing was written, and starting anyway would be an
/// installation nobody can sign into with a variable that looks as though they
/// could.
/// </para>
/// </remarks>
public sealed class BootstrapService(
    IServiceScopeFactory scopeFactory,
    BootstrapSettings settings,
    ILogger<BootstrapService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var bootstrap = scope.ServiceProvider.GetRequiredService<BootstrapTheInstallation>();

        BootstrapOutcome outcome;
        try
        {
            outcome = await bootstrap.ExecuteAsync(settings, cancellationToken);
        }
        catch (BootstrapRefusedException refusal)
        {
            logger.LogCritical("{Reason} The installation will not start.", refusal.Message);

            throw;
        }

        switch (outcome)
        {
            case BootstrapOutcome.Bootstrapped:
                logger.LogInformation(
                    "Bootstrapped: {Administrator} was created from {AdministratorKey} and "
                    + "{EmailKey}. Open this installation in a browser and exchange the "
                    + "bootstrap token for a password. The configuration is ignored from the "
                    + "next start on.",
                    settings.Administrator,
                    BootstrapSettings.AdministratorKey,
                    BootstrapSettings.EmailKey);
                break;

            case BootstrapOutcome.AddressAdopted:
                logger.LogWarning(
                    "This installation was upgraded from one that had an operator. That "
                    + "account is now its first administrator and signs in with the address "
                    + "in {EmailKey}; its password, second factor and backup codes are "
                    + "unchanged.",
                    BootstrapSettings.EmailKey);
                break;

            case BootstrapOutcome.AlreadyBootstrapped
                when settings.Administrator is not null || settings.Token is not null:
                logger.LogInformation(
                    "{AdministratorKey} and {TokenKey} are set and ignored: this installation "
                    + "already has identities, and a bootstrap happens once.",
                    BootstrapSettings.AdministratorKey,
                    BootstrapSettings.TokenKey);
                break;

            case BootstrapOutcome.AlreadyBootstrapped:
                break;

            case BootstrapOutcome.NothingToBootstrapFrom:
                logger.LogWarning(
                    "There is no identity on this installation and nothing can sign in. Set "
                    + "{AdministratorKey}, {EmailKey} and {TokenKey} (at least {Minimum} "
                    + "characters) and start again to create the first administrator. "
                    + "Ingestion works meanwhile and needs no identity.",
                    BootstrapSettings.AdministratorKey,
                    BootstrapSettings.EmailKey,
                    BootstrapSettings.TokenKey,
                    BootstrapSettings.TokenMinimumLength);
                break;

            default:
                throw new InvalidOperationException($"Unknown bootstrap outcome {outcome}.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
