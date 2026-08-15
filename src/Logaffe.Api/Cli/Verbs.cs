namespace Logaffe.Api.Cli;

/// <summary>
/// One binary is the server and the command line both. A recognized verb runs
/// and exits, no arguments at all is the server, and a first argument that was
/// meant as a verb and is not one is refused.
/// </summary>
/// <remarks>
/// <para>
/// Every verb is host-local by design and never reachable over the network
/// (ADR 0013). They are single words with at most a flag behind them, so they
/// are read as such — a parser would be machinery for a surface that is not
/// meant to grow.
/// </para>
/// <para>
/// <b>The token acts are deliberately not here.</b> These two are host-local
/// because they are the way back into an installation nobody can sign in to;
/// issuing, revoking or reading back a credential is not that kind of act. The
/// operator has a browser, the first-run guide hands them the snippet in it, and
/// a second surface for the same acts would be a second thing to keep true.
/// </para>
/// </remarks>
public static class Verbs
{
    /// <summary>Nothing ran, because the code to run it is not written yet.</summary>
    public const int NotImplemented = 70;

    /// <summary>The command line was wrong, so nothing ran. `EX_USAGE`.</summary>
    public const int Usage = 64;

    /// <summary>
    /// The verb is the first argument or there is none. Reading it from anywhere
    /// in the list would make <c>logaffe logaffe restore --yes</c> work by
    /// accident and leave <c>logaffe resotre</c> starting a server, which is the
    /// pair of failures this position exists to separate.
    /// </summary>
    public static bool TryRead(string[] args, out string verb)
    {
        verb = args.Length > 0 ? args[0] : string.Empty;
        return verb is "backup" or "restore" or "recover";
    }

    /// <summary>
    /// A bare first word was meant as a verb. Serving takes no arguments of its
    /// own — configuration reaches it as <c>--flags</c> and environment
    /// variables — so a word standing there is a mistake and not a request to
    /// serve.
    /// </summary>
    public static bool WasMeantAsAVerb(string[] args) =>
        args.Length > 0 && !args[0].StartsWith('-');

    /// <summary>
    /// Names the mistake rather than only refusing it, because the way to make
    /// it is to follow a working command that is typed the other way round:
    /// <c>exec</c> runs what it is given and has to name the binary,
    /// <c>run</c> hands its arguments to the entrypoint, which is this binary
    /// already.
    /// </summary>
    public static string NotAVerb(string argument) =>
        $"logaffe: '{argument}' is not one of backup, restore, recover.\n"
        + "Serving takes no argument, so this is a typo or one word too many — "
        + "`docker compose run --rm logaffe restore --yes` names it once, while "
        + "`docker compose exec logaffe logaffe backup` names it twice because "
        + "`exec` runs the command it is given.";

    public static Task<int> RunAsync(string verb, string[] args) => verb switch
    {
        "backup" => BackupCommand.RunAsync(args),
        "restore" => RestoreCommand.RunAsync(args),
        "recover" => RecoverCommand.RunAsync(args),
        _ => Task.FromResult(NotImplemented),
    };
}
