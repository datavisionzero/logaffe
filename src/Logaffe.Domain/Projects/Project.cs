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
        new(Guid.CreateVersion7(), NormalizeName(name), retention, createdAt);

    public void Rename(string name) => Name = NormalizeName(name);

    public void KeepFor(RetentionWindow retention) => Retention = retention;

    /// <summary>
    /// The name as it would be stored.
    /// </summary>
    /// <remarks>
    /// Public because uniqueness is asked about before it is written: whoever
    /// looks a name up has to ask about the string this would store rather than
    /// the one that was typed, or a name with a space on the end passes the
    /// check and is then refused by the unique index.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is not a name — it is blank, or longer than
    /// <see cref="NameMaxLength"/>.
    /// </exception>
    public static string NormalizeName(string name)
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
