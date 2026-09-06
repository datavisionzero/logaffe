namespace Logaffe.Application.Ports;

/// <summary>
/// What holds the guessing back at the sign-in: two rolling windows, one per
/// account and one per source (ADR 0056).
/// </summary>
/// <remarks>
/// <para>
/// A port because where the attempts are kept is not product data. They live in
/// a bounded store in the process, so a restart forgets them — which is the
/// safer end of the trade: the alternative makes signing in depend on a table
/// that has to be swept, or on a second service, and an installation whose
/// sign-in path can be taken down by its own bookkeeping is worse than one that
/// occasionally forgives four wrong guesses.
/// </para>
/// <para>
/// <b>The account key is the normalized address as it was presented</b>, not a
/// user id: an address nobody holds has to count against itself exactly as one
/// somebody holds does, or the throttle becomes a way of asking who has an
/// account here.
/// </para>
/// </remarks>
public interface ISignInThrottle
{
    /// <summary>
    /// Whether another attempt is answered at all. <c>false</c> is a
    /// <c>429</c> and says nothing about which of the two windows is
    /// exhausted.
    /// </summary>
    bool Admits(string account, string source, DateTimeOffset now);

    /// <summary>Counts a failure against both windows.</summary>
    void Failed(string account, string source, DateTimeOffset now);

    /// <summary>
    /// Clears the account's window and leaves the source's standing. Somebody
    /// who mistyped their password four times and then got it right is back to
    /// zero; a source that has been trying twenty addresses has proven nothing
    /// by guessing one of them correctly, and clearing its window on a success
    /// is precisely what a spraying attempt would use.
    /// </summary>
    void Succeeded(string account);
}
