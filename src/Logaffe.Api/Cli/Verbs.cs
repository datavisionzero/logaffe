namespace Logaffe.Api.Cli;

/// <summary>
/// One binary is the server and the command line both. A recognized verb runs
/// and exits; anything else starts the server.
/// </summary>
/// <remarks>
/// Both verbs are host-local by design and never reachable over the network
/// (ADR 0013). They are two words, so they are read as two words — a parser
/// would be machinery for a surface that is not meant to grow.
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
