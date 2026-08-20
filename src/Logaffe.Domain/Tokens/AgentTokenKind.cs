namespace Logaffe.Domain.Tokens;

/// <summary>
/// Which of the two an agent token is: it reads entries, or it administers the
/// installation.
/// </summary>
/// <remarks>
/// <para>
/// Neither is a superset of the other and no token is both. A reading token gets
/// the five tools of <c>docs/mcp.md</c> and reaches no setting; an administering
/// token gets the settings surface and reaches no entry — which is the whole of
/// what makes administration safe enough to offer at all, because prompt
/// injection out of a log entry needs one session that both holds untrusted text
/// and can act (ADR 0046).
/// </para>
/// <para>
/// It is settled when the token is issued and there is no act that changes it.
/// An agent that needs the other one is given a second token, and the operator
/// revokes whatever it replaces.
/// </para>
/// </remarks>
public enum AgentTokenKind
{
    /// <summary>
    /// What an agent is given unless the operator decides otherwise, which is
    /// what <c>VISION.md</c>'s read-only by default means. It is zero so that
    /// every agent token issued before the kind existed is one of these.
    /// </summary>
    Reading = 0,

    Administering = 1,
}

/// <summary>
/// The prefix an agent token of each kind is written with, and the reading back
/// of it.
/// </summary>
/// <remarks>
/// The two enums are one fact in two vocabularies: <see cref="TokenKind"/> is
/// what a presented token's prefix says, and <see cref="AgentTokenKind"/> is
/// what an agent token row is. They are mapped here and nowhere else, so that
/// the comparison authentication makes between the two is a comparison of like
/// with like.
/// </remarks>
public static class AgentTokenKinds
{
    /// <summary>The prefix kind a token of <paramref name="kind"/> carries.</summary>
    public static TokenKind AsTokenKind(this AgentTokenKind kind) => kind switch
    {
        AgentTokenKind.Reading => TokenKind.Agent,
        AgentTokenKind.Administering => TokenKind.Administering,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown agent token kind."),
    };

    /// <summary>
    /// Which kind of agent token <paramref name="kind"/> is the prefix of, and
    /// <c>false</c> when it is the prefix of no agent token at all — an ingest
    /// or a host token, presented where an agent's belongs.
    /// </summary>
    public static bool TryFromTokenKind(TokenKind kind, out AgentTokenKind agentKind)
    {
        switch (kind)
        {
            case TokenKind.Agent:
                agentKind = AgentTokenKind.Reading;
                return true;
            case TokenKind.Administering:
                agentKind = AgentTokenKind.Administering;
                return true;
            default:
                agentKind = default;
                return false;
        }
    }
}
