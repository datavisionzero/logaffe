namespace Logaffe.Domain.Hosts;

/// <summary>
/// The machine an operator runs projects on, which reports what it is doing.
/// </summary>
/// <remarks>
/// A host is a name and the samples and token attached to it. Like a group it is
/// a row with an identity that survives every rename (ADR 0039), and unlike a
/// group the identity does not have to be argued for: a host holds data from the
/// day it exists, so it is paying for itself rather than being bought early.
/// <para>
/// It is not a scope. No query takes one, no filter narrows by one, and two
/// projects named onto one machine are as separate as they were before — a host
/// is where samples come from, never a way of asking about entries.
/// </para>
/// </remarks>
public sealed class Host
{
    public const int NameMaxLength = 100;

    private Host()
    {
        // EF Core materializes through this; every other route goes through Create.
    }

    private Host(Guid id, string name, DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private init; }

    /// <summary>
    /// Unique across the installation, and changeable at any time. There is no
    /// group to relax it the way a project's name is relaxed: a host sits in
    /// nothing, so two machines called <c>web</c> are the trap with nothing
    /// beside them to tell them apart.
    /// </summary>
    public string Name { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private init; }

    public static Host Create(string name, DateTimeOffset createdAt) =>
        new(Guid.CreateVersion7(), NormalizeName(name), createdAt);

    public void Rename(string name) => Name = NormalizeName(name);

    /// <summary>
    /// The name as it would be stored. Public for the same reason a project's
    /// is: uniqueness is asked about before it is written, and a name with a
    /// space on the end would otherwise pass the check and be refused by the
    /// index.
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
            throw new ArgumentException("A host has a name.", nameof(name));
        }

        return trimmed.Length > NameMaxLength
            ? throw new ArgumentException(
                $"A host name is at most {NameMaxLength} characters.", nameof(name))
            : trimmed;
    }
}
