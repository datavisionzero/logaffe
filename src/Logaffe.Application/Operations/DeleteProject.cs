using Logaffe.Application.Ports;

namespace Logaffe.Application.Operations;

/// <summary>
/// Ending a project, immediately and irreversibly.
/// </summary>
/// <remarks>
/// <para>
/// The project, its tokens and its visibility go at once; the entries follow
/// afterwards, in the background (ADR 0019). Doing it all in one transaction
/// would mean a request standing for minutes while millions of rows are
/// removed, at whatever moment the operator happened to pick and while other
/// projects are still receiving deliveries.
/// </para>
/// <para>
/// <b>The tokens go with the row and the entries do not yet.</b> The cascade on
/// the foreign key is what removes the tokens, so a sender holding one is
/// answered <c>401</c> from its next delivery and, being fire-and-forget, keeps
/// writing locally without noticing — the same experience as a rotation done
/// carelessly. The entry table does not exist yet; when it does, the sweep hangs
/// here, and until then there is nothing left behind.
/// </para>
/// <para>
/// <b>The confirmation is not here.</b> Deleting is confirmed by typing the
/// project's name, and that guard belongs to the screen the operator is
/// standing in front of: a name repeated back to the server would protect
/// nobody who deliberately issued this by hand, and it would make one route
/// answer to a rule none of the others do.
/// </para>
/// <para>
/// There is no undelete, no archive and no grace period. Logs are additive to
/// the applications' own local files, so the cost of a mistaken deletion is
/// real but bounded, and a grace period would keep data the operator believes
/// is gone.
/// </para>
/// </remarks>
public sealed class DeleteProject(IProjects projects)
{
    /// <summary>
    /// Whether there was a project to delete. <c>false</c> is a project already
    /// gone — a second click, or another tab — and not a failure of anything.
    /// </summary>
    public async Task<bool> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        var project = await projects.FindAsync(id, cancellationToken);
        if (project is null)
        {
            return false;
        }

        await projects.RemoveAsync(project, cancellationToken);
        return true;
    }
}
