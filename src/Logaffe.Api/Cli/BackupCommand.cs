namespace Logaffe.Api.Cli;

/// <summary>
/// <c>docker compose exec logaffe logaffe backup &gt; logaffe-backup.tar</c>
/// </summary>
/// <remarks>
/// The artifact has to contain <em>both</em> halves — the database and the key
/// material on the host volume — because neither is useful without the other,
/// and an operator who backs up one and believes they are covered discovers it
/// at the moment they go looking for a token (ADR 0024).
/// </remarks>
public static class BackupCommand
{
    public static Task<int> RunAsync()
    {
        Console.Error.WriteLine(
            "logaffe backup is not implemented yet. It has to write both the database "
            + "and the key material into one artifact; see docs/operations.md and ADR 0024.");

        return Task.FromResult(Verbs.NotImplemented);
    }
}
