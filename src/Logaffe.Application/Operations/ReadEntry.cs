using Logaffe.Application.Ports;
using Logaffe.Domain.Entries;

namespace Logaffe.Application.Operations;

/// <summary>
/// One entry by its identity, always in full.
/// </summary>
/// <remarks>
/// <para>
/// This is the follow-up after a compact search: the promising line is on the
/// screen, and what is wanted is its exception and its properties. It is a read
/// of its own rather than a flag on the page because that is the shape of the
/// act — a page is scanned and one entry of it is opened — and because a page
/// that carried every exception would be the four-megabyte stack traces of
/// <c>docs/ingestion.md</c> arriving a hundred at a time.
/// </para>
/// <para>
/// <b>It is asked inside a project</b>, like every other read. Not because the
/// identity is not unique — it is, and the cursor depends on it — but so that an
/// entry cannot be reached from a project it does not belong to by guessing a
/// number.
/// </para>
/// <para>
/// It carries no <see cref="Read{TAnswer}"/>. The five seconds bind it like
/// everything else on this surface, but a lookup by primary key that meets them
/// is a broken database rather than a query to narrow, and there is nothing to
/// tell the caller to change.
/// </para>
/// </remarks>
public sealed class ReadEntry(IProjects projects, IEntryReader entries)
{
    /// <summary>
    /// The entry, or <c>null</c> when the project holds none by that identity —
    /// which is also what an entry that aged out between the page and the click
    /// looks like, and what a project that no longer exists looks like.
    /// </summary>
    public async Task<LogEntry?> ExecuteAsync(
        Guid projectId, long id, CancellationToken cancellationToken)
    {
        var project = await projects.FindAsync(projectId, cancellationToken);

        return project is null
            ? null
            : await entries.FindAsync(project.Id, id, cancellationToken);
    }
}
