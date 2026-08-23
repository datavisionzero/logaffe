using Logaffe.Application.Ports;
using Logaffe.Domain.Alerts;
using Logaffe.Domain.Hosts;
using Logaffe.Domain.Operators;
using Logaffe.Domain.Projects;
using Microsoft.EntityFrameworkCore;

namespace Logaffe.Infrastructure.Persistence;

/// <summary>
/// The one row an installation holds about itself.
/// </summary>
/// <remarks>
/// The instant is here rather than in a file on the host volume beside the key
/// (ADR 0034), which is what makes the first run the run that created the
/// schema — and what keeps Host Recovery writing to one store rather than two
/// that can disagree. The drawn claim secret's hash is in the same row for the
/// same reasons (ADR 0040).
/// </remarks>
public sealed class Installation(LogaffeDbContext context) : IInstallation
{
    public Task<ClaimGuard?> ReadClaimGuardAsync(CancellationToken cancellationToken) =>
        context.ClaimGuards.SingleOrDefaultAsync(cancellationToken);

    public async Task<ClaimGuard> OpenClaimAsync(
        DateTimeOffset firstRunAt, CancellationToken cancellationToken)
    {
        var existing = await context.ClaimGuards.SingleOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            // Every start after the first. This is what "a restart does not
            // extend it" is: the row is already there and nothing is written.
            return existing;
        }

        var guard = ClaimGuard.OpenedOnFirstRun(firstRunAt);
        context.ClaimGuards.Add(guard);

