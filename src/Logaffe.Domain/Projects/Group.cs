namespace Logaffe.Domain.Projects;

/// <summary>
/// The set of projects an operator keeps together so they are found together —
/// one product's environments, one customer's applications.
/// </summary>
/// <remarks>
/// <para>
/// A group is a name and an identity that survives its rename, and nothing else:
/// no retention window, no token, and nothing that can be asked of it, because a
/// query still names one project. It exists so that an installation holding
/// twenty projects is a list the operator can read.
/// </para>
/// <para>
/// <b>The identity is deliberate and this class is deliberately empty</b>
/// (ADR 0039). Nothing a group does today needs one; the identity is what makes
/// adding something to a group later an addition rather than a migration that
/// has to invent one for every string an installation in the field happens to
/// hold. Simplifying it away to a word on the project is undoing the decision,
/// not tidying up after it.
/// </para>
/// </remarks>
public sealed class Group
{
    public const int NameMaxLength = 100;

    private Group()
    {
        // EF Core materializes through this; every other route goes through Create.
    }

    private Group(Guid id, string name, DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private init; }

    /// <summary>
    /// Unique within an installation, for the same reason a project's name is
    /// unique within its group: two headings reading <c>shop</c> tell the
    /// operator nothing about which of them holds what.
    /// </summary>
    public string Name { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private init; }

    public static Group Create(string name, DateTimeOffset createdAt) =>
        new(Guid.CreateVersion7(), NormalizeName(name), createdAt);

    public void Rename(string name) => Name = NormalizeName(name);

    /// <summary>
    /// The name as it would be stored, asked about before it is written — the
    /// same reason <see cref="Project.NormalizeName"/> is public.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is not a name — it is blank, or longer than
    /// <see cref="NameMaxLength"/>.
    /// </exception>
    public static string NormalizeName(string name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("A group has a name.", nameof(name));
        }

        return trimmed.Length > NameMaxLength
            ? throw new ArgumentException(
                $"A group name is at most {NameMaxLength} characters.", nameof(name))
            : trimmed;
    }
}
