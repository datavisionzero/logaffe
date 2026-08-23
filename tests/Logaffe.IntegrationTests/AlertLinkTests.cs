using Logaffe.Domain.Alerts;
using Logaffe.Infrastructure.Alerts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Logaffe.IntegrationTests;

/// <summary>
/// Where a notification points, which needs no database and lives here because
/// this is the project that can see an adapter.
/// </summary>
/// <remarks>
/// The link is the better half of an alert (ADR 0049), so what is worth proving
/// is that it lands somewhere legible: the flooding hour on that project rather
/// than the front door, and nothing at all on an installation that was never
/// told its own address.
/// </remarks>
public sealed class AlertLinkTests
{
    private static readonly Guid Project = Guid.Parse("4c1f8b6e-0000-4000-8000-000000000001");
    private static readonly Guid Machine = Guid.Parse("4c1f8b6e-0000-4000-8000-000000000002");

    [Fact]
    public void A_flood_lands_on_the_hour_it_fired_on() =>
        Assert.Equal(
            $"https://logs.example.com/project/{Project}"
            + "?from=2026-08-22T03%3A00%3A00Z&until=2026-08-22T04%3A00%3A00Z",
            Links("https://logs.example.com").For(new Alert.ProjectFlooding(
                Project,
                "shop / api",
                new DateTimeOffset(2026, 8, 22, 3, 0, 0, TimeSpan.Zero),
                12_000,
                300))?.ToString());

    /// <summary>
    /// The same hour as a flood's, narrowed to what the condition counted. The
    /// level is a threshold rather than a selection, so <c>Error</c> is Error and
    /// Fatal — the tally's second number exactly, and therefore the entries
    /// behind the figure in the notification.
    /// </summary>
    [Fact]
    public void A_failure_lands_on_that_hours_errors() =>
        Assert.Equal(
            $"https://logs.example.com/project/{Project}"
            + "?from=2026-08-22T03%3A00%3A00Z&until=2026-08-22T04%3A00%3A00Z"
            + "&minimumLevel=Error",
            Links("https://logs.example.com").For(new Alert.ProjectFailing(
                Project,
                "shop / api",
                new DateTimeOffset(2026, 8, 22, 3, 0, 0, TimeSpan.Zero),
                4_000,
                2_500,
                2))?.ToString());

    /// <summary>
    /// An hour of a project that has stopped delivering is a screen that says
    /// nothing, so the span is wide enough to hold the silence and the last
    /// thing that arrived before it.
    /// </summary>
    [Theory]
    [InlineData(2, "1d")]
    [InlineData(22, "1d")]
    [InlineData(23, "1w")]
    [InlineData(400, "1w")]
    public void A_silence_lands_on_a_span_wide_enough_to_hold_it(int hours, string span) =>
        Assert.Equal(
            $"https://logs.example.com/project/{Project}?range={span}",
            Links("https://logs.example.com")
                .For(new Alert.ProjectGoneQuiet(Project, "shop / api", hours, 1))?.ToString());

    /// <summary>
    /// The disk is about a machine rather than a project, so it lands on the
    /// screen the filesystem readings it was decided on are drawn.
    /// </summary>
    [Fact]
    public void A_filling_store_lands_on_the_machine() =>
        Assert.Equal(
            $"https://logs.example.com/settings/hosts/{Machine}",
            Links("https://logs.example.com")
                .For(new Alert.StoreFillingUp(Machine, "vps-1", 91, 85))?.ToString());

    /// <summary>An ntfy behind a proxy is commonly under a path, and so is this.</summary>
    [Fact]
    public void An_installation_under_a_path_keeps_it() =>
        Assert.Equal(
            $"https://example.com/logs/settings/hosts/{Machine}",
            Links("https://example.com/logs")
                .For(new Alert.StoreFillingUp(Machine, "vps-1", 91, 85))?.ToString());

    /// <summary>
    /// The alert is still worth having; a link to a container port is not.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("logs.example.com")]
    [InlineData("ftp://logs.example.com")]
    public void An_installation_that_was_not_told_its_address_links_to_nothing(string? configured)
    {
        var links = Links(configured);

        Assert.Null(links.Home);
        Assert.Null(links.For(new Alert.StoreFillingUp(Machine, "vps-1", 91, 85)));
    }

    [Fact]
    public void A_test_notification_points_at_the_installation() =>
        Assert.Equal("https://logs.example.com/", Links("https://logs.example.com").Home?.ToString());

    private static AlertLinks Links(string? publicUrl) => AlertLinks.From(
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [AlertLinks.PublicUrlKey] = publicUrl,
            })
            .Build(),
        NullLogger<AlertLinks>.Instance);
}
