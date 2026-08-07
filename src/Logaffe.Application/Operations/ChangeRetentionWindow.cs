using Logaffe.Application.Ports;
using Logaffe.Domain.Projects;

namespace Logaffe.Application.Operations;

/// <summary>
/// Changing how long a project keeps its entries.
/// </summary>
/// <remarks>
/// <para>
/// The window is counted from receipt time and it is the only limit a project
/// has. The number is the operator's up to a ceiling no installation can raise,
/// and <see cref="RetentionWindow"/> is what holds that (ADR 0020) — this act
/// is handed a window that already exists.
/// </para>
/// <para>
/// <b>Lowering it removes entries, and this does not yet say how many.</b>
/// <c>docs/projects.md</c> requires the operator be told what the new window
/// puts outside it before it takes effect, because a settings field that
/// silently destroys data is a bad settings field. That count is over the entry
/// table, which does not exist yet; the warning is a screen in front of this act
/// rather than a change to it, and it arrives with the entries.
/// </para>
/// <para>
/// Nothing is swept here. The window is what the sweep reads, so lowering it
/// puts entries outside the window and the next sweep removes them — which is
/// also why raising it again brings nothing back.
/// </para>
/// </remarks>
public sealed class ChangeRetentionWindow(IProjects projects)
{
    /// <summary>Whether there was a project to change.</summary>
    public async Task<bool> ExecuteAsync(
        Guid id, RetentionWindow retention, CancellationToken cancellationToken)
    {
        var project = await projects.FindAsync(id, cancellationToken);
        if (project is null)
        {
            return false;
        }

        project.KeepFor(retention);
        await projects.RecordAsync(project, cancellationToken);

        return true;
    }
}
