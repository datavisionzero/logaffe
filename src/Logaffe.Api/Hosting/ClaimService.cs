using Logaffe.Application.Operations;
using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;

namespace Logaffe.Api.Hosting;

/// <summary>
/// Writes the installation's first run, draws the secret that guards its claim
/// when it is configured to have one, and says in the container log how the
/// installation can be claimed.
/// </summary>
/// <remarks>
/// <para>
/// It runs after <see cref="KeyFitsService"/> — registration order is start
/// order — so that an installation which is about to refuse to start does not
/// first open a claim it will never serve. An operator who fixes the key an hour
/// later gets their window, or their secret, from the start that works.
/// </para>
/// <para>
/// The log line is the point of doing this out loud. An operator bringing an
/// installation up for the first time is reading <c>docker compose logs</c>, and
/// what they need from it is either a deadline they are racing or a secret to
/// hand over.
/// </para>
/// <para>
/// <b>The secret is written out in full exactly once</b>, on the start that drew
/// it. Every later start names the file instead: an operator who restarted a
/// container has not lost anything, and a credential repeated on every start is
/// one that ends up in whatever collects the logs.
/// </para>
/// </remarks>
public sealed class ClaimService(
    IServiceScopeFactory scopeFactory,
    IClaimSecretHandover handover,
    ClaimSettings settings,
    ILogger<ClaimService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var opened = await scope.ServiceProvider
            .GetRequiredService<OpenTheClaim>()
            .ExecuteAsync(cancellationToken);

        var state = await scope.ServiceProvider
            .GetRequiredService<CheckTheClaim>()
            .ExecuteAsync(cancellationToken);

        if (state.IsClaimed)
        {
            // Nothing to say. This is every start of an installation in
            // ordinary use.
            return;
        }

        if (opened.Drawn is not null)
        {
            logger.LogWarning(
                "This installation belongs to nobody and is guarded by a claim secret, "
                + "which it has just drawn: {Secret}. It is also in {Path}, readable by "
                + "this container's user alone, and it is removed when somebody claims. "
                + "There is no deadline.",
                opened.Drawn.Text,
                handover.Path);

            return;
        }

        if (settings.Mode is ClaimMode.Secret)
        {
            logger.LogWarning(
                state.CanBeClaimed
                    ? "This installation belongs to nobody and can be claimed by whoever "
                    + "presents its claim secret. There is no deadline."
                    : "This installation belongs to nobody and has no claim secret to "
                    + "present to, so it cannot be claimed. Run `logaffe recover` in this "
                    + "container to draw one.");

            return;
        }

        if (state.CanBeClaimed)
        {
            logger.LogWarning(
                "This installation belongs to nobody and can be claimed by anyone who "
                + "can reach it until {ClosesAt:u}. Claim it now.",
                state.ClosesAt);
        }
        else
        {
            logger.LogWarning(
                "This installation belongs to nobody and its claim window has closed, so "
                + "it cannot be claimed over the network. Run `logaffe recover` in this "
                + "container to open a fresh one.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
