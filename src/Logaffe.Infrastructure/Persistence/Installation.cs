using Logaffe.Application.Ports;
using Logaffe.Domain.Alerts;
using Logaffe.Domain.Hosts;
using Logaffe.Domain.Projects;
using Microsoft.EntityFrameworkCore;

namespace Logaffe.Infrastructure.Persistence;

/// <summary>
/// The one row an installation holds about itself.
/// </summary>
/// <remarks>
/// Everything here is a setting somebody set: the sample window, the machine the
/// installation sits on, the alert switches and the notifier. Nothing about who
/// may sign in is here any more — that is the identity table (ADR 0052) — and
/// nothing about the first start is, because the first administrator comes out
/// of the configuration rather than out of a row (ADR 0054).
/// </remarks>
public sealed class Installation(LogaffeDbContext context) : IInstallation
{
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
                settings.AlertOnFlooding,
                settings.AlertOnFailing);
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
        settings.AlertOnFailing = switches.Failing;

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
}
