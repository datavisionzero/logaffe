using Logaffe.Domain.Identities;
using Logaffe.Infrastructure.Throttling;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The two rolling windows in front of the sign-in, which need no database and
/// live here because this is the project that can see an adapter.
/// </summary>
/// <remarks>
/// What is worth proving is what ADR 0056 promises: it expires rather than
/// latches, a success clears the account's window and not the source's, and it
/// is bounded — a flood of distinct addresses evicts rather than growing, which
/// matters because an unauthenticated caller chooses the account key.
/// </remarks>
public sealed class SignInThrottleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    private const string Address = "somebody@example.com";

    private const string Source = "203.0.113.7";

    private readonly InProcessSignInThrottle throttle = new();

    [Fact]
    public void An_untouched_account_is_admitted() =>
        Assert.True(throttle.Admits(Address, Source, Now));

    [Fact]
    public void Five_failures_against_one_address_close_it()
    {
        for (var attempt = 0; attempt < SignInThrottle.AttemptsPerAccount; attempt++)
        {
            Assert.True(throttle.Admits(Address, Source, Now));
            throttle.Failed(Address, Source, Now);
        }

        Assert.False(throttle.Admits(Address, Source, Now));

        // And nobody else's: the point of counting per account is that one
        // person's bad afternoon is not everybody's.
        Assert.True(throttle.Admits("somebody.else@example.com", Source, Now));
    }

    [Fact]
    public void It_drains_rather_than_latching()
    {
        for (var attempt = 0; attempt < SignInThrottle.AttemptsPerAccount; attempt++)
        {
            throttle.Failed(Address, Source, Now);
        }

        // Still closed a second before the window is out, and open the moment
        // the last failure has fallen out of it. Nobody lifted anything.
        Assert.False(throttle.Admits(
            Address, Source, Now + SignInThrottle.Window - TimeSpan.FromSeconds(1)));
        Assert.True(throttle.Admits(Address, Source, Now + SignInThrottle.Window));
    }

    [Fact]
    public void A_success_clears_the_account_and_leaves_the_source()
    {
        for (var attempt = 0; attempt < SignInThrottle.AttemptsPerAccount; attempt++)
        {
            throttle.Failed(Address, Source, Now);
        }

        throttle.Succeeded(Address);

        Assert.True(throttle.Admits(Address, Source, Now));

        // The source keeps what it accumulated: guessing one address correctly
        // proves nothing about the twenty it was trying, and clearing the
        // source's window on a success is precisely what a spraying attempt
        // would use.
        for (var attempt = SignInThrottle.AttemptsPerAccount;
             attempt < SignInThrottle.AttemptsPerSource;
             attempt++)
        {
            throttle.Failed($"nobody{attempt}@example.com", Source, Now);
        }

        Assert.False(throttle.Admits("another@example.com", Source, Now));
    }

    [Fact]
    public void Twenty_failures_from_one_source_close_it_whatever_they_were_against()
    {
        for (var attempt = 0; attempt < SignInThrottle.AttemptsPerSource; attempt++)
        {
            throttle.Failed($"nobody{attempt}@example.com", Source, Now);
        }

        Assert.False(throttle.Admits(Address, Source, Now));

        // Distributed across origins is what the per-account window is for, and
        // the source window is what keeps one origin from being a free oracle.
        Assert.True(throttle.Admits(Address, "198.51.100.4", Now));
    }

    [Fact]
    public void A_flood_of_addresses_evicts_rather_than_growing()
    {
        // Ten thousand distinct account keys, which is well past what the store
        // holds. Nothing here asserts a number of rows — the store is private —
        // what it asserts is that the throttle is still answering afterwards,
        // and still answering correctly for a key it was just told about.
        for (var attempt = 0; attempt < 10_000; attempt++)
        {
            throttle.Failed($"nobody{attempt}@example.com", $"198.51.100.{attempt % 256}", Now);
        }

        for (var attempt = 0; attempt < SignInThrottle.AttemptsPerAccount; attempt++)
        {
            throttle.Failed(Address, Source, Now);
        }

        Assert.False(throttle.Admits(Address, Source, Now));
    }
}
