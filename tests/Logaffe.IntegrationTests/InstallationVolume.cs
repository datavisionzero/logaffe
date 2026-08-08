namespace Logaffe.IntegrationTests;

/// <summary>
/// The host volume of an installation a test starts, and what happens to it
/// afterwards.
/// </summary>
/// <remarks>
/// <para>
/// A test that starts a <c>WebApplicationFactory</c> starts logaffe, and logaffe
/// writes its own account of itself to this directory rather than into itself
/// (ADR 0002). Deleting it at the end of the class threw that away — which was
/// fine until a run failed on the runner and not here, and the only thing left
/// to read was what xUnit had printed.
/// </para>
/// <para>
/// So <c>LOGAFFE_TEST_VOLUMES</c> names a directory to put them under and keeps
/// them. CI sets it and collects the logs from a job that failed; nothing sets
/// it locally, where the temporary directory is still temporary and still goes.
/// </para>
/// <para>
/// It is only for the volumes with an installation behind them. A directory
/// holding one key for a cipher has nothing to say about a failure, and naming
/// it the same thing would suggest it did.
/// </para>
/// </remarks>
internal static class InstallationVolume
{
    private const string Collected = "LOGAFFE_TEST_VOLUMES";

    private const string Namespace = nameof(Logaffe) + "." + nameof(IntegrationTests);

    /// <summary>
    /// One volume, named for the test that will run against it so that the
    /// directory is findable from the failure that sent somebody looking.
    /// </summary>
    /// <remarks>
    /// Every test gets an installation of its own — the lifetime is per test,
    /// not per class — so a run leaves one of these per test, and the name is
    /// what makes that a help rather than a haystack.
    /// </remarks>
    public static string Create(string tests)
    {
        var collected = Environment.GetEnvironmentVariable(Collected);

        if (string.IsNullOrWhiteSpace(collected))
        {
            return Directory.CreateTempSubdirectory($"logaffe-{tests}-").FullName;
        }

        // Under the collected directory rather than beside it, and still unique:
        // a theory's cases share a display name once it is cut to a path.
        var path = Path.Combine(
            collected, $"{Named(tests)}-{Guid.NewGuid().ToString("n")[..8]}");

        Directory.CreateDirectory(path);

        return path;
    }

    /// <summary>
    /// What the test is called, as much of it as belongs in a path.
    /// </summary>
    private static string Named(string fallback)
    {
        var test = TestContext.Current.Test?.TestDisplayName;

        if (string.IsNullOrWhiteSpace(test))
        {
            return fallback;
        }

        // Without this project's namespace in front of every one of them, which
        // is twenty-six characters that say nothing and crowd out a theory's
        // parameters — the part that says which case it was.
        var named = test.StartsWith($"{Namespace}.", StringComparison.Ordinal)
            ? test[(Namespace.Length + 1)..]
            : test;

        var written = new string(
            [.. named.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' ? c : '-')]);

        return written.Length > 120 ? written[..120] : written;
    }

    /// <summary>
    /// Takes it away again, unless this run is keeping what the installations
    /// wrote.
    /// </summary>
    public static void Delete(string volume)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Collected)))
        {
            return;
        }

        Directory.Delete(volume, recursive: true);
    }
}
