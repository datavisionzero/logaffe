namespace Logaffe.Domain.Projects;

/// <summary>
/// The assignment that puts one project within reach of one user
/// (ADR 0055).
/// </summary>
/// <remarks>
/// <para>
/// An agent has no row of its own: it reaches exactly what its owner reaches,
/// resolved through the owner every time rather than copied at the moment a
/// token was issued (ADR 0052). A copy would be a second thing to keep in step,
/// and it would go stale in the one direction that matters — an assignment taken
/// away.
/// </para>
/// <para>
/// <b>It records who granted it and when.</b> Not because anything reads that
/// today, but because "who gave this person access" is one of the questions a
/// multi-user installation gets asked, and the row is where the answer has to be
/// if it is to exist at all.
/// </para>
/// </remarks>
public sealed class ProjectAccess
{
    private ProjectAccess()
    {
        // EF Core materializes through this; every other route goes through Grant.
    }

    private ProjectAccess(Guid projectId, Guid userId, Guid grantedBy, DateTimeOffset grantedAt)
    {
        ProjectId = projectId;
        UserId = userId;
        GrantedBy = grantedBy;
        GrantedAt = grantedAt;
    }

    public Guid ProjectId { get; private init; }

    public Guid UserId { get; private init; }

    /// <summary>
    /// Who handed it out — an administrator, or the person themselves when they
    /// created the project.
    /// </summary>
    public Guid GrantedBy { get; private init; }

    public DateTimeOffset GrantedAt { get; private init; }

    public static ProjectAccess Grant(
        Guid projectId, Guid userId, Guid grantedBy, DateTimeOffset grantedAt) =>
        new(projectId, userId, grantedBy, grantedAt);
}
