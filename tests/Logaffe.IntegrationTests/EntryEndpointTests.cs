using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Logaffe.Domain.Entries;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The read surface over HTTP, asked of an installation that is actually
/// running.
/// </summary>
/// <remarks>
/// <para>
/// The property worth starting a composition root for is that <b>every one of
/// these is behind the operator's session</b>. This is the surface that hands
/// out the log content itself, and a read reachable without a session is an
/// installation whose logs a stranger can page through — a one-line mistake away
/// from being true.
/// </para>
/// <para>
/// The rest is what the shape of a response has to get right and no unit test
/// can vouch for: that an entry reaches a consumer as named fields and never as
/// prose (ADR 0012), that the properties come back as the object they were
/// delivered as, and that a cursor handed out by one page is the one the next
/// page is asked for.
/// </para>
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class EntryEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string TheirPassword = "a passphrase they typed";
    private const string NoSuchProject = "0195f0d4-0000-7000-8000-000000000000";

    private static readonly DateTimeOffset Ten = new(2026, 8, 8, 10, 0, 0, TimeSpan.Zero);

    private readonly string _volume = InstallationVolume.Create(nameof(EntryEndpointTests));

    private WebApplicationFactory<Program> _installation = null!;
    private string _secondFactorSecret = null!;
    private string _connectionString = null!;
    private long _nextId = 1;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await postgres.CreateDatabaseAsync();

        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _connectionString);
        Environment.SetEnvironmentVariable("Logaffe__VolumePath", _volume);

        _installation = new WebApplicationFactory<Program>();

        using var client = _installation.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

        await ClaimAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _installation.DisposeAsync();
        InstallationVolume.Delete(_volume);
    }

    [Theory]
    [InlineData($"/projects/{NoSuchProject}/entries")]
    [InlineData($"/projects/{NoSuchProject}/entries/tail")]
    [InlineData($"/projects/{NoSuchProject}/entries/count")]
    [InlineData($"/projects/{NoSuchProject}/entries/1")]
    public async Task Every_read_is_behind_the_operator_s_session(string path)
    {
        using var client = _installation.CreateClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData($"/projects/{NoSuchProject}/entries")]
    [InlineData($"/projects/{NoSuchProject}/entries/tail")]
    [InlineData($"/projects/{NoSuchProject}/entries/count")]
    [InlineData($"/projects/{NoSuchProject}/entries/1")]
    public async Task There_is_no_reading_a_project_that_does_not_exist(string path)
    {
        using var client = await SignedInAsync();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_page_is_newest_first_and_carries_no_total()
    {
        using var client = await SignedInAsync();
        var project = await CreateAsync(client, "orders");

        await StoreAsync(project.Id, Ten.AddMinutes(-5), message: "Checkout started");
        await StoreAsync(project.Id, Ten, message: "Checkout failed");

        var page = await ReadAsync<PageBody>(await client.GetAsync(
            $"/projects/{project.Id}/entries", TestContext.Current.CancellationToken));

        Assert.Equal(["Checkout failed", "Checkout started"], page.Entries.Select(e => e.Message));
        Assert.Null(page.Next);

        // ADR 0012: named fields, and no rendered form of the entry anywhere in
        // the response. There is nothing here for a consumer to read as an
        // instruction because there is nothing here that is prose.
        var newest = page.Entries[0];
        Assert.Equal("Information", newest.Level);
        Assert.Equal(Ten, newest.EventTime);
        Assert.False(newest.HasException);
    }

    [Fact]
    public async Task A_page_that_filled_hands_out_the_cursor_of_the_next_one()
    {
        using var client = await SignedInAsync();
        var project = await CreateAsync(client, "orders");

        for (var minute = 0; minute < 120; minute++)
        {
            await StoreAsync(project.Id, Ten.AddMinutes(-minute), message: $"Line {minute}");
        }

        var first = await ReadAsync<PageBody>(await client.GetAsync(
            $"/projects/{project.Id}/entries", TestContext.Current.CancellationToken));

        Assert.Equal(100, first.Entries.Count);
        Assert.NotNull(first.Next);

        // Handed back unread, which is what opaque means: the caller passes on
        // the string it was given.
        var second = await ReadAsync<PageBody>(await client.GetAsync(
            $"/projects/{project.Id}/entries?cursor={Uri.EscapeDataString(first.Next)}",
            TestContext.Current.CancellationToken));

        // Neither repeating the last entry of the first page nor skipping the
        // one after it, and the short page is the last one.
        Assert.Equal(20, second.Entries.Count);
        Assert.Null(second.Next);
        Assert.Equal("Line 100", second.Entries[0].Message);
        Assert.Empty(first.Entries.Select(e => e.Id).Intersect(second.Entries.Select(e => e.Id)));
    }

    [Fact]
    public async Task The_first_poll_arms_the_tail_and_the_next_one_answers_what_arrived()
    {
        using var client = await SignedInAsync();
        var project = await CreateAsync(client, "orders");

        await StoreAsync(project.Id, Ten, message: "Checkout started");

        var armed = await TailAsync(client, project);

        // The view has just loaded its page, and what it needs is the position
        // to watch from rather than the entries it is already showing.
        Assert.Empty(armed.Entries);
        Assert.False(armed.More);
        Assert.NotNull(armed.Next);

        // A sender that was disconnected, delivering what happened before the
        // line the tail has already shown (ADR 0009).
        await StoreAsync(
            project.Id, Ten.AddMinutes(-10), message: "Checkout failed",
            receivedAt: Ten.AddSeconds(1));

        var polled = await TailAsync(client, project, armed.Next);

        Assert.Equal("Checkout failed", Assert.Single(polled.Entries).Message);
        Assert.False(polled.More);
        Assert.NotEqual(armed.Next, polled.Next);

        // And a quiet poll answers nothing and the same position again, so that
        // following the logs is a loop over the last answer.
        var quiet = await TailAsync(client, project, polled.Next);

        Assert.Empty(quiet.Entries);
        Assert.Equal(polled.Next, quiet.Next);
    }

    [Fact]
    public async Task A_poll_is_in_the_views_order_and_says_when_it_could_not_carry_it_all()
    {
        using var client = await SignedInAsync();
        var project = await CreateAsync(client, "orders");

        var armed = await TailAsync(client, project);

        // Delivered in the reverse of the order they happened in, and more of
        // them than one poll may carry.
        for (var i = 1; i <= 120; i++)
        {
            await StoreAsync(
                project.Id, Ten.AddSeconds(-i), message: $"Line {i}",
                receivedAt: Ten.AddSeconds(i));
        }

        var first = await TailAsync(client, project, armed.Next);

        // What is taken is the front of the arrival order; what it is answered
        // in is the order the view keeps.
        Assert.Equal(100, first.Entries.Count);
        Assert.Equal("Line 1", first.Entries[0].Message);
        Assert.Equal("Line 100", first.Entries[^1].Message);

        // An interval that cannot keep up says so rather than losing the middle:
        // the rest is waiting where the cursor stopped.
        Assert.True(first.More);

        var second = await TailAsync(client, project, first.Next);

        Assert.Equal(20, second.Entries.Count);
        Assert.False(second.More);
        Assert.Empty(
            first.Entries.Select(e => e.Id).Intersect(second.Entries.Select(e => e.Id)));
    }

    [Fact]
    public async Task A_tail_narrows_with_the_filters_in_its_address()
    {
        using var client = await SignedInAsync();
        var project = await CreateAsync(client, "orders");

        var armed = await TailAsync(client, project);

        await StoreAsync(
            project.Id, Ten, Level.Error, message: "Checkout failed",
            receivedAt: Ten.AddSeconds(1));
        await StoreAsync(
            project.Id, Ten, Level.Debug, message: "Checkout started",
            receivedAt: Ten.AddSeconds(2));

        var polled = await ReadAsync<TailBody>(await client.GetAsync(
            $"/projects/{project.Id}/entries/tail"
            + $"?since={Uri.EscapeDataString(armed.Next)}&minimumLevel=warning",
            TestContext.Current.CancellationToken));

        // The same seven narrowings as every other read: a tail is a filter set
        // that is being watched, not a mode with rules of its own.
        Assert.Equal("Checkout failed", Assert.Single(polled.Entries).Message);
    }

    [Fact]
    public async Task The_filters_are_in_the_address()
    {
        using var client = await SignedInAsync();
        var project = await CreateAsync(client, "orders");

        await StoreAsync(
            project.Id, Ten, Level.Error, "Orders.Api", "api-7c4f", message: "Checkout failed");
        await StoreAsync(
            project.Id, Ten, Level.Debug, "Orders.Api", "api-7c4f", message: "Checkout failed");
        await StoreAsync(
            project.Id, Ten.AddDays(-1), Level.Error, "Orders.Api", "api-7c4f",
            message: "Checkout failed");

        // A log view is a thing an operator links a colleague to and finds again
        // in their history (docs/ui.md), so every narrowing is a query parameter.
        var page = await ReadAsync<PageBody>(await client.GetAsync(
            $"/projects/{project.Id}/entries"
            + $"?from={Uri.EscapeDataString(Ten.AddHours(-1).ToString("O"))}"
            + $"&until={Uri.EscapeDataString(Ten.AddHours(1).ToString("O"))}"
            + "&minimumLevel=warning&loggerName=Orders.Api&instance=api-7c4f&search=failed",
            TestContext.Current.CancellationToken));

        Assert.Equal("Checkout failed", Assert.Single(page.Entries).Message);
    }

    [Fact]
    public async Task One_entry_comes_back_with_its_exception_and_its_properties()
    {
        using var client = await SignedInAsync();
        var project = await CreateAsync(client, "orders");

        var id = await StoreAsync(
            project.Id,
            Ten,
            Level.Error,
            "Orders.Api.CheckoutController",
            "api-7c4f",
            trace: "0af7651916cd43dd8448eb211c80319c",
            message: "Checkout 4711 failed",
            exception: "System.NullReferenceException: Object reference not set",
            properties: """{"UserId": 42, "Ip": "203.0.113.7"}""");

        using var response = await client.GetAsync(
            $"/projects/{project.Id}/entries/{id}", TestContext.Current.CancellationToken);
        var entry = await ReadAsync<EntryBody>(response);

        Assert.Equal("Error", entry.Level);
        Assert.Equal("Orders.Api.CheckoutController", entry.LoggerName);
        Assert.Equal("api-7c4f", entry.Instance);

        // The bytes are what is stored; the hex is what CLEF carried and what
        // goes back out, so a trace read off an entry can be pasted into the
        // filter that gathers the request.
        Assert.Equal("0af7651916cd43dd8448eb211c80319c", entry.Trace);
        Assert.StartsWith("System.NullReferenceException", entry.Exception);

        // The object it was delivered as, and not a string holding its text:
        // log content reaches a consumer as data (ADR 0012).
        Assert.Equal(JsonValueKind.Object, entry.Properties!.Value.ValueKind);
        Assert.Equal(42, entry.Properties.Value.GetProperty("UserId").GetInt32());
    }

    [Fact]
    public async Task An_entry_of_another_project_is_not_reachable_by_its_number()
    {
        using var client = await SignedInAsync();
        var project = await CreateAsync(client, "orders");
        var other = await CreateAsync(client, "billing");

        var id = await StoreAsync(project.Id, Ten);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync(
                $"/projects/{other.Id}/entries/{id}",
                TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task A_count_answers_a_number_and_a_grouped_one_answers_names()
    {
        using var client = await SignedInAsync();
        var project = await CreateAsync(client, "orders");

        await StoreAsync(project.Id, Ten, Level.Fatal);
        await StoreAsync(project.Id, Ten, Level.Information);
        await StoreAsync(project.Id, Ten, Level.Information);

        var plain = await ReadAsync<CountBody>(await client.GetAsync(
            $"/projects/{project.Id}/entries/count", TestContext.Current.CancellationToken));

        Assert.Null(Assert.Single(plain.Groups).Value);
        Assert.Equal(3, plain.Groups[0].Entries);

        var byLevel = await ReadAsync<CountBody>(await client.GetAsync(
            $"/projects/{project.Id}/entries/count?groupBy=level",
            TestContext.Current.CancellationToken));

        // The name and not the number the column holds: the contract speaks the
        // six severities everywhere else, and a grouped count is where the
        // stored form would otherwise leak out.
        Assert.Equal(["Fatal", "Information"], byLevel.Groups.Select(group => group.Value));
        Assert.Equal([1, 2], byLevel.Groups.Select(group => group.Entries));
    }

    [Fact]
    public async Task A_count_over_a_time_bucket_is_labelled_as_an_instant()
    {
        using var client = await SignedInAsync();
        var project = await CreateAsync(client, "orders");

        await StoreAsync(project.Id, Ten);
        await StoreAsync(project.Id, Ten.AddMinutes(30));
        await StoreAsync(project.Id, Ten.AddHours(2));

        var counted = await ReadAsync<CountBody>(await client.GetAsync(
            $"/projects/{project.Id}/entries/count?groupBy=time&bucket=hour",
            TestContext.Current.CancellationToken));

        Assert.Equal(
            ["2026-08-08T12:00:00Z", "2026-08-08T10:00:00Z"],
            counted.Groups.Select(group => group.Value));
        Assert.Equal([1, 2], counted.Groups.Select(group => group.Entries));
    }

    [Theory]

    // Two characters is not a narrower search, it is a scan of the project
    // (ADR 0025), and the rule binds the surface rather than one caller.
    [InlineData("entries?search=ab")]
    [InlineData("entries?exception=ab")]

    // A malformed question rather than an empty answer.
    [InlineData("entries?from=2026-08-08T11:00:00Z&until=2026-08-08T10:00:00Z")]

    // Values that could only ever match nothing, refused where they were typed.
    [InlineData("entries?minimumLevel=loud")]
    [InlineData("entries?trace=nothex")]
    [InlineData("entries?cursor=not-a-cursor")]
    [InlineData("entries/tail?since=not-a-cursor")]
    [InlineData("entries/count?groupBy=trace")]
    [InlineData("entries/count?groupBy=time&bucket=fortnight")]
    public async Task A_filter_that_cannot_be_read_is_refused_rather_than_run(string query)
    {
        using var client = await SignedInAsync();
        var project = await CreateAsync(client, "orders");

        using var response = await client.GetAsync(
            $"/projects/{project.Id}/{query}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// One poll of the tail, with the cursor the previous one handed back — or
    /// none at all, which is the poll that arms it.
    /// </summary>
    private async Task<TailBody> TailAsync(
        HttpClient client, ProjectBody project, string? since = null) =>
        await ReadAsync<TailBody>(await client.GetAsync(
            $"/projects/{project.Id}/entries/tail"
            + (since is null ? string.Empty : $"?since={Uri.EscapeDataString(since)}"),
            TestContext.Current.CancellationToken));

    private async Task<ProjectBody> CreateAsync(HttpClient client, string name) =>
        await ReadAsync<ProjectBody>(await client.PostAsJsonAsync(
            "/projects",
            new { name, retentionDays = 7 },
            TestContext.Current.CancellationToken));

    /// <summary>
    /// One entry, written straight into the table. The ingestion path that puts
    /// them there in production has its own tests; what is being asked here is
    /// what a read makes of a row.
    /// </summary>
    private async Task<long> StoreAsync(
        Guid projectId,
        DateTimeOffset at,
        Level level = Level.Information,
        string? loggerName = null,
        string? instance = null,
        string? trace = null,
        string? message = null,
        string? exception = null,
        string? properties = null,
        DateTimeOffset? receivedAt = null)
    {
        var id = _nextId++;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = new NpgsqlCommand(
            """
            insert into log_entry (
                id, project_id, event_time, receipt_time, level, logger_name, instance,
                trace_id, message_template, rendered_message, exception, properties,
                message_truncated, exception_truncated)
            values (
                @id, @project_id, @at, @received, @level, @logger_name, @instance,
                @trace_id, @text, @text, @exception, @properties::jsonb, false, false)
            """,
            connection);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("at", at);

        // The two clocks are the same instant unless a test is about them
        // disagreeing, which only the tail is (ADR 0009).
        command.Parameters.AddWithValue("received", receivedAt ?? at);
        command.Parameters.AddWithValue("level", (short)level);
        command.Parameters.AddWithValue("logger_name", (object?)loggerName ?? DBNull.Value);
        command.Parameters.AddWithValue("instance", (object?)instance ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "trace_id", trace is null ? DBNull.Value : Convert.FromHexString(trace));
        command.Parameters.AddWithValue("text", message ?? "Handled /orders");
        command.Parameters.AddWithValue("exception", (object?)exception ?? DBNull.Value);
        command.Parameters.AddWithValue("properties", (object?)properties ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        return id;
    }

    /// <inheritdoc cref="ProjectEndpointTests"/>
    private async Task<HttpClient> SignedInAsync()
    {
        var client = _installation.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/sign-in",
            new
            {
                password = TheirPassword,
                secondFactorCode = Authenticator.CodeFor(_secondFactorSecret),
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);

        return client;
    }

    /// <inheritdoc cref="ProjectEndpointTests"/>
    private async Task ClaimAsync()
    {
        using var client = _installation.CreateClient();

        var enrolment = await ReadAsync<Enrolment>(await client.PostAsync(
            "/claim/enrolment", null, TestContext.Current.CancellationToken));

        _secondFactorSecret = enrolment.SecondFactorSecret;

        using var claimed = await client.PostAsJsonAsync(
            "/claim",
            new
            {
                password = TheirPassword,
                ticket = enrolment.Ticket,
                secondFactorCode = Authenticator.CodeFor(enrolment.SecondFactorSecret),
                backupCode = enrolment.BackupCodes[0],
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, claimed.StatusCode);
    }

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

    private sealed record Enrolment(
        string SecondFactorSecret, IReadOnlyList<string> BackupCodes, string Ticket);

    private sealed record ProjectBody(
        Guid Id, string Name, int RetentionDays, DateTimeOffset CreatedAt);

    private sealed record PageBody(IReadOnlyList<ListedEntryBody> Entries, string? Next);

    /// <summary>
    /// The tail's answer. <c>Next</c> is not nullable here, deliberately: every
    /// poll carries the position of the next one, including the one that
    /// answered nothing and the one that armed the tail.
    /// </summary>
    private sealed record TailBody(IReadOnlyList<ListedEntryBody> Entries, string Next, bool More);

    private sealed record ListedEntryBody(
        long Id,
        DateTimeOffset EventTime,
        string Level,
        string? LoggerName,
        string? Instance,
        string? Trace,
        string Message,
        bool MessageTruncated,
        bool HasException);

    private sealed record EntryBody(
        long Id,
        DateTimeOffset EventTime,
        DateTimeOffset ReceiptTime,
        string Level,
        string? LoggerName,
        string? Instance,
        string? Trace,
        string? Span,
        string MessageTemplate,
        string Message,
        string? Exception,
        JsonElement? Properties,
        bool MessageTruncated,
        bool ExceptionTruncated);

    private sealed record CountBody(IReadOnlyList<CountedGroupBody> Groups);

    private sealed record CountedGroupBody(string? Value, long Entries);
}
