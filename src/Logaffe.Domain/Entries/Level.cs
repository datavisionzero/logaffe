namespace Logaffe.Domain.Entries;

/// <summary>
/// The severity a sender assigned to an entry.
/// </summary>
/// <remarks>
/// The numeric values are what is stored, and they are ordered so that a
/// threshold is a comparison. The partial index of <c>docs/storage.md</c> is
/// defined over <c>level &gt;= 3</c>, so "Warning and above" is a property of
/// these numbers rather than only of their names.
/// </remarks>
public enum Level
{
    Verbose = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Fatal = 5,
}

/// <summary>
/// Reading a level off the wire.
/// </summary>
public static class Levels
{
    /// <summary>
    /// Per CLEF, an entry that names no level is <see cref="Level.Information"/>.
    /// This is what keeps the <c>curl</c> case short.
    /// </summary>
    public const Level WhenAbsent = Level.Information;

    /// <summary>
    /// Parses a level name case-insensitively. Both spellings are accepted:
    /// Serilog's six, and the two names <c>Microsoft.Extensions.Logging</c> uses
    /// for the ends of the same scale. A name that is neither makes the entry
    /// invalid — it is never quietly coerced, because a wrong level is worse
    /// than a counted rejection the operator can see.
    /// </summary>
    public static bool TryParse(string? name, out Level level)
    {
        switch (name?.Trim().ToLowerInvariant())
        {
            case "verbose":
            case "trace":
                level = Level.Verbose;
                return true;
            case "debug":
                level = Level.Debug;
                return true;
            case "information":
                level = Level.Information;
                return true;
            case "warning":
                level = Level.Warning;
                return true;
            case "error":
                level = Level.Error;
                return true;
            case "fatal":
            case "critical":
                level = Level.Fatal;
                return true;
            default:
                level = default;
                return false;
        }
    }
}
