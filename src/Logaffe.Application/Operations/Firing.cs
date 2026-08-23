using Logaffe.Application.Ports;
using Logaffe.Domain.Alerts;

namespace Logaffe.Application.Operations;

/// <summary>
/// What has already been said about a condition, and whether it leaves anything
/// to say now.
/// </summary>
/// <remarks>
/// The four conditions share this and only this. What each of them decides —
/// how full is too full, how quiet is too quiet, how many is too many, how long
/// too many has to last — is its own and is where the whole of the arithmetic
/// lives; what none of them decides on its own is whether a thing that is true
/// is also worth saying again, and that answer has to be the same for all four
/// or the guarding is four guardings (ADR 0050).
/// </remarks>
internal static class Firing
{
    /// <summary>
    /// Whether an alert at <paramref name="level"/> is due for this subject, and
    /// writes down whatever the answer changed.
    /// </summary>
    /// <remarks>
    /// A condition that has fallen back is armed again and written down without
    /// a word being sent: nothing is emitted when a condition clears, and a
    /// re-arming a restart lost would leave the next real event unsaid.
    /// </remarks>
    public static async Task<bool> DecideAsync(
        IConditionStates states,
        Guid subjectId,
        AlertCondition condition,
        int level,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var state = await states.FindAsync(subjectId, condition, cancellationToken)
            ?? ConditionState.For(subjectId, condition);

        var armed = state.Holds(level);

        if (!state.Fires(level, now))
        {
            if (armed)
            {
                await states.RecordAsync(state, cancellationToken);
            }

            return false;
        }

        state.Fired(level, now);
        await states.RecordAsync(state, cancellationToken);

        return true;
    }
}
