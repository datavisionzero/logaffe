namespace Logaffe.Domain.Projects;

/// <summary>
/// What one flush adds to one project's hour: the entries that arrived for it
/// since the last flush, and how many of those were <c>Error</c> or worse.
/// </summary>
/// <remarks>
/// It is not a <see cref="Tally"/> with small numbers in it. A tally is what an
/// hour came to; this is an amount to add to one, and the two are separate types
/// because writing the second where the first was meant would overwrite an hour
/// with a minute of it.
/// </remarks>
public sealed record TallyIncrement
{
    public required Guid ProjectId { get; init; }

    /// <summary>The hour these arrived in, per <see cref="Tallying.HourOf"/>.</summary>
    public required DateTimeOffset Hour { get; init; }

    public required long Entries { get; init; }

    public required long AtErrorOrAbove { get; init; }
}
