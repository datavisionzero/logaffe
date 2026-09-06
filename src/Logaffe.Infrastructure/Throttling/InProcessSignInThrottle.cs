using System.Collections.Concurrent;
using Logaffe.Application.Ports;
using Logaffe.Domain.Identities;

namespace Logaffe.Infrastructure.Throttling;

/// <summary>
/// The two rolling windows of ADR 0056, in a bounded store in the process.
/// </summary>
/// <remarks>
/// <para>
/// A restart forgets what it held, and that is the point rather than a
/// shortcoming: the alternative makes signing in depend on a table that has to
/// be swept, or on a second service. What is lost by forgetting is a few
/// minutes of an attacker's accumulated cost; what is gained is a sign-in path
/// that cannot be taken down by its own bookkeeping.
/// </para>
/// <para>
/// <b>It is bounded, so a flood of distinct addresses evicts rather than
/// grows.</b> An unauthenticated caller chooses the account key — that is the
/// whole point of counting per address — so a store that grew with it would be a
/// way to spend an installation's memory from outside. When the cap is reached
/// the oldest windows go, which is safe in the direction that matters: what an
/// eviction costs is a forgotten failure, and what it prevents is an
/// installation that stops answering.
/// </para>
/// </remarks>
public sealed class InProcessSignInThrottle : ISignInThrottle
{
    /// <summary>
    /// How many windows of each kind are held. An installation of the size
    /// `VISION.md` targets has a handful of accounts and a handful of places
    /// they sign in from, so anything above this is somebody feeding the store
    /// rather than somebody signing in.
    /// </summary>
    private const int Windows = 4_096;

    private readonly ConcurrentDictionary<string, Window> accounts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Window> sources = new(StringComparer.Ordinal);

    public bool Admits(string account, string source, DateTimeOffset now) =>
        Attempts(accounts, account, now) < SignInThrottle.AttemptsPerAccount
        && Attempts(sources, source, now) < SignInThrottle.AttemptsPerSource;

    public void Failed(string account, string source, DateTimeOffset now)
    {
        Count(accounts, account, now);
        Count(sources, source, now);
    }

    public void Succeeded(string account) => accounts.TryRemove(account, out _);

    /// <summary>
    /// How many failures that key has inside the window, with the ones that fell
    /// out of it dropped on the way past. Pruning on the read is what keeps a
    /// key that is never presented again from needing a sweep.
    /// </summary>
    private static int Attempts(
        ConcurrentDictionary<string, Window> windows, string key, DateTimeOffset now)
    {
        if (!windows.TryGetValue(key, out var window))
        {
            return 0;
        }

        var live = window.Since(now - SignInThrottle.Window);

        if (live.Attempts.Count == 0)
        {
            windows.TryRemove(key, out _);

            return 0;
        }

        windows[key] = live;

        return live.Attempts.Count;
    }

    private void Count(
        ConcurrentDictionary<string, Window> windows, string key, DateTimeOffset now)
    {
        Evict(windows, now);

        windows.AddOrUpdate(
            key,
            _ => new Window([now]),
            (_, held) => held.Since(now - SignInThrottle.Window).With(now));
    }

    /// <summary>
    /// Makes room before writing: first the windows that have drained, and then
    /// — only if that was not enough — the oldest of what is left.
    /// </summary>
    private static void Evict(ConcurrentDictionary<string, Window> windows, DateTimeOffset now)
    {
        if (windows.Count < Windows)
        {
            return;
        }

        var drained = now - SignInThrottle.Window;

        foreach (var (key, window) in windows)
        {
            if (window.Since(drained).Attempts.Count == 0)
            {
                windows.TryRemove(key, out _);
            }
        }

        foreach (var (key, _) in windows
                     .OrderBy(entry => entry.Value.Newest)
                     .Take(Math.Max(0, windows.Count - Windows + 1)))
        {
            windows.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// The failures one key has accumulated, newest last. It is a handful of
    /// timestamps: the count that matters is five or twenty, so there is nothing
    /// here a list is the wrong shape for.
    /// </summary>
    private sealed record Window(IReadOnlyList<DateTimeOffset> Attempts)
    {
        public DateTimeOffset Newest =>
            Attempts.Count == 0 ? DateTimeOffset.MinValue : Attempts[^1];

        public Window Since(DateTimeOffset drained) =>
            new([.. Attempts.Where(attempt => attempt > drained)]);

        public Window With(DateTimeOffset attempt) => new([.. Attempts, attempt]);
    }
}
