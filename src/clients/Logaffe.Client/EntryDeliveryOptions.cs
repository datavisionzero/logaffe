using System.Diagnostics.CodeAnalysis;

namespace Logaffe.Client;

/// <summary>
/// What a sender says once, so that everything after it is
/// <see cref="EntryDelivery.Send"/>.
/// </summary>
/// <remarks>
/// <para>
/// The two required members are the whole of what identifies a delivery: the
/// installation's address, and the ingest token that says which project the
/// entries belong to. A delivery never names a project, because the token is the
/// project (<c>docs/ingestion.md</c>).
/// </para>
/// <para>
/// <b>What is not here is as deliberate as what is.</b> The batch limits — a
/// thousand entries, five mebibytes — are product values, the same in every
/// installation rather than something a sender tunes, so they live in
/// <see cref="EntryDelivery"/> as constants. The path is not settable either: it
/// is a promise to everything already sending, not a route that can be moved.
/// </para>
/// </remarks>
public sealed class EntryDeliveryOptions
{
    /// <summary>Options as a sender writes them, member by member.</summary>
    public EntryDeliveryOptions()
    {
    }

    /// <summary>
    /// The same options again, so that a package above this one can put its own
    /// default under a member the sender left unset.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A copy rather than a write into the caller's object: the options belong
    /// to whoever wrote them, who may be holding them for a second sender, and a
    /// default written into them would follow the object rather than the
    /// delivery it was meant for.
    /// </para>
    /// <para>
    /// <b>It carries every member.</b> A setting added above and not added here
    /// would be silently lost by everything that copies, which is why a test
    /// asks it of this constructor by reflection rather than by name.
    /// </para>
    /// </remarks>
    /// <param name="other">The options to copy.</param>
    [SetsRequiredMembers]
    public EntryDeliveryOptions(EntryDeliveryOptions other)
    {
        ArgumentNullException.ThrowIfNull(other);

        Installation = other.Installation;
        IngestToken = other.IngestToken;
        QueueCapacity = other.QueueCapacity;
        BatchInterval = other.BatchInterval;
        FlushTimeout = other.FlushTimeout;
        DeliveryTimeout = other.DeliveryTimeout;
        OnFailure = other.OnFailure;
    }

    /// <summary>
    /// Where this installation answers — scheme and host, as the operator reaches
    /// it. The ingest path is appended and is not a setting.
    /// </summary>
    public required Uri Installation { get; init; }

    /// <summary>
    /// The project's ingest token, travelling as a bearer credential. It permits
    /// writing and grants no read access of any kind.
    /// </summary>
    public required string IngestToken { get; init; }

    /// <summary>
    /// How many entries may wait in memory before the oldest are dropped.
    /// </summary>
    /// <remarks>
    /// Ten thousand is ten full batches, and it is chosen against the target
    /// <c>VISION.md</c> names — a handful of self-hosted services, not a fleet.
    /// It is large enough that an installation being restarted underneath a
    /// sender costs nothing, and small enough that an installation that stays
    /// unreachable costs a few megabytes rather than the application's memory.
    /// The queue is bounded rather than growing because an unbounded one turns a
    /// logging outage into an outage of the application, which is the one thing
    /// fire-and-forget exists to prevent.
    /// </remarks>
    public int QueueCapacity { get; init; } = 10_000;

    /// <summary>
    /// How long the first entry of a batch waits for company before it is sent
    /// on its own.
    /// </summary>
    /// <remarks>
    /// A second, because the log view's tail polls every five
    /// (<c>docs/ui.md</c>): waiting longer would be visible to an operator
    /// watching entries arrive, and waiting less would send an application's
    /// steady trickle one entry per request.
    /// </remarks>
    public TimeSpan BatchInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long <see cref="EntryDelivery.Dispose"/> spends trying to deliver
    /// what is still queued.
    /// </summary>
    /// <remarks>
    /// Five seconds is long enough for what a shutting-down application has in
    /// hand and short enough that a container being stopped is not held open by
    /// its logging. What does not go in that time is lost, which is what
    /// fire-and-forget means and why the application still has its own file.
    /// </remarks>
    public TimeSpan FlushTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long one delivery may take before it is abandoned.
    /// </summary>
    public TimeSpan DeliveryTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Where this reports what went wrong, which is the application's own local
    /// log.
    /// </summary>
    /// <remarks>
    /// <para>
    /// logaffe is additive: the application keeps its file logging, so the
    /// record of a delivery that failed already has somewhere to be, and it is
    /// not this installation — an installation that could not be reached cannot
    /// be told that it could not be reached.
    /// </para>
    /// <para>
    /// It is a delegate rather than an <c>ILogger</c> so that this package asks
    /// nothing of the application's logging stack; the two packages above it
    /// wire it to whichever one is there.
    /// </para>
    /// </remarks>
    public Action<string, Exception?>? OnFailure { get; init; }
}
