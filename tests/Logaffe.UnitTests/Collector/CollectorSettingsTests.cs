using Logaffe.Collector;

namespace Logaffe.UnitTests.Collector;

/// <summary>
/// The three things a collector is told, and what it says when one of them is
/// wrong.
/// </summary>
/// <remarks>
/// Every reason is read by one person in one situation: they pasted the command
/// the installation handed over, edited it, and the host is not reporting. A
/// sentence that does not name the variable sends them back to the docs.
/// </remarks>
public sealed class CollectorSettingsTests
{
    private const string Token = "logaffe_host_3kf9q2_thesecretpart";

    [Fact]
    public void The_command_the_installation_hands_over_is_read_as_it_stands()
    {
        var read = CollectorSettings.TryRead(
            Given(("LOGAFFE_ENDPOINT", "https://logs.example.com"),
                  ("LOGAFFE_HOST_TOKEN", Token),
                  ("LOGAFFE_MOUNTS", "/")),
            out var settings,
            out _);

        Assert.True(read);
        Assert.Equal(new Uri("https://logs.example.com"), settings.Endpoint);
        Assert.Equal(Token, settings.Token);
        Assert.Equal(["/"], settings.Mounts);

        // The two bind mounts of that command, which are defaults rather than
        // settings.
        Assert.Equal("/host/proc", settings.ProcPath);
        Assert.Equal("/rootfs", settings.RootPath);
    }

    [Fact]
    public void An_ingest_token_pasted_here_is_caught_before_the_first_post()
    {
        var read = CollectorSettings.TryRead(
            Given(("LOGAFFE_ENDPOINT", "https://logs.example.com"),
                  ("LOGAFFE_HOST_TOKEN", "logaffe_ingest_3kf9q2_thesecretpart")),
            out _,
            out var reason);

        // Otherwise it is a machine that quietly 401s once a minute, and a host
        // that never reports with nothing to say why.
        Assert.False(read);
        Assert.Contains("LOGAFFE_HOST_TOKEN", reason);
        Assert.Contains("logaffe_host_", reason);
    }

    [Theory]
    [InlineData("logs.example.com")]
    [InlineData("ftp://logs.example.com")]
    [InlineData("not an address")]
    public void An_endpoint_that_is_not_an_address_is_named_as_the_variable_it_is(string endpoint)
    {
        var read = CollectorSettings.TryRead(
            Given(("LOGAFFE_ENDPOINT", endpoint), ("LOGAFFE_HOST_TOKEN", Token)),
            out _,
            out var reason);

        Assert.False(read);
        Assert.Contains("LOGAFFE_ENDPOINT", reason);
    }

    [Fact]
    public void A_missing_setting_says_which_one_and_what_it_is_for()
    {
        Assert.False(CollectorSettings.TryRead(Given(), out _, out var noEndpoint));
        Assert.Contains("LOGAFFE_ENDPOINT", noEndpoint);

        Assert.False(CollectorSettings.TryRead(
            Given(("LOGAFFE_ENDPOINT", "https://logs.example.com")), out _, out var noToken));
        Assert.Contains("LOGAFFE_HOST_TOKEN", noToken);
    }

    [Fact]
    public void Several_mounts_are_a_list_and_the_spaces_around_them_are_not_paths()
    {
        Assert.True(CollectorSettings.TryRead(
            Given(("LOGAFFE_ENDPOINT", "https://logs.example.com"),
                  ("LOGAFFE_HOST_TOKEN", Token),
                  ("LOGAFFE_MOUNTS", "/, /data ,/var/lib/docker,")),
            out var settings,
            out _));

        Assert.Equal(["/", "/data", "/var/lib/docker"], settings.Mounts);
    }

    [Fact]
    public void A_collector_watching_no_filesystem_still_reports_the_machine()
    {
        // The processor, the memory and the load are not filesystems, so an
        // empty list is a configuration rather than a mistake.
        Assert.True(CollectorSettings.TryRead(
            Given(("LOGAFFE_ENDPOINT", "https://logs.example.com"),
                  ("LOGAFFE_HOST_TOKEN", Token)),
            out var settings,
            out _));

        Assert.Empty(settings.Mounts);
    }

    [Fact]
    public void A_mount_that_is_not_an_absolute_path_is_refused_by_name()
    {
        var read = CollectorSettings.TryRead(
            Given(("LOGAFFE_ENDPOINT", "https://logs.example.com"),
                  ("LOGAFFE_HOST_TOKEN", Token),
                  ("LOGAFFE_MOUNTS", "/,var/lib")),
            out _,
            out var reason);

        Assert.False(read);
        Assert.Contains("LOGAFFE_MOUNTS", reason);
        Assert.Contains("var/lib", reason);
    }

    private static Func<string, string?> Given(params (string Name, string Value)[] set)
    {
        var environment = set.ToDictionary(one => one.Name, one => one.Value, StringComparer.Ordinal);

        return name => environment.GetValueOrDefault(name);
    }
}
