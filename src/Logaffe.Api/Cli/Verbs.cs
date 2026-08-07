namespace Logaffe.Api.Cli;

/// <summary>
/// One binary is the server and the command line both. A recognized verb runs
/// and exits; anything else starts the server.
/// </summary>
/// <remarks>
/// <para>
/// Both verbs are host-local by design and never reachable over the network
/// (ADR 0013). They are two words, so they are read as two words — a parser
/// would be machinery for a surface that is not meant to grow.
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

    public static bool TryRead(string[] args, out string verb)
    {
        verb = args.FirstOrDefault(argument => !argument.StartsWith('-')) ?? string.Empty;
        return verb is "backup" or "recover";
    }

    public static Task<int> RunAsync(string verb) => verb switch
    {
        "backup" => BackupCommand.RunAsync(),
        "recover" => RecoverCommand.RunAsync(),
        _ => Task.FromResult(NotImplemented),
    };
}
