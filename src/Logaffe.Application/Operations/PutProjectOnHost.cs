using Logaffe.Application.Ports;

namespace Logaffe.Application.Operations;

/// <summary>How saying where a project runs ended.</summary>
public enum PutProjectOnHostOutcome
{
    /// <summary>The project says which machine it runs on, or says none.</summary>
    PutOn,

    /// <summary>
    /// There is no such project. A second browser tab deleted it, or the address
    /// was typed.
    /// </summary>
    NoSuchProject,

    /// <summary>
    /// There is no such host, which is ordinarily one removed from another
    /// browser while this screen was open.
    /// </summary>
    NoSuchHost,
}

/// <summary>
/// Saying which machine a project runs on, or that its machine is not tracked.
/// </summary>
/// <remarks>
/// <para>
/// It moves nothing: entries, tokens and queries are attached to the project's
/// identity, so no sender notices and nothing is redeployed. What it changes is
/// whether there is a band to draw over this project's entries.
/// </para>
/// <para>
/// <b>Unlike moving a project between groups, no name can be taken.</b> A
/// project's name is unique within its group, and a host is not a group — two
/// projects called <c>api</c> may perfectly well run on one machine, because the
/// host is not where they are listed and not a scope they are found in
/// (<c>docs/metrics.md</c>). So this act has one fewer way to fail than its
/// counterpart, and the difference is the whole of what says a host is not a
/// group.
/// </para>
/// </remarks>
public sealed class PutProjectOnHost(IProjects projects, IHosts hosts)
{
    /// <param name="hostId">The machine it runs on, or <c>null</c> for none.</param>
    public async Task<PutProjectOnHostOutcome> ExecuteAsync(
        Guid id, Guid? hostId, CancellationToken cancellationToken)
    {
        var project = await projects.FindAsync(id, cancellationToken);
        if (project is null)
        {
            return PutProjectOnHostOutcome.NoSuchProject;
        }

        if (project.HostId == hostId)
        {
            return PutProjectOnHostOutcome.PutOn;
        }

        // Asked before the write so that naming a host another tab removed is an
        // answer rather than a foreign key violation surfacing as a failure of
        // the installation.
        if (hostId is not null
            && await hosts.FindAsync(hostId.Value, cancellationToken) is null)
        {
            return PutProjectOnHostOutcome.NoSuchHost;
        }

        project.RunsOn(hostId);
        await projects.RecordAsync(project, cancellationToken);

        return PutProjectOnHostOutcome.PutOn;
    }
}