        try
        {
            await context.SaveChangesAsync(cancellationToken);

            return guard;
        }
        catch (DbUpdateException)
        {
            // Two containers came up at once and the single-row unique index
            // decided it, exactly as it decides the claim itself. Whichever row
            // is there is the installation's guard, and this start reads it
            // rather than insisting on its own — including the secret the other
            // one drew, which is the one that was handed over.
            context.ChangeTracker.Clear();

            return await context.ClaimGuards.SingleAsync(cancellationToken);
        }
    }

    public async Task<ClaimGuard> ArmClaimAsync(
        DateTimeOffset at, CancellationToken cancellationToken)
    {
        var guard = await context.ClaimGuards.SingleOrDefaultAsync(cancellationToken);

        if (guard is null)
        {
            // A database somebody created without ever starting the
            // installation. Host Recovery is the command that hands an
            // installation back, so it writes the row rather than refusing over
            // one that should have been there.
            guard = ClaimGuard.OpenedOnFirstRun(at);
            context.ClaimGuards.Add(guard);
        }
        else
        {
            guard.ArmAt(at);
        }

        await context.SaveChangesAsync(cancellationToken);

        return guard;
    }

    public async Task<RetentionWindow> ReadSampleRetentionAsync(
        CancellationToken cancellationToken)
    {
        var settings = await context.InstallationSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        // An installation that has never been told keeps the default, and the
        // row is not written to say so: a setting nobody has set is the absence
        // of a row, not a row repeating what the product already says.
        return RetentionWindow.OfDays(
            settings?.SampleRetentionDays ?? Sampling.RetentionDaysByDefault);
    }

    public async Task RecordSampleRetentionAsync(
        RetentionWindow window, CancellationToken cancellationToken)
    {
        var settings = await context.InstallationSettings.SingleOrDefaultAsync(
            cancellationToken);

        if (settings is null)
        {
            context.InstallationSettings.Add(
                new InstallationSettings { SampleRetentionDays = window.Days });
        }
        else
        {
            settings.SampleRetentionDays = window.Days;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <remarks>
    /// The pair is read as a pair: the set-null on
    /// <c>fk_installation_settings_host</c> can take the machine away without
    /// taking the mount with it, and a mount naming no machine names nothing.
    /// </remarks>
    public async Task<InstallationHost?> ReadHostAsync(CancellationToken cancellationToken)
    {
        var settings = await context.InstallationSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        return settings is { HostId: { } hostId, MountPath: { } mount }
            ? new InstallationHost(hostId, MountPath.Create(mount))
            : null;
    }

    public async Task RecordHostAsync(
        InstallationHost? host, CancellationToken cancellationToken)
    {
        var settings = await context.InstallationSettings.SingleOrDefaultAsync(
            cancellationToken);

        if (settings is null)
        {
            // The row is written for this alone, carrying the window the product
            // recommends: an installation that names a host before it has ever
            // touched the sample window has still not set that window, and
            // writing the default down is what the row not existing said.
            context.InstallationSettings.Add(new InstallationSettings
            {
                SampleRetentionDays = Sampling.RetentionDaysByDefault,
                HostId = host?.HostId,
                MountPath = host?.Mount.Value,
            });
        }
        else
        {
            settings.HostId = host?.HostId;
            settings.MountPath = host?.Mount.Value;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<AlertSwitches> ReadAlertSwitchesAsync(CancellationToken cancellationToken)
    {
        var settings = await context.InstallationSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        // An installation nobody has asked has all three off, and the row is not
        // written to say so: a switch nobody has touched is the absence of a
        // row, not a row repeating what the product already says.
        return settings is null
            ? AlertSwitches.AllOff
            : new AlertSwitches(
                settings.AlertOnFillingUp,
                settings.AlertOnGoneQuiet,
                settings.AlertOnFlooding);
    }

    public async Task RecordAlertSwitchesAsync(
        AlertSwitches switches, CancellationToken cancellationToken)
    {
        var settings = await context.InstallationSettings.SingleOrDefaultAsync(
            cancellationToken);

        if (settings is null)
        {
            // Written for this alone, carrying the window the product
            // recommends, for the reason RecordHostAsync writes it: an
            // installation that switches a condition on before it has ever
            // touched the sample window has still not touched it.
            settings = new InstallationSettings
            {
                SampleRetentionDays = Sampling.RetentionDaysByDefault,
            };

            context.InstallationSettings.Add(settings);
        }

        settings.AlertOnFillingUp = switches.FillingUp;
        settings.AlertOnGoneQuiet = switches.GoneQuiet;
        settings.AlertOnFlooding = switches.Flooding;

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<Notifier?> ReadNotifierAsync(CancellationToken cancellationToken)
    {
        var settings = await context.InstallationSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        // Read as a set, the way the host and its mount are: a topic naming no
        // server addresses nothing. A row whose two strings no longer make a
        // notifier is an installation with none, which is what the sending
        // adapter says one line about rather than posting somewhere it guessed.
        return settings is { NotifierServer: { } server, NotifierTopic: { } topic }
            && Notifier.TryCreate(server, topic, settings.NotifierAccessToken, out var notifier)
                ? notifier
                : null;
    }

    public async Task RecordNotifierAsync(
        Notifier? notifier, CancellationToken cancellationToken)
    {
        var settings = await context.InstallationSettings.SingleOrDefaultAsync(
            cancellationToken);

        if (settings is null)
        {
            if (notifier is null)
            {
                // Clearing what was never set. A setting nobody has set is the
                // absence of a row rather than a row saying nothing.
                return;
            }

            // Written for this alone, carrying the window the product
            // recommends, for the reason RecordHostAsync writes it.
            settings = new InstallationSettings
            {
                SampleRetentionDays = Sampling.RetentionDaysByDefault,
            };

            context.InstallationSettings.Add(settings);
        }

        settings.NotifierServer = notifier?.Server.ToString();
        settings.NotifierTopic = notifier?.Topic;
        settings.NotifierAccessToken = notifier?.EncryptedAccessToken;

        await context.SaveChangesAsync(cancellationToken);
    }

    public Task RecordClaimAsync(ClaimGuard guard, CancellationToken cancellationToken)
    {
        // Attached already on every path that gets here — the guard being written
        // back is the one this context read or added a moment ago — so this is
        // the save and nothing else.
        context.ClaimGuards.Update(guard);

        return context.SaveChangesAsync(cancellationToken);
    }
}
