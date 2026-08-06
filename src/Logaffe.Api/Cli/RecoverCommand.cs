namespace Logaffe.Api.Cli;

/// <summary>
/// <c>docker compose exec logaffe logaffe recover</c>
/// </summary>
/// <remarks>
/// Returns the installation to unclaimed and arms a fresh claim window, keeping
/// its projects, tokens and entries (ADR 0013). It is the only route back into a
/// claimed installation, and every use is written to logaffe's own file log,
/// which is the one place a record of it survives the reset it performs.
/// </remarks>
public static class RecoverCommand
{
    public static Task<int> RunAsync()
    {
        Console.Error.WriteLine(
            "logaffe recover is not implemented yet. It has to return the installation "
            + "to unclaimed and arm a fresh claim window; see docs/setup.md and ADR 0013.");

        return Task.FromResult(Verbs.NotImplemented);
    }
}
