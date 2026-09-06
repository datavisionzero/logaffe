namespace Logaffe.Domain.Identities;

/// <summary>
/// What a failed sign-in costs, in the two windows it is counted in
/// (ADR 0056).
/// </summary>
/// <remarks>
/// <para>
/// The numbers are here because they are a rule the documents state, and the
/// counting is not: where the attempts are kept is a bounded store in the
/// process, which is an adapter's business and deliberately not product data.
/// </para>
/// <para>
/// <b>It is a throttle that expires, not a lockout that has to be lifted.</b>
/// Nothing latches, nobody has to unlock anything, and there is no state a
/// stranger can put somebody's account into that outlasts a coffee. That is
/// what makes counting per account affordable now that there is more than one:
/// the counter-argument to a lockout — that it hands an attacker a way to shut a
/// person out — is answered by the clock rather than by refusing to count.
/// </para>
/// </remarks>
public static class SignInThrottle
{
    /// <summary>
    /// The rolling window both counts are taken over. Long enough that a
    /// guesser gains nothing by pausing, short enough that somebody who locked
    /// themselves out by mistyping is back inside the quarter hour.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How many failures one normalized address may accumulate in that window.
    /// Five is what a person mistyping a passphrase actually makes.
    /// </summary>
    public const int AttemptsPerAccount = 5;

    /// <summary>
    /// How many one source address may accumulate. Higher than an account's,
    /// because a household, an office or a reverse proxy is several people
    /// behind one address and none of them should spend another's budget.
    /// </summary>
    public const int AttemptsPerSource = 20;
}
