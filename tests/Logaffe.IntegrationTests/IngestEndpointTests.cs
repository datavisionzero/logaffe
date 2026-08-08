using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Logaffe.Application.Operations;
using Logaffe.Domain.Entries;
using Logaffe.Domain.Projects;
using Logaffe.Domain.Tokens;
using Logaffe.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The delivery path end to end, asked of an installation that is actually
/// running: the token, the format, the caps, and the rows that come out.
/// </summary>
/// <remarks>
/// <para>
/// <c>VISION.md</c> makes this the adoption barrier, and it is the one public
/// surface that is neither behind the operator's session nor part of the claim.
/// So what is asked here is mostly what it refuses — a token that admits nothing,
/// a batch that is too large — and the one thing no unit test can say, which is
/// that a line of CLEF over HTTP becomes a row in Postgres.
/// </para>
/// <para>
/// The deliveries go out as <c>curl</c> would send them, with the token in an
/// <c>Authorization</c> header and the body as bytes, because that is the path
/// <c>docs/ingestion.md</c> promises works with nothing installed.
/// </para>
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class IngestEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Happened = "2026-08-07T09:15:00.417Z";

    private readonly string _volume = Directory.CreateTempSubdirectory("logaffe-volume-").FullName;

    private string _connectionString = null!;
    private WebApplicationFactory<Program> _installation = null!;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await postgres.CreateDatabaseAsync();

        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _connectionString);
        Environment.SetEnvironmentVariable("Logaffe__VolumePath", _volume);

        _installation = new WebApplicationFactory<Program>();

        // The migrations run as a hosted service, so the first request is what
        // waits for them.
        using var client = _installation.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        await _installation.DisposeAsync();
        Directory.Delete(_volume, recursive: true);
    }

    [Fact]
    public async Task A_delivery_becomes_rows_in_the_project_its_token_names()
    {
        var (project, token) = await AdmittedAsync();

        var receipt = await DeliverAsync(
            token,
            $$"""{"@t":"{{Happened}}","@l":"Warning","@mt":"User {UserId} failed login from {Ip}","UserId":42,"Ip":"203.0.113.7","instance":"api-7c4f","SourceContext":"Orders.Api"}""",
            $$"""{"@t":"{{Happened}}","@l":"Error","@mt":"Disk full on /dev/sda1","@x":"System.IO.IOException: no space"}""");

        Assert.Equal(HttpStatusCode.OK, receipt.Status);
        Assert.Equal(2, receipt.Body!.Accepted);
        Assert.Equal(0, receipt.Body.Rejected);

        var stored = await StoredAsync(project);
        Assert.Equal(2, stored.Count);

        // Rendered by the server, once, on the way in (ADR 0005), and out of
        // properties the sender delivered as an ordinary part of the line.
        Assert.Equal("User 42 failed login from 203.0.113.7", stored[0].Rendered);
        Assert.Equal("User {UserId} failed login from {Ip}", stored[0].Template);
        Assert.Equal((short)Level.Warning, stored[0].Level);
        Assert.Equal("api-7c4f", stored[0].Instance);
        Assert.Equal("Orders.Api", stored[0].LoggerName);

        // A plain line renders to itself, and the exception is its own field
        // rather than being folded into the message.
        Assert.Equal("Disk full on /dev/sda1", stored[1].Rendered);
        Assert.Equal("System.IO.IOException: no space", stored[1].Exception);

        // The receipt is the installation's own clock, and it is what retention
        // counts from (ADR 0007).
        Assert.All(stored, entry => Assert.NotEqual(entry.EventTime, entry.ReceiptTime));
    }

    [Fact]
    public async Task The_curl_line_the_product_hands_over_delivers()
    {
        // The snippet of docs/setup.md as a request: one field it does not
        // carry is @l, because an absent level is Information and that is the
        // affordance the short case exists on.
        var (project, token) = await AdmittedAsync();

        var receipt = await DeliverAsync(
            token, $$"""{"@t":"{{Happened}}","@mt":"Hello from {Sender}","Sender":"curl"}""");

        Assert.Equal(1, receipt.Body!.Accepted);

        var only = Assert.Single(await StoredAsync(project));
        Assert.Equal("Hello from curl", only.Rendered);
        Assert.Equal((short)Level.Information, only.Level);
    }

    [Fact]
    public async Task A_batch_is_accepted_in_part_and_the_reasons_name_their_lines()
    {
        var (project, token) = await AdmittedAsync();

        var receipt = await DeliverAsync(
            token,
            $$"""{"@t":"{{Happened}}","@mt":"first"}""",
            "{ not json",
            $$"""{"@mt":"no clock"}""",
            $$"""{"@t":"{{Happened}}","@mt":"second"}""");

        // 200, because one broken line never costs the others: the sender will
        // not retry and will not look, so refusing the batch is a permanent,
        // silent loss (ADR 0006).
        Assert.Equal(HttpStatusCode.OK, receipt.Status);
        Assert.Equal(2, receipt.Body!.Accepted);
        Assert.Equal(2, receipt.Body.Rejected);
        Assert.Equal([2, 3], receipt.Body.Reasons.Select(reason => reason.Line));

        Assert.Equal(["first", "second"], (await StoredAsync(project)).Select(e => e.Rendered));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Bearer")]
    [InlineData("Bearer not-a-token")]
    [InlineData("Basic bG9nYWZmZQ==")]
    public async Task A_delivery_that_presents_nothing_usable_is_refused_and_told_nothing(
        string? authorization)
    {
        using var client = _installation.CreateClient();
        using var request = Delivery($$"""{"@t":"{{Happened}}","@mt":"first"}""");

        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // And nothing further: not whether the project exists, not whether the
        // token once did, not whether it was revoked.
        Assert.Empty(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_agent_token_is_refused_at_the_delivery_door()
    {
        // The prefix is what refuses each credential at the other's endpoint,
        // before the database is asked anything at all (ADR 0021).
        await AdmittedAsync();

        var receipt = await DeliverAsync(
            TokenText.Mint(TokenKind.Agent), $$"""{"@t":"{{Happened}}","@mt":"first"}""");

        Assert.Equal(HttpStatusCode.Unauthorized, receipt.Status);
    }

    [Fact]
    public async Task A_revoked_token_admits_nothing_from_the_next_delivery_on()
    {
        var (_, token) = await AdmittedAsync();

        await using (var context = ContextFor())
        {
            await context.IngestTokens
                .Where(issued => issued.Identifier == token.Identifier)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        var receipt = await DeliverAsync(token, $$"""{"@t":"{{Happened}}","@mt":"first"}""");

        Assert.Equal(HttpStatusCode.Unauthorized, receipt.Status);
    }

    [Fact]
    public async Task A_batch_over_the_entry_cap_is_refused_whole()
    {
        var (project, token) = await AdmittedAsync();

        var receipt = await DeliverAsync(
            token,
            [.. Enumerable.Range(0, Caps.EntriesPerBatch + 1).Select(
                index => $$"""{"@t":"{{Happened}}","@mt":"entry {{index}}"}""")]);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, receipt.Status);
        Assert.Empty(await StoredAsync(project));
    }

    [Fact]
    public async Task A_batch_over_the_size_cap_is_refused_whole()
    {
        var (project, token) = await AdmittedAsync();

        var receipt = await DeliverAsync(
            token,
            $$"""{"@t":"{{Happened}}","@mt":"{{new string('x', Caps.BatchBytes + 1)}}"}""");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, receipt.Status);
        Assert.Empty(await StoredAsync(project));
    }

    [Fact]
    public async Task A_gzipped_delivery_is_read_and_the_cap_counts_what_comes_out_of_it()
    {
        var (project, token) = await AdmittedAsync();

        var receipt = await DeliverAsync(
            token,
            gzip: true,
            $$"""{"@t":"{{Happened}}","@mt":"first"}""",
            $$"""{"@t":"{{Happened}}","@mt":"second"}""");

        Assert.Equal(2, receipt.Body!.Accepted);
        Assert.Equal(2, (await StoredAsync(project)).Count);

        // The bomb: a body that is a few kilobytes on the wire and more than the
        // cap once decompressed. The cap counts the decompressed bytes, so it
        // cannot be walked around this way.
        var bomb = await DeliverAsync(
            token,
            gzip: true,
            [.. Enumerable.Range(0, 200).Select(
                index => $$"""{"@t":"{{Happened}}","@mt":"{{new string('x', 40_000)}}"}""")]);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, bomb.Status);
    }

    [Fact]
    public async Task A_body_that_said_it_was_gzip_and_was_not_is_a_bad_request()
    {
        var (_, token) = await AdmittedAsync();

        using var client = _installation.CreateClient();
        using var request = Delivery($$"""{"@t":"{{Happened}}","@mt":"first"}""");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Text);
        request.Content!.Headers.ContentEncoding.Add("gzip");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Deliveries_keep_being_admitted_after_the_token_has_recorded_its_use()
    {
        // The coarse last-use write of ADR 0033 sits in front of every admitted
        // delivery, and a second one within the interval writes nothing. That it
        // does not also stop admitting is the part worth a running installation.
        var (project, token) = await AdmittedAsync();

        for (var delivery = 0; delivery < 3; delivery++)
        {
            var receipt = await DeliverAsync(
                token, $$"""{"@t":"{{Happened}}","@mt":"entry {{delivery}}"}""");

            Assert.Equal(1, receipt.Body!.Accepted);
        }

        Assert.Equal(3, (await StoredAsync(project)).Count);

        await using var context = ContextFor();
        var stored = await context.IngestTokens.SingleAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(stored.LastUsedAt);
    }

    [Fact]
    public async Task Identities_are_unique_across_deliveries_and_survive_a_restart()
    {
        var (project, token) = await AdmittedAsync();

        await DeliverAsync(token, $$"""{"@t":"{{Happened}}","@mt":"first"}""");
        await DeliverAsync(token, $$"""{"@t":"{{Happened}}","@mt":"second"}""");

        // A second installation on the same database is what a restart is. Its
        // counter has to seed from what the table holds rather than from one,
        // because the cursor of docs/querying.md is only total if the identity
        // is unique.
        await using (var restarted = new WebApplicationFactory<Program>())
        {
            using var client = restarted.CreateClient();
            using var request = Delivery($$"""{"@t":"{{Happened}}","@mt":"third"}""");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Text);

            using var response = await client.SendAsync(
                request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var stored = await StoredAsync(project);
        Assert.Equal(3, stored.Count);
        Assert.Equal(3, stored.Select(entry => entry.Id).Distinct().Count());
    }

    /// <summary>
    /// A project holding one ingest token, which is the state a delivery needs
    /// the installation in.
    /// </summary>
    private async Task<(Guid Project, TokenText Token)> AdmittedAsync()
    {
        var project = Project.Create("orders-api", RetentionWindow.OfDays(7), DateTimeOffset.UtcNow);

        using var scope = _installation.Services.CreateScope();
        var issue = scope.ServiceProvider.GetRequiredService<IssueIngestToken>();

        await using (var context = ContextFor())
        {
            context.Projects.Add(project);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var issued = await issue.ExecuteAsync(project.Id, TestContext.Current.CancellationToken);

        return (project.Id, issued.Token!.Token);
    }

    private Task<Receipt> DeliverAsync(TokenText token, params string[] lines) =>
        DeliverAsync(token, gzip: false, lines);

    private async Task<Receipt> DeliverAsync(TokenText token, bool gzip, params string[] lines)
    {
        using var client = _installation.CreateClient();
        using var request = Delivery(string.Join('\n', lines) + '\n', gzip);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Text);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        return new Receipt(
            response.StatusCode,
            response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<DeliveryReceipt>(
                    TestContext.Current.CancellationToken)
                : null);
    }

    private static HttpRequestMessage Delivery(string body, bool gzip = false)
    {
        var bytes = Encoding.UTF8.GetBytes(body);

        if (gzip)
        {
            using var compressed = new MemoryStream();
            using (var writer = new GZipStream(compressed, CompressionLevel.SmallestSize))
            {
                writer.Write(bytes);
            }

            bytes = compressed.ToArray();
        }

        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-ndjson");
        if (gzip)
        {
            content.Headers.ContentEncoding.Add("gzip");
        }

        return new HttpRequestMessage(HttpMethod.Post, "/ingest") { Content = content };
    }

    private async Task<List<StoredEntry>> StoredAsync(Guid projectId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = new NpgsqlCommand(
            """
            select id, level, logger_name, instance, message_template, rendered_message,
                   exception, event_time, receipt_time
            from log_entry
            where project_id = @project_id
            order by id
            """,
            connection);
        command.Parameters.AddWithValue("project_id", projectId);

        await using var reader = await command.ExecuteReaderAsync(
            TestContext.Current.CancellationToken);

        var entries = new List<StoredEntry>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            entries.Add(new StoredEntry(
                reader.GetInt64(0),
                reader.GetInt16(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetFieldValue<DateTimeOffset>(7),
                reader.GetFieldValue<DateTimeOffset>(8)));
        }

        return entries;
    }

    private LogaffeDbContext ContextFor() =>
        new(new DbContextOptionsBuilder<LogaffeDbContext>().UseNpgsql(_connectionString).Options);

    private sealed record Receipt(HttpStatusCode Status, DeliveryReceipt? Body);

    private sealed record DeliveryReceipt(
        int Accepted, int Rejected, IReadOnlyList<RejectedLine> Reasons);

    private sealed record RejectedLine(int Line, string Reason);

    private sealed record StoredEntry(
        long Id,
        short Level,
        string? LoggerName,
        string? Instance,
        string Template,
        string Rendered,
        string? Exception,
        DateTimeOffset EventTime,
        DateTimeOffset ReceiptTime);
}
