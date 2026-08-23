using Logaffe.Application.Ports;

namespace Logaffe.Application.Operations;

/// <summary>How muting a project ended.</summary>
public enum MuteAProjectOutcome
{
    /// <summary>The project's conditions are evaluated, or are not.</summary>
    Muted,

    /// <summary>
    /// There is no such project. A second browser tab deleted it, or the address
    /// was typed.
    /// </summary>
    NoSuchProject,
}

/// <summary>
/// Taking one project out of the conditions, or putting it back in.
/// </summary>
/// <remarks>
/// <para>
/// It is the project's own setting rather than the installation's, beside the
/// group and the host, because it is a fact about that project and about nothing
/// else (<c>docs/alerts.md</c>). The switches stay where they are: what is
/// adjustable about alerting is three switches and this one checkbox, and there
/// is nothing here that takes a threshold or a condition.
/// </para>
/// <para>
/// <b>One flag rather than a mute per condition.</b> The project a batch job
/// writes into at three in the morning is the project whose silence at four is
/// not an incident either, so the two conditions are muted by the same fact —
/// and a mute per condition would be the beginning of the per-project
/// configuration ADR 0050 exists to refuse.
/// </para>
/// <para>
/// <b>It changes what is evaluated and nothing else.</b> What a muted project
/// receives, keeps and answers is exactly what it was: the tally is still
/// written, the entries are still swept on their window, and the hourly pass
/// simply does not ask about it (<see cref="EvaluateTheConditions"/>).
/// </para>
/// </remarks>
public sealed class MuteAProject(IProjects projects)
{
    /// <param name="muted">
    /// <c>true</c> to stop evaluating this project's conditions, <c>false</c> to
    /// start again — which is the state every project is created in.
    /// </param>
    public async Task<MuteAProjectOutcome> ExecuteAsync(
        Guid id, bool muted, CancellationToken cancellationToken)
    {
        var project = await projects.FindAsync(id, cancellationToken);
        if (project is null)
        {
            return MuteAProjectOutcome.NoSuchProject;
        }

        if (project.Muted == muted)
        {
            return MuteAProjectOutcome.Muted;
        }

        project.Mute(muted);
        await projects.RecordAsync(project, cancellationToken);

        return MuteAProjectOutcome.Muted;
    }
}
