namespace Logaffe.Domain.Identities;

/// <summary>
/// The two kinds an identity comes in, and the only two there are
/// (<c>CONTEXT.md</c>, Identity).
/// </summary>
public enum IdentityKind
{
    /// <summary>A person's account, which signs in and may administer.</summary>
    User,

    /// <summary>
    /// An AI acting for the user who owns it, which authenticates with an agent
    /// token and never administers (ADR 0052).
    /// </summary>
    Agent,
}
