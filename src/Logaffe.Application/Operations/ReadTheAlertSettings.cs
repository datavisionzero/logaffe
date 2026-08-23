using Logaffe.Application.Ports;
using Logaffe.Domain.Alerts;
using Logaffe.Domain.Hosts;
using Logaffe.Domain.Projects;

namespace Logaffe.Application.Operations;

/// <summary>
/// What one project's silence currently has to reach before anything is said
/// about it.
/// </summary>
/// <param name="ToleratedHours">
/// The project's longest quiet stretch of the fortnight, three times over, with
/// a floor of an hour (<see cref="Quiet"/>). It fires on the hour after this.
/// </param>
public sealed record ToleratedSilence(Guid ProjectId, string Name, int ToleratedHours);

/// <summary>
/// What "gone quiet" works out to in this installation, at both ends of it.
/// </summary>
/// <remarks>
/// <para>
/// A switch whose behaviour has to be looked up in a document is a switch that
/// gets turned on once and then distrusted (<c>docs/ui.md</c>), and this is what
/// the screen says instead: not that a project is noticed after three times its
/// longest quiet stretch, but that <c>shop / api</c> is noticed after two hours
/// and the nightly batch after fifteen.
/// </para>
/// <para>
/// <b>Two projects and not a table of them.</b> What the operator is deciding is
/// whether to switch a condition on, and the two ends are what tells them
/// whether it will wake them for nothing — the middle of the list adds length
/// and no information.
/// </para>
/// </remarks>
/// <param name="Busiest">
/// The project noticed soonest, or <c>null</c> when no project can fire this
/// condition at all.
/// </param>
/// <param name="Quietest">
/// The project noticed latest, which is the same project as
/// <paramref name="Busiest"/> on an installation holding one.
/// </param>
/// <param name="WithoutAFortnight">
/// How many projects have less than a fortnight of tally behind them and so
/// cannot fire either rate condition however they behave. It covers the first
/// two weeks of every project and the first two weeks after a restore.
/// </param>
public sealed record QuietAsItStands(
    ToleratedSilence? Busiest, ToleratedSilence? Quietest, int WithoutAFortnight);

/// <summary>
/// When one condition last fired about one subject, and what that subject is
/// called.
/// </summary>
/// <remarks>
/// It is the only history alerting has. There is no list of alerts and nothing
/// to acknowledge — an alert leaves the installation and does not accumulate on
/// a screen (<c>docs/ui.md</c>) — so what is kept is one instant per subject per
/// condition and what is shown is that instant.
/// </remarks>
public sealed record WhatLastFired(
    Guid SubjectId, string Subject, AlertCondition Condition, DateTimeOffset At);

