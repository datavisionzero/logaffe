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
/// The <see cref="GroupId"/> it may carry belongs to the group rather than to
/// it: it changes where the project is listed and nothing about what it is. The
/// <see cref="HostId"/> is the same shape again — it says which machine the
/// project runs on, so that its entries can be read beside what that machine was
/// doing, and it is no kind of scope.
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
    /// Unique within its group and changeable at any time. The uniqueness is
    /// there for the operator who reaches for one of two projects called
    /// <c>api</c> at three in the morning, not for any technical reason — which
    /// is why the group relaxes it exactly as far as it resolves it, and why two
    /// projects called <c>api</c> in no group at all still collide.
    /// </summary>
    public string Name { get; private set; } = null!;

    /// <summary>
    /// The group this project is listed under, or <c>null</c> for one in no
    /// group. It is an identity rather than a name, so renaming a group moves no
    /// project (ADR 0039).
    /// </summary>
    public Guid? GroupId { get; private set; }

    /// <summary>
    /// The host this project runs on, or <c>null</c> for one whose machine is
    /// not tracked. It is an identity rather than a name, so renaming a host
    /// moves no project.
    /// </summary>
    /// <remarks>
    /// At most one, which is the group's shape and the group's sentence. A
    /// project replicated across two machines names one of them or neither: the
    /// truthful owner of a host is the instance, which is a property a sender
    /// writes rather than something the installation manages, and making it
    /// manageable is a larger product than this one (<c>docs/metrics.md</c>).
    /// </remarks>
    public Guid? HostId { get; private set; }

    public RetentionWindow Retention { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private init; }

    public static Project Create(string name, RetentionWindow retention, DateTimeOffset createdAt) =>
        new(Guid.CreateVersion7(), NormalizeName(name), retention, createdAt);

    public void Rename(string name) => Name = NormalizeName(name);

    /// <summary>
    /// Lists the project under another group, or under none when
    /// <paramref name="groupId"/> is <c>null</c>. It moves nothing else: entries,
    /// tokens and queries are attached to the identity, so no sender notices.
    /// </summary>
    public void MoveTo(Guid? groupId) => GroupId = groupId;

    /// <summary>
    /// Says which machine this project runs on, or none when
    /// <paramref name="hostId"/> is <c>null</c>. It moves nothing else and no
    /// sender notices: what it changes is whether there is a band to draw over
    /// this project's entries.
    /// </summary>
    public void RunsOn(Guid? hostId) => HostId = hostId;

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
