namespace Logaffe.Domain.Identities;

/// <summary>
/// Whoever acts on an installation: a <see cref="User"/> or an
/// <see cref="Agent"/>, and never a third thing (ADR 0052).
/// </summary>
/// <remarks>
/// <para>
/// One hierarchy in one table, so that everything which records <em>who</em> —
/// a session, a project assignment, a record of a changed setting — has one
/// place to point at. The two kinds differ in what they may do and in how they
/// prove who they are, not in what they are to the rest of the model.
/// </para>
/// <para>
/// <see cref="Administrator"/> sits here rather than on the user because it is
/// asked of every caller on every request, whichever kind the caller is — and
/// for an agent the answer is always no. An agent cannot be made one: the check
/// constraint on the table holds it, and so does the absence of any act on
/// <see cref="Agent"/> that would set it.
/// </para>
/// <para>
/// <b>Identities are never deleted.</b> There is no <c>DeletedAt</c> here and
/// there will not be one: a user who should not sign in is deactivated, so that
/// everything pointing at them keeps pointing at something. The one thing that
/// removes a row is Host Recovery, which removes every one of them at once
/// (ADR 0058).
/// </para>
/// </remarks>
public abstract class Identity
{
    /// <summary>
    /// What a name may be. It is shown in a list beside an address, so it is a
    /// display name rather than a handle.
    /// </summary>
    public const int NameMaxLength = 100;

    protected Identity()
    {
        // EF Core materializes through this; every other route goes through the
        // derived types' own factories.
    }

    protected Identity(Guid id, string name, bool administrator, DateTimeOffset createdAt)
    {
        Id = id;
        Name = NormalizeName(name);
        Administrator = administrator;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private init; }

    /// <summary>
    /// Which of the two this is — a fact of the type rather than a column the
    /// type reads, and the discriminator the table is split on.
    /// </summary>
    public abstract IdentityKind Kind { get; }

    public string Name { get; private set; } = null!;

    /// <summary>
    /// Whether this identity administers the installation: invites and
    /// deactivates people, hands out the role, and assigns projects. It says
    /// nothing about what may be <em>read</em> — that is project access and only
    /// that (ADR 0055).
    /// </summary>
    public bool Administrator { get; protected set; }

    public DateTimeOffset CreatedAt { get; private init; }

    public void Rename(string name) => Name = NormalizeName(name);

    /// <summary>
    /// The name as it would be stored. Public because it is asked about before
    /// it is written — whoever checks for a clash has to ask about the string
    /// this would store rather than the one that was typed.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is blank or longer than
    /// <see cref="NameMaxLength"/>.
    /// </exception>
    public static string NormalizeName(string? name)
    {
        var trimmed = name?.Trim();

        return string.IsNullOrEmpty(trimmed)
            ? throw new ArgumentException("An identity has a name.", nameof(name))
            : trimmed.Length > NameMaxLength
                ? throw new ArgumentException(
                    $"A name is at most {NameMaxLength} characters.", nameof(name))
                : trimmed;
    }
}
