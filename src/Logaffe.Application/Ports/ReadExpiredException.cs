namespace Logaffe.Application.Ports;

/// <summary>
/// A read that used up the five seconds ADR 0026 gives it.
/// </summary>
/// <remarks>
/// <para>
/// It is its own exception because the alternative is indistinguishable from the
/// caller going away. Both arrive as a cancelled statement, and one of them is a
/// query the operator has to be told how to narrow while the other is a browser
/// tab that closed and wants nothing said to it at all.
/// </para>
/// <para>
/// <b>It carries no remedy.</b> What to narrow is computed from the filters by
/// <c>ReadLimit</c>, above this, because the port does not hold them and because
/// the answer is the same one whichever store threw.
/// </para>
/// </remarks>
public sealed class ReadExpiredException(Exception inner)
    : Exception("A read has five seconds.", inner);