/// <summary>
/// Everything the alerts area shows apart from the notifier: the switches, what
/// each of them currently works out to, and when each condition last fired.
/// </summary>
/// <remarks>
/// <para>
/// One read for one area, because it is one screen and the parts of it are not
/// separately interesting: the switch, what it will do and whether it can see
/// are the same sentence, and an operator reading the three of them out of three
/// requests would watch them arrive in a different order every time.
/// </para>
/// <para>
/// <b>It reads no entry either.</b> What describes a condition is what the
/// condition itself runs on — the tally and the samples — so the settings screen
/// reaches exactly as far as the hourly pass does and no further (ADR 0049).
/// </para>
/// <para>
/// <b>It walks the projects and their fortnight.</b> That is a few hundred small
/// rows per project on a screen an operator opens rarely, which is the same read
/// the hourly pass takes and is why it is taken here rather than kept as a
/// figure that would go stale between openings.
/// </para>
/// </remarks>
public sealed class ReadTheAlertSettings(
    IInstallation installation,
    IProjects projects,
    IHosts hosts,
    ITallies tallies,
    IConditionStates states,
    CheckTheStoreIsFillingUp fillingUp,
    TimeProvider clock)
{
    public async Task<TheAlertSettings> ExecuteAsync(CancellationToken cancellationToken)
    {
        var held = await projects.ListAsync(cancellationToken);

        return new TheAlertSettings(
            await installation.ReadAlertSwitchesAsync(cancellationToken),
            await installation.ReadHostAsync(cancellationToken),
            await fillingUp.ReadAsync(cancellationToken),
            await QuietAsync(held, cancellationToken),
            await FiredAsync(held, cancellationToken));
    }

    /// <remarks>
    /// <b>A muted project is left out of both ends.</b> It is not evaluated at
    /// all, so putting it forward as what this switch will do would be describing
    /// the one project the switch will never say anything about.
    /// </remarks>
    private async Task<QuietAsItStands> QuietAsync(
        IReadOnlyList<Project> held, CancellationToken cancellationToken)
    {
        var closedHour = Alerting.ClosedHourAt(clock.GetUtcNow());
        var from = closedHour - Tallying.Baseline;

        var tolerances = new List<ToleratedSilence>();
        var young = 0;

        foreach (var project in held)
        {
            if (project.Muted)
            {
                continue;
            }

            var oldest = await tallies.OldestHourAsync(project.Id, cancellationToken);
            if (oldest is null || oldest.Value > from)
            {
                // The fortnight guard, counted rather than named: a project
                // created this morning has no normal to have departed from, and
                // neither has an installation restored this morning.
                young++;
                continue;
            }

            var received = await tallies.ReadAsync(
                project.Id, from, closedHour.AddHours(1), cancellationToken);

            if (received.Count == 0)
            {
                // Nothing in a whole fortnight, so there is no stretch it has
                // come back from and this condition cannot fire for it either.
                continue;
            }

            tolerances.Add(new ToleratedSilence(
                project.Id,
                project.Name,
                Quiet.Tolerated([.. received.Select(row => row.Hour)], from)));
        }

        // Ordered by the number and then by the name, so that two projects
        // tolerating the same silence put the same one forward on every read.
        var ordered = tolerances
            .OrderBy(tolerance => tolerance.ToleratedHours)
            .ThenBy(tolerance => tolerance.Name, StringComparer.Ordinal)
            .ToList();

        return new QuietAsItStands(
            ordered.FirstOrDefault(), ordered.LastOrDefault(), young);
    }

    /// <remarks>
    /// <b>A row whose subject is gone is left out.</b> Deleting a project or a
    /// host leaves its rows behind, exactly as it leaves its tally, and what
    /// they are is history about something that no longer exists — a name this
    /// screen cannot print and a link it cannot make.
    /// </remarks>
    private async Task<IReadOnlyList<WhatLastFired>> FiredAsync(
        IReadOnlyList<Project> held, CancellationToken cancellationToken)
    {
        var fired = await states.ListFiredAsync(cancellationToken);
        if (fired.Count == 0)
        {
            return [];
        }

        var names = held.ToDictionary(project => project.Id, project => project.Name);

        foreach (var host in await hosts.ListAsync(cancellationToken))
        {
            // The one condition that is not about a project is about the machine
            // the installation sits on, and the two identities cannot collide.
            names[host.Id] = host.Name;
        }

        return
        [
            .. fired
                .Where(state => names.ContainsKey(state.SubjectId))
                .Select(state => new WhatLastFired(
                    state.SubjectId,
                    names[state.SubjectId],
                    state.Condition,
                    state.NotifiedAt!.Value)),
        ];
    }
}

/// <inheritdoc cref="ReadTheAlertSettings"/>
/// <param name="Host">
/// The machine the installation sits on and the mount holding its database, or
/// <c>null</c> when it names none — which is the ordinary case and not a
/// degraded one.
/// </param>
/// <param name="Store">
/// How full that mount is, or what stands between this installation and knowing.
/// A condition switched on and blind says so where it is switched on, rather
/// than sitting there silent.
/// </param>
public sealed record TheAlertSettings(
    AlertSwitches Switches,
    InstallationHost? Host,
    StoreFullness Store,
    QuietAsItStands Quiet,
    IReadOnlyList<WhatLastFired> Fired);
