using Logaffe.Application.Ports;
using Logaffe.Domain.Projects;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// The project table, in memory. It behaves as the real store does in the three
/// ways the acts turn on — a project is found by its identity, a name is found
/// by the string that would be stored, and a removed project is not there any
/// more — and in no other way.
/// </summary>
/// <remarks>
/// It does not cascade. What removing a project does to its tokens is the
/// foreign key's doing and is asked of a real database, not of this.
/// </remarks>
internal sealed class InMemoryProjects : IProjects
{
    private readonly List<Project> _projects = [];

    public IReadOnlyList<Project> Stored => _projects;

    /// <summary>How many statements the store was asked to write.</summary>
    public int Writes { get; private set; }

    /// <summary>A project that is already there when the act runs.</summary>
    public Project Holding(
        string name,
        RetentionWindow retention,
        DateTimeOffset createdAt,
        Guid? groupId = null)
    {
        var project = Project.Create(name, retention, createdAt);
        project.MoveTo(groupId);
        _projects.Add(project);

        return project;
    }

    /// <summary>
    /// Narrowed by the reach, as the real store is: that is the one filter, and
    /// a double that ignored it would let a test pass over a leak (ADR 0055).
    /// </summary>
    public Task<IReadOnlyList<Project>> ListAsync(
        Reach reach, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Project>>(
            [.. _projects.Where(p => reach.Includes(p.Id)).OrderBy(p => p.CreatedAt)]);

    public Task<Project?> FindAsync(Reach reach, Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(
            _projects.SingleOrDefault(p => p.Id == id && reach.Includes(p.Id)));

    /// <summary>
    /// A name is taken where the project would be listed and nowhere else, which
    /// is the whole of what the group changes about the acts: the projects in no
    /// group are one set, and each group is another.
    /// </summary>
    public Task<Project?> FindAsync(
        string name, Guid? groupId, CancellationToken cancellationToken) =>
        Task.FromResult(_projects.SingleOrDefault(p => p.Name == name && p.GroupId == groupId));

    public Task AddAsync(Project project, CancellationToken cancellationToken) =>
        Write(() => _projects.Add(project));

    public Task RecordAsync(Project project, CancellationToken cancellationToken) =>
        Write(() => { });

    public Task RemoveAsync(Project project, CancellationToken cancellationToken) =>
        Write(() => _projects.Remove(project));

    private Task Write(Action write)
    {
        write();
        Writes++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// The group table, in memory, in the same three ways as the projects above.
/// </summary>
/// <remarks>
/// It does not cascade either. What removing a group does to the projects that
/// pointed at it is <c>fk_project_project_group</c>'s doing and is asked of a
/// real database, not of this.
/// </remarks>
internal sealed class InMemoryGroups : IGroups
{
    private readonly List<Group> _groups = [];

    public IReadOnlyList<Group> Stored => _groups;

    /// <summary>How many statements the store was asked to write.</summary>
    public int Writes { get; private set; }

    /// <summary>A group that is already there when the act runs.</summary>
    public Group Holding(string name, DateTimeOffset createdAt)
    {
        var group = Group.Create(name, createdAt);
        _groups.Add(group);

        return group;
    }

    public Task<IReadOnlyList<Group>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Group>>([.. _groups.OrderBy(g => g.CreatedAt)]);

    public Task<Group?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_groups.SingleOrDefault(g => g.Id == id));

    public Task<Group?> FindAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(_groups.SingleOrDefault(g => g.Name == name));

    public Task AddAsync(Group group, CancellationToken cancellationToken) =>
        Write(() => _groups.Add(group));

    public Task RecordAsync(Group group, CancellationToken cancellationToken) =>
        Write(() => { });

    public Task RemoveAsync(Group group, CancellationToken cancellationToken) =>
        Write(() => _groups.Remove(group));

    private Task Write(Action write)
    {
        write();
        Writes++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Who reaches which project, in memory. What the acts turn on is that creating
/// one grants its creator access in the same act (ADR 0055), and that granting
/// twice is the same state rather than a second row.
/// </summary>
internal sealed class InMemoryProjectAccess : IProjectAccess
{
    private readonly List<ProjectAccess> _granted = [];

    public IReadOnlyList<ProjectAccess> Granted => _granted;

    public Task<Reach> ReachOfAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Reach.Of(
            _granted.Where(a => a.UserId == userId).Select(a => a.ProjectId)));

    public Task<IReadOnlyList<ProjectAccess>> ListForProjectAsync(
        Guid projectId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProjectAccess>>(
            [.. _granted.Where(a => a.ProjectId == projectId)]);

    public Task<IReadOnlyList<ProjectAccess>> ListForUserAsync(
        Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProjectAccess>>(
            [.. _granted.Where(a => a.UserId == userId)]);

    public Task<bool> GrantAsync(ProjectAccess access, CancellationToken cancellationToken)
    {
        if (_granted.Any(a => a.ProjectId == access.ProjectId && a.UserId == access.UserId))
        {
            return Task.FromResult(false);
        }

        _granted.Add(access);

        return Task.FromResult(true);
    }

    public Task<bool> RevokeAsync(
        Guid projectId, Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(
            _granted.RemoveAll(a => a.ProjectId == projectId && a.UserId == userId) > 0);
}
