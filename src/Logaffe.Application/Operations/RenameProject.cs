using Logaffe.Application.Ports;
using Logaffe.Domain.Projects;

namespace Logaffe.Application.Operations;

/// <summary>
/// How a rename ended.
/// </summary>
public enum RenameOutcome
{
    /// <summary>The project answers to the new name, and to nothing else.</summary>
    Renamed,

    /// <summary>
    /// There is no such project. A second browser tab deleted it, or the
    /// address was typed.
    /// </summary>
    NoSuchProject,

    /// <summary>
    /// Another project holds that name. It is the one refusal the operator
    /// acts on, so it is not the same answer as a project that is not there.
    /// </summary>
    NameTaken,
}

/// <summary>
/// Giving a project another name.
/// </summary>
/// <remarks>
/// The name can change at any time and the identity cannot: entries, tokens and
/// queries are attached to the identity, so a rename moves nothing, breaks no
/// delivery and is invisible to every sender. What it changes is the word the
/// operator reads at three in the morning, which is what the name is for.
/// </remarks>
public sealed class RenameProject(IProjects projects)
{
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is not a name — it is blank, or longer than
    /// <see cref="Project.NameMaxLength"/>.
    /// </exception>
    public async Task<RenameOutcome> ExecuteAsync(
        Guid id, string name, CancellationToken cancellationToken)
    {
        var project = await projects.FindAsync(id, cancellationToken);
        if (project is null)
        {
            return RenameOutcome.NoSuchProject;
        }

        // Renaming a project to the name it already has is a no-op rather than
        // a collision with itself: the operator opened the field and left it.
        var normalized = Project.NormalizeName(name);
        var taken = await projects.FindAsync(normalized, cancellationToken);
        if (taken is not null && taken.Id != project.Id)
        {
            return RenameOutcome.NameTaken;
        }

        project.Rename(normalized);
        await projects.RecordAsync(project, cancellationToken);

        return RenameOutcome.Renamed;
    }
}
