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
    public Project Holding(string name, RetentionWindow retention, DateTimeOffset createdAt)
    {
        var project = Project.Create(name, retention, createdAt);
        _projects.Add(project);

        return project;
    }

    public Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Project>>([.. _projects.OrderBy(p => p.CreatedAt)]);

    public Task<Project?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_projects.SingleOrDefault(p => p.Id == id));

    public Task<Project?> FindAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(_projects.SingleOrDefault(p => p.Name == name));

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
