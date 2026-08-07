using System.Threading.RateLimiting;
using Logaffe.Api.Hosting;
using Microsoft.AspNetCore.RateLimiting;

namespace Logaffe.Api.Http;

/// <summary>
/// The limits every publicly reachable surface carries, which `VISION.md`
/// requires of all of them and <c>docs/setup.md</c> requires of this one in
/// particular.
/// </summary>
public static class PublicRateLimits
{
    /// <summary>
    /// The throttle in front of the sign-in, which is public, reachable by
    /// anyone, and the only place a password can be guessed at over the network.
    /// </summary>
    public const string SignIn = "sign-in";

    /// <summary>
    /// A burst of attempts a person mistyping their password actually makes.
    /// </summary>
    private const int Burst = 5;

    /// <summary>
    /// What the bucket refills at once the burst is gone, which is what turns
    /// the sixth attempt into a wait and the eighth into a longer one — the
    /// growing delay of ADR 0017.
    /// </summary>
    private static readonly TimeSpan Refill = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How many attempts are held waiting rather than refused. It is small on
    /// purpose: a queue is a request kept open, and the point is to slow a
    /// guesser down rather than to hold their connections for them.
    /// </summary>
    private const int Waiting = 2;

    public static IServiceCollection AddLogaffeRateLimits(this IServiceCollection services) =>
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Partitioned by where the attempt came from, and never by the
            // account: with exactly one account, throttling by account is a
            // lockout, and a lockout is a weapon pointed at its owner
            // (ADR 0017). What that address is worth behind a proxy is
            // RequestSource's business.
            limiter.AddPolicy(SignIn, context => RateLimitPartition.GetTokenBucketLimiter(
                context.SeenFrom() ?? "unknown",
                _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = Burst,
                    TokensPerPeriod = 1,
                    ReplenishmentPeriod = Refill,
                    QueueLimit = Waiting,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true,
                }));

            limiter.OnRejected = (context, cancellationToken) =>
            {
                // Saying when to come back is not a disclosure — the limit is
                // documented — and a client that is told waits instead of
                // hammering.
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var after))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)after.TotalSeconds).ToString();
                }

                return ValueTask.CompletedTask;
            };
        });
}
