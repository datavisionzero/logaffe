namespace Logaffe.Domain.Identities;

/// <summary>
/// An AI acting on the behalf of the user who owns it, reaching the installation
/// over MCP (<c>CONTEXT.md</c>, Agent).
/// </summary>
/// <remarks>
/// <para>
/// It is an identity so that everything which records who acted can point at it,
/// and so that the record says whether a change was made by a person or by
/// something acting for one. The credential it presents is its agent token,
/// which is a row of its own and outlives nothing: <b>the identity survives a
/// revoked token</b>, because a revocation must not erase the record of what the
/// agent did while it held one.
/// </para>
/// <para>
/// <b>An agent is never an administrator and always has an owner.</b> Both are
/// held by the check constraint on the table as well as by this type, because
/// the constraint is the half that cannot be bypassed by a query somebody writes
/// later. Its authority is exactly its owner's: it sees the projects that user
/// sees, and the installation-wide acts are within reach only while that user is
/// an administrator (ADR 0052, ADR 0055).
/// </para>
/// </remarks>
public sealed class Agent : Identity
{
    private Agent()
    {
        // EF Core materializes through this; every other route goes through
        // Create.
    }

    private Agent(Guid id, string name, Guid ownerId, DateTimeOffset createdAt)
        : base(id, name, administrator: false, createdAt) => OwnerId = ownerId;

    public override IdentityKind Kind => IdentityKind.Agent;

    /// <summary>
    /// The user this agent belongs to. Nullable in the column, because the
    /// column is shared with users; required on every agent row, by the check
    /// constraint on the table.
    /// </summary>
    public Guid OwnerId { get; private init; }

    /// <summary>
    /// Creates an agent for a user. It is a user who does this and never an
    /// agent: an agent that can create an agent has escaped the identity it was
    /// given (ADR 0052).
    /// </summary>
    public static Agent Create(string name, Guid ownerId, DateTimeOffset createdAt) =>
        new(Guid.CreateVersion7(), name, ownerId, createdAt);
}
