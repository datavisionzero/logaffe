using Logaffe.Application.Ports;
using Logaffe.Domain.Projects;

namespace Logaffe.Application.Operations;

/// <summary>
/// How many of a project's entries a window would put outside itself, asked
/// before that window is applied.
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/projects.md</c> requires that the operator be told this before the
/// change takes effect, because a settings field that silently destroys data is
/// a bad settings field. Raising a window brings nothing back — what was swept
/// is gone — so this is the only moment at which the number is worth anything.
/// </para>
/// <para>
/// <b>It is a read in front of the act and not part of it.</b>
/// <see cref="ChangeRetentionWindow"/> stays a write with no reading behaviour
/// in it, and this stays something the operator can ask as often as they like
/// while they are deciding, for windows they never apply.
/// </para>
/// <para>
/// The window it is asked for is one that already exists, so a number above the
/// ceiling of ADR 0020 is refused where every other window is — there is no
/// answering "and this is what two years would keep", because that is not a
/// window an installation has.
/// </para>
/// <para>
/// <b>It is the sweep's own arithmetic.</b> The cutoff here is the one
/// <see cref="SweepExpiredEntries"/> computes, over the same index, so the
/// number the operator is shown is the number that goes. It is a count at a
/// moment and not a promise: entries keep arriving, and the ones that age past
/// the window between the reading and the change go with them.
/// </para>
/// </remarks>
public sealed class CountEntriesOutsideWindow(
    IProjects projects, IEntries entries, TimeProvider clock)
{
    /// <summary>
    /// The count, or <c>null</c> when there is no such project — which is what a
    /// project deleted in another tab looks like.
    /// </summary>
    public async Task<long?> ExecuteAsync(
        Guid id, RetentionWindow proposed, CancellationToken cancellationToken)
    {
        var project = await projects.FindAsync(id, cancellationToken);
        if (project is null)
        {
            return null;
        }

        // Zero is the ordinary answer, and it is the answer to raising a window
        // as well as to lowering one onto nothing. Nothing here makes it a
        // warning; what the screen does with a nought is the screen's.
        return await entries.CountReceivedBeforeAsync(
            project.Id, clock.GetUtcNow() - proposed.Duration, cancellationToken);
    }
}
