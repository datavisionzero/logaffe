namespace Logaffe.Domain.Projects;

/// <summary>
/// The unit of separation: every log entry belongs to exactly one, the operator
/// creates them explicitly, and separation holds in storage, in the UI and in
/// agent access alike.
/// </summary>
/// <remarks>
/// A project is a name, a retention window and its ingest token, and nothing
/// else. It is identified by an <see cref="Id"/> that survives every rename, and
/// that identity is what entries, tokens and queries attach to — never the name.
/// </remarks>
public sealed class Project
{
    public const int NameMaxLength = 100;

    private Project()
    {
        // EF Core materializes through this; every other route goes through Create.
    }

    private Project(Guid id, string name, RetentionWindow retention, DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        Retention = retention;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private init; }

    /// <summary>
    /// Unique within an installation and changeable at any time. The uniqueness
    /// is there for the operator who reaches for one of two projects called
    /// <c>api</c> at three in the morning, not for any technical reason.
    /// </summary>
    public string Name { get; private set; } = null!;

    public RetentionWindow Retention { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private init; }

    public static Project Create(string name, RetentionWindow retention, DateTimeOffset createdAt) =>
        new(Guid.CreateVersion7(), Normalize(name), retention, createdAt);

    public void Rename(string name) => Name = Normalize(name);

    public void KeepFor(RetentionWindow retention) => Retention = retention;

    private static string Normalize(string name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("A project has a name.", nameof(name));
        }

        return trimmed.Length > NameMaxLength
            ? throw new ArgumentException(
                $"A project name is at most {NameMaxLength} characters.", nameof(name))
            : trimmed;
    }
}
