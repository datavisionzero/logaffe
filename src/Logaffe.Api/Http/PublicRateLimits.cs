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
    /// The throttle in front of the two acts that take an installation. It does
    /// not stop somebody who wins the race honestly — nothing can, and
    /// <c>VISION.md</c> accepts that — it stops the automated attempt at the
    /// password afterwards, and it keeps drawing enrolments from being free.
    /// </summary>
    public const string Claim = "claim";

    /// <summary>
    /// The throttle on the one question an unclaimed installation answers for
    /// nothing: whether it has an operator. It is a single row read and it says
    /// what the screen says, so it is limited to keep it from being a free
    /// heartbeat rather than because the answer is worth anything.
    /// </summary>
    public const string ClaimState = "claim-state";

    /// <summary>
    /// The throttle on everything behind the operator's session. It is generous,
    /// because the only repeating request the interface makes is the tail of the
    /// view being watched (<c>docs/ui.md</c>) — it is here so that a stolen
    /// cookie cannot walk the installation at machine speed, not to ration an
    /// operator who is working.
    /// </summary>
    public const string Operator = "operator";

    /// <summary>
    /// The throttle in front of the deliveries. It is what <c>VISION.md</c> asks
    /// abuse protection on the ingestion endpoint for: keeping an
    /// unauthenticated flood or a misbehaving deployment from filling the store,
    /// and not defending against the sending applications, which are the
    /// operator's own.
    /// </summary>
    public const string Ingest = "ingest";

    /// <summary>
    /// The throttle in front of the samples. It is a bucket of its own rather
    /// than the deliveries', for the reason the claim has one beside the
    /// sign-in: the two never compete for anything, so sharing a partition would
    /// only mean a fleet's collectors spending an application's delivery budget
    /// on the day they sit behind one address.
    /// </summary>
    public const string Sample = "sample";

    /// <summary>
    /// The throttle in front of the MCP tools — the five a reading token earns
    /// and the twenty-one an administering one does, on the one endpoint they
    /// share (ADR 0046) — which are publicly reachable like everything else this
    /// product exposes. An agent calls because the operator asked — there is no
    /// poll and no subscription behind this door (<c>docs/mcp.md</c>) — so what
    /// it stands in front of is a loop that got away from a model rather than
    /// ordinary use.
    /// </summary>
    public const string Agent = "agent";

    /// <summary>
    /// A burst of attempts a person mistyping their password actually makes.
    /// </summary>
    private const int Burst = 5;

    /// <summary>
    /// How many deliveries one source gets per minute. At the thousand entries a
    /// batch may carry, it is ten thousand entries a second — which is what
    /// <c>docs/storage.md</c> measured this installation sustaining, so the
    /// limit sits where the store does rather than below it.
    /// </summary>
    private const int IngestPerMinute = 600;

    /// <summary>
    /// How many readings one source gets per minute. A machine reports once a
    /// minute, so this is a hundred of them behind one address — and anything
    /// above it is not a collector, whatever it says in its header.
    /// </summary>
    private const int SamplePerMinute = 120;

    /// <summary>How many requests a signed-in browser gets per minute.</summary>
    private const int OperatorPerMinute = 300;

    /// <summary>
    /// How many tool calls one source gets per minute. Below the operator's,
    /// because every one of these may be a five-second read and a hundred of
    /// them a minute is already more than the store can serve — and well above
    /// what answering a question takes, which is a handful.
    /// </summary>
    private const int AgentPerMinute = 120;

    /// <summary>
    /// How often the claim screen may ask whether the installation is still
    /// unclaimed. It is generous because it is a page load rather than a poll —
    /// the screen is given a deadline and counts down to it in the browser.
    /// </summary>
    private const int ClaimStatePerMinute = 60;

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

            // The same bucket the sign-in gets, and a separate one rather than
            // the same policy: the two never compete — an unclaimed
            // installation has nobody to sign in and a claimed one cannot be
            // claimed — so sharing a partition would only mean one surface
            // spending the other's budget on the day they overlap.
            limiter.AddPolicy(Claim, context => RateLimitPartition.GetTokenBucketLimiter(
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

            limiter.AddPolicy(ClaimState, context => RateLimitPartition.GetFixedWindowLimiter(
                context.SeenFrom() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = ClaimStatePerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            // Partitioned by source here as well rather than by session, so that
            // a request arriving with no session at all — which is most of what
            // an intruder sends — is counted like any other.
            limiter.AddPolicy(Operator, context => RateLimitPartition.GetFixedWindowLimiter(
                context.SeenFrom() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = OperatorPerMinute,
                    Window = TimeSpan.FromMinutes(1),

                    // Nothing waits here. Holding an operator's request open to
                    // smooth a burst would make the interface feel broken, and
                    // there is no guessing to slow down behind the door.
                    QueueLimit = 0,
                }));

            // By source and not by the agent token, for the reason the ingest
            // limit below is: the limiter runs before anything is
            // authenticated, so a partition read off the header would be one an
            // unauthenticated caller chooses.
            limiter.AddPolicy(Agent, context => RateLimitPartition.GetFixedWindowLimiter(
                context.SeenFrom() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = AgentPerMinute,
                    Window = TimeSpan.FromMinutes(1),

                    // Nothing waits. A tool call held open to smooth a burst
                    // spends the agent's own timeout on a request that has not
                    // started, and being told to come back is the better answer.
                    QueueLimit = 0,
                }));

            // By source, for the reason the deliveries are: the limiter runs
            // before anything is authenticated. What a shared bucket costs here
            // is smaller still — a collector spends one permit a minute, so a
            // fleet behind one address is nowhere near the limit and a flood is
            // nowhere near a fleet.
            limiter.AddPolicy(Sample, context => RateLimitPartition.GetFixedWindowLimiter(
                context.SeenFrom() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = SamplePerMinute,
                    Window = TimeSpan.FromMinutes(1),

                    // Nothing waits, for the deliveries' reason: a collector does
                    // not look at the answer and will not retry.
                    QueueLimit = 0,
                }));

            // By source, like the rest, and deliberately not by the token the
            // delivery presents. The limiter runs before anything is
            // authenticated, so a partition read off the header would be a
            // partition an unauthenticated caller chooses — and a flood that
            // wrote a fresh identifier into every request would spend a fresh
            // budget each time and be throttled by nothing at all. What that
            // costs is a fleet behind one address sharing a bucket, which at the
            // rate above is not a bucket they can empty.
            limiter.AddPolicy(Ingest, context => RateLimitPartition.GetFixedWindowLimiter(
                context.SeenFrom() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = IngestPerMinute,
                    Window = TimeSpan.FromMinutes(1),

                    // Nothing waits. A sender does not look at the answer and
                    // will not retry, so holding a delivery open to smooth a
                    // burst would buy it nothing and cost the installation a
                    // connection.
                    QueueLimit = 0,
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
