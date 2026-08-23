using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The operator's side of alerting over HTTP, asked of an installation that is
/// actually running.
/// </summary>
/// <remarks>
/// <para>
/// The property worth starting a composition root for is the one the other
/// endpoint tests are here for: <b>every one of these is behind the operator's
/// session</b>. It matters more here than elsewhere — what sits behind these
/// routes is where notifications go and a credential for that place.
/// </para>
/// <para>
/// The rest is what only a real installation answers: that the switches survive
/// a restart, that a notifier is stored sealed and read back through a route of
/// its own, and that a condition switched on with nothing to look at says which
/// of the three things is in the way rather than staying silent.
/// </para>
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class AlertEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Nowhere = "0195f0d4-0000-7000-8000-000000000000";

    private readonly string _volume = InstallationVolume.Create(nameof(AlertEndpointTests));

    private WebApplicationFactory<Program> _installation = null!;
    private string _secondFactorSecret = null!;

    public async ValueTask InitializeAsync()
    {
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Postgres", await postgres.CreateDatabaseAsync());
        Environment.SetEnvironmentVariable("Logaffe__VolumePath", _volume);

        _installation = new WebApplicationFactory<Program>();

        using var client = _installation.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

        var enrolled = await AClaimedInstallation.ClaimAsync(_installation, _volume);
        _secondFactorSecret = enrolled.SecondFactorSecret;
    }

    public async ValueTask DisposeAsync()
    {
        await _installation.DisposeAsync();
        InstallationVolume.Delete(_volume);
    }

    [Theory]
    [InlineData("GET", "/alerts")]
    [InlineData("PUT", "/alerts/switches")]
    [InlineData("PUT", "/alerts/host")]
    [InlineData("PUT", "/alerts/notifier")]
    [InlineData("DELETE", "/alerts/notifier")]
    [InlineData("GET", "/alerts/notifier/token")]
    [InlineData("POST", "/alerts/notifier/test")]
    [InlineData("GET", $"/hosts/{Nowhere}/mounts")]
    [InlineData("PUT", $"/projects/{Nowhere}/muted")]
    public async Task Every_alert_endpoint_is_behind_the_operator_s_session(
        string method, string path)
    {
        using var client = _installation.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new
            {
                fillingUp = true,
                goneQuiet = true,
                flooding = true,
                server = "https://ntfy.sh",
                topic = "logaffe",
                muted = true,
            }),
        };

        using var response = await client.SendAsync(
            request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_installation_nobody_has_asked_has_all_four_off_and_nowhere_to_send()
    {
        using var client = await SignedInAsync();

        var alerts = await ReadAsync(client);

        Assert.False(alerts.Switches.FillingUp);
        Assert.False(alerts.Switches.GoneQuiet);
        Assert.False(alerts.Switches.Flooding);
        Assert.False(alerts.Switches.Failing);

        Assert.Null(alerts.Notifier);

        // Every installation names no machine until the operator decides they
        // want the disk read, and that is the ordinary state rather than a
        // degraded one.
        Assert.Equal("noHostNamed", alerts.Store.Blindness);
        Assert.Null(alerts.Store.Percent);

        Assert.Empty(alerts.Fired);
    }

    [Fact]
    public async Task The_switches_are_written_as_one_setting_and_survive_a_restart()
    {
        using var client = await SignedInAsync();

        using (var put = await client.PutAsJsonAsync(
            "/alerts/switches",
            new { fillingUp = false, goneQuiet = true, flooding = true, failing = true },
            TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        // Read back through a second client of the same installation, which is
        // the row rather than anything held in the first one.
        using var second = await SignedInAsync();

        var alerts = await ReadAsync(second);

        Assert.False(alerts.Switches.FillingUp);
        Assert.True(alerts.Switches.GoneQuiet);
        Assert.True(alerts.Switches.Flooding);
        Assert.True(alerts.Switches.Failing);
    }

    [Fact]
    public async Task The_machine_and_its_mount_are_named_and_the_condition_says_what_it_sees()
    {
        using var client = await SignedInAsync();

        var host = await ReadAsync<HostBody>(await client.PostAsJsonAsync(
            "/hosts", new { name = "db" }, TestContext.Current.CancellationToken));

        // A machine that has never reported has no filesystems to offer, and an
        // empty answer is an ordinary one rather than a 404.
        var mounts = await ReadAsync<string[]>(await client.GetAsync(
            $"/hosts/{host.Id}/mounts", TestContext.Current.CancellationToken));

        Assert.Empty(mounts);

        await NameAsync(client, host.Id, "/var/lib/postgresql", HttpStatusCode.NoContent);

        var alerts = await ReadAsync(client);

        Assert.Equal(host.Id, alerts.Store.HostId);
        Assert.Equal("/var/lib/postgresql", alerts.Store.Mount);

        // Named, switched on and blind: the machine has never reported, so
        // there is no reading and the screen says which of the three reasons it
        // is rather than showing a per cent nothing stands behind.
        Assert.Equal("notReporting", alerts.Store.Blindness);
        Assert.Null(alerts.Store.Percent);

        // The pair goes together, so clearing the machine clears the mount.
        await NameAsync(client, null, null, HttpStatusCode.NoContent);

        Assert.Null((await ReadAsync(client)).Store.HostId);
    }

    [Fact]
    public async Task A_machine_that_is_gone_and_a_mount_that_is_not_one_are_told_apart()
    {
        using var client = await SignedInAsync();

        var host = await ReadAsync<HostBody>(await client.PostAsJsonAsync(
            "/hosts", new { name = "db" }, TestContext.Current.CancellationToken));

        // Nothing about the request is wrong and the address is what is gone.
        await NameAsync(client, Guid.Parse(Nowhere), "/var", HttpStatusCode.NotFound);

        // And here the machine is real and what was typed is not a path.
        await NameAsync(client, host.Id, "postgresql", HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_notifier_is_stored_sealed_read_back_on_a_route_of_its_own_and_cleared()
    {
        using var client = await SignedInAsync();

        await SetNotifierAsync(client, "https://ntfy.sh", "logaffe", "tk_secret");

        var alerts = await ReadAsync(client);

        Assert.NotNull(alerts.Notifier);
        Assert.Equal("https://ntfy.sh/", alerts.Notifier.Server);
        Assert.Equal("logaffe", alerts.Notifier.Topic);

        // The area says a token is there and never what it is: a screen showing
        // which server this installation notifies through has not read a secret.
        Assert.True(alerts.Notifier.HasAccessToken);

        var read = await ReadAsync<NotifierTokenBody>(await client.GetAsync(
            "/alerts/notifier/token", TestContext.Current.CancellationToken));

        Assert.Equal("tk_secret", read.Token);

        // A token that is not supplied is the token already there, so correcting
        // a topic is not re-typing a secret the screen cannot show.
        await SetNotifierAsync(client, "https://ntfy.sh", "logaffe-alerts", null);

        Assert.Equal(
            "tk_secret",
            (await ReadAsync<NotifierTokenBody>(await client.GetAsync(
                "/alerts/notifier/token", TestContext.Current.CancellationToken))).Token);

        // And the empty string is the public topic, which is what most
        // self-hosters are on.
        await SetNotifierAsync(client, "https://ntfy.sh", "logaffe-alerts", string.Empty);

        using (var none = await client.GetAsync(
            "/alerts/notifier/token", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
        }

        Assert.False((await ReadAsync(client)).Notifier!.HasAccessToken);

        using (var cleared = await client.DeleteAsync(
            "/alerts/notifier", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);
        }

        Assert.Null((await ReadAsync(client)).Notifier);
    }

    [Fact]
    public async Task What_is_not_a_server_and_what_is_not_a_topic_name_different_boxes()
    {
        using var client = await SignedInAsync();

        using (var server = await client.PutAsJsonAsync(
            "/alerts/notifier",
            new { server = "ntfy.sh", topic = "logaffe" },
            TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.BadRequest, server.StatusCode);
            Assert.Contains("\"server\"", await Body(server), StringComparison.Ordinal);
        }

        using var topic = await client.PutAsJsonAsync(
            "/alerts/notifier",
            new { server = "https://ntfy.sh", topic = "logaffe/alerts" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, topic.StatusCode);
        Assert.Contains("\"topic\"", await Body(topic), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_test_send_with_no_notifier_says_there_was_nowhere_to_send_it()
    {
        using var client = await SignedInAsync();

        var proof = await ReadAsync<TestNotificationBody>(await client.PostAsync(
            "/alerts/notifier/test", null, TestContext.Current.CancellationToken));

        // The one send in this product that answers, and this is the answer an
        // installation nobody has configured gets.
        Assert.Equal("noNotifier", proof.Proof);
    }

    [Fact]
    public async Task A_project_is_muted_from_its_own_settings_and_the_list_says_so()
    {
        using var client = await SignedInAsync();

        var project = await ReadAsync<ProjectBody>(await client.PostAsJsonAsync(
            "/projects",
            new { name = "api", retentionDays = 7 },
            TestContext.Current.CancellationToken));

        // Every project is evaluated until the operator says otherwise.
        Assert.False(project.Muted);

        using (var muted = await client.PutAsJsonAsync(
            $"/projects/{project.Id}/muted",
            new { muted = true },
            TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, muted.StatusCode);
        }

        Assert.True((await ReadAsync<ProjectBody>(await client.GetAsync(
            $"/projects/{project.Id}", TestContext.Current.CancellationToken))).Muted);

        // It rides on the list too, because the screen that sets it reads the
        // project off the list rather than fetching one of its own.
        Assert.True(Assert.Single(await ReadAsync<ListedProjectBody[]>(await client.GetAsync(
            "/projects", TestContext.Current.CancellationToken))).Muted);

        using var nowhere = await client.PutAsJsonAsync(
            $"/projects/{Nowhere}/muted",
            new { muted = true },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, nowhere.StatusCode);
    }

    private async Task NameAsync(
        HttpClient client, Guid? hostId, string? mount, HttpStatusCode expected)
    {
        using var response = await client.PutAsJsonAsync(
            "/alerts/host", new { hostId, mount }, TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
    }

    private async Task SetNotifierAsync(
        HttpClient client, string server, string topic, string? accessToken)
    {
        using var response = await client.PutAsJsonAsync(
            "/alerts/notifier",
            new { server, topic, accessToken },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private Task<AlertSettingsBody> ReadAsync(HttpClient client) =>
        ReadAsync<AlertSettingsBody>(client.GetAsync(
            "/alerts", TestContext.Current.CancellationToken));

    private async Task<HttpClient> SignedInAsync()
    {
        var client = _installation.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/sign-in",
            new
            {
                password = AClaimedInstallation.TheirPassword,
                secondFactorCode = Authenticator.CodeFor(_secondFactorSecret),
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);

        return client;
    }

    private static async Task<string> Body(HttpResponseMessage response) =>
        await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

    private static async Task<T> ReadAsync<T>(Task<HttpResponseMessage> sending) =>
        await ReadAsync<T>(await sending);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.True(
                response.IsSuccessStatusCode,
                $"{(int)response.StatusCode} from {response.RequestMessage?.RequestUri}");

            return (await response.Content.ReadFromJsonAsync<T>(
                TestContext.Current.CancellationToken))!;
        }
    }

    private sealed record HostBody(Guid Id, string Name);

    private sealed record ProjectBody(Guid Id, string Name, bool Muted);

    private sealed record ListedProjectBody(Guid Id, string Name, bool Muted);

    private sealed record NotifierBody(string Server, string Topic, bool HasAccessToken);

    private sealed record NotifierTokenBody(string Token);

    private sealed record TestNotificationBody(string Proof);

    private sealed record SwitchesBody(
        bool FillingUp, bool GoneQuiet, bool Flooding, bool Failing);

    private sealed record StoreBody(
        string Blindness, Guid? HostId, string? HostName, string? Mount, int? Percent);

    private sealed record FiredBody(Guid SubjectId, string Subject, string Condition);

    private sealed record AlertSettingsBody(
        NotifierBody? Notifier,
        SwitchesBody Switches,
        StoreBody Store,
        FiredBody[] Fired);
}
