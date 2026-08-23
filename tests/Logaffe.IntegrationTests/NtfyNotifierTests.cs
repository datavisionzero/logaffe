using System.Net;
using System.Text;
using System.Text.Json;
using Logaffe.Application.Ports;
using Logaffe.Domain.Alerts;
using Logaffe.Domain.Hosts;
using Logaffe.Domain.Operators;
using Logaffe.Domain.Projects;
using Logaffe.Infrastructure.Alerts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The one notifier, asked what it puts on the wire and what it does when the
/// wire is not there. It needs no database and lives here because this is the
/// project that can see an adapter.
/// </summary>
/// <remarks>
/// Three things are worth proving and they are the three the product promised:
/// what leaves carries names and numbers and nothing an entry said (ADR 0049),
/// a failure costs one line in the installation's own log and no retry
/// (ADR 0002), and the operator's test send answers rather than logging.
/// </remarks>
public sealed class NtfyNotifierTests
{
    private static readonly Guid Project = Guid.Parse("4c1f8b6e-0000-4000-8000-000000000001");
    private static readonly Guid Machine = Guid.Parse("4c1f8b6e-0000-4000-8000-000000000002");

    private static readonly Alert.ProjectFlooding Flood = new(
        Project,
        "shop / api",
        new DateTimeOffset(2026, 8, 22, 3, 0, 0, TimeSpan.Zero),
        12_000,
        300);

    private readonly TakingNotifications _ntfy = new();
    private readonly RecordingLog _log = new();

    [Fact]
    public async Task What_arrives_is_the_topic_the_numbers_and_the_link()
    {
        await Notifier().SendAsync(Flood, TestContext.Current.CancellationToken);

        var published = Assert.Single(_ntfy.Taken).Published;

        Assert.Equal("logaffe", published.GetProperty("topic").GetString());
        Assert.Equal("logaffe: shop / api is flooding", published.GetProperty("title").GetString());
        Assert.Equal(
            "12000 entries in the hour from 2026-08-22 03:00 UTC, against a usual 300.",
            published.GetProperty("message").GetString());
        Assert.Equal(
            $"https://logs.example.com/project/{Project}"
            + "?from=2026-08-22T03%3A00%3A00Z&until=2026-08-22T04%3A00%3A00Z",
            published.GetProperty("click").GetString());
    }

    [Fact]
    public async Task A_silence_says_how_long_and_how_long_is_ordinary()
    {
        await Notifier().SendAsync(
            new Alert.ProjectGoneQuiet(Project, "shop / api", 7, 5),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "Nothing received for 7 hours, against 5 this project is ordinarily quiet for.",
            Assert.Single(_ntfy.Taken).Published.GetProperty("message").GetString());
    }

    [Fact]
    public async Task A_filling_store_says_the_figure_and_the_threshold()
    {
        await Notifier().SendAsync(
            new Alert.StoreFillingUp(Machine, "vps-1", 91, 85),
            TestContext.Current.CancellationToken);

        var published = Assert.Single(_ntfy.Taken).Published;

        Assert.Equal("logaffe: vps-1 is filling up", published.GetProperty("title").GetString());
        Assert.Equal(
            "The filesystem holding the database is 91 per cent full, past 85.",
            published.GetProperty("message").GetString());
    }

    /// <summary>
    /// A project is called what the operator called it, and a header is ASCII.
    /// The JSON form is what carries the name as it was written.
    /// </summary>
    [Fact]
    public async Task A_name_survives_the_way_it_was_written()
    {
        await Notifier().SendAsync(
            new Alert.ProjectGoneQuiet(Project, "läden / kasse", 7, 5),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "logaffe: läden / kasse has gone quiet",
            Assert.Single(_ntfy.Taken).Published.GetProperty("title").GetString());
    }

    [Fact]
    public async Task An_installation_that_knows_no_address_sends_no_link()
    {
        await Notifier(publicUrl: null).SendAsync(Flood, TestContext.Current.CancellationToken);

        Assert.False(
            Assert.Single(_ntfy.Taken).Published.TryGetProperty("click", out _));
    }

    [Fact]
    public async Task A_sealed_token_is_opened_for_the_send()
    {
        await Notifier(accessToken: "tk_secret")
            .SendAsync(Flood, TestContext.Current.CancellationToken);

        Assert.Equal("Bearer tk_secret", Assert.Single(_ntfy.Taken).Authorization);
    }

    [Fact]
    public async Task A_public_topic_is_published_to_without_one()
    {
        await Notifier().SendAsync(Flood, TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(_ntfy.Taken).Authorization);
    }

    /// <summary>
    /// An operator switches a condition on before they configure a notifier, or
    /// clears the notifier afterwards. It is a real state, and the line is what
    /// makes it legible instead of silent.
    /// </summary>
    [Fact]
    public async Task An_installation_with_no_notifier_says_so_in_its_own_log()
    {
        await Notifier(notifier: null).SendAsync(Flood, TestContext.Current.CancellationToken);

        Assert.Empty(_ntfy.Taken);
        Assert.Contains("no notifier configured", Assert.Single(_log.Lines));
    }

    /// <summary>
    /// The trade ADR 0050 makes deliberately: a queue of undelivered alerts
    /// arriving together an hour later is a burst of notifications about things
    /// that are no longer true, so the alert that was missed is the cost.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task A_notifier_that_says_no_costs_one_line_and_no_retry(HttpStatusCode status)
    {
        _ntfy.Status = status;

        await Notifier().SendAsync(Flood, TestContext.Current.CancellationToken);

        Assert.Single(_ntfy.Taken);
        Assert.Contains("There is no retry", Assert.Single(_log.Lines));
    }

    [Fact]
    public async Task A_notifier_that_is_not_there_costs_the_same()
    {
        _ntfy.Fails = new HttpRequestException("There is no such host.");

        await Notifier().SendAsync(Flood, TestContext.Current.CancellationToken);

        Assert.Single(_ntfy.Taken);
        Assert.Single(_log.Lines);
    }

    /// <summary>
    /// The pass that decides has other projects to evaluate, and a throw would
    /// take them with it.
    /// </summary>
    [Fact]
    public async Task A_notifier_that_takes_too_long_is_not_thrown_at_the_pass()
    {
        _ntfy.Fails = new TaskCanceledException("It took too long.");

        await Notifier().SendAsync(Flood, TestContext.Current.CancellationToken);

        Assert.Single(_log.Lines);
    }

    [Fact]
    public async Task A_test_notification_is_the_shape_a_real_one_is()
    {
        var proof = await Notifier().SendTestAsync(TestContext.Current.CancellationToken);

        var published = Assert.Single(_ntfy.Taken).Published;

        Assert.Equal(NotifierProof.Sent, proof);
        Assert.Equal("logaffe: a test notification", published.GetProperty("title").GetString());
        Assert.Equal("https://logs.example.com/", published.GetProperty("click").GetString());
        Assert.Empty(_log.Lines);
    }

    /// <summary>
    /// The two refusals are told apart because the operator's next move is
    /// different: a token that is wrong is the notifier's own settings, and
    /// anything else is the address or the network.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.OK, NotifierProof.Sent)]
    [InlineData(HttpStatusCode.Unauthorized, NotifierProof.Refused)]
    [InlineData(HttpStatusCode.Forbidden, NotifierProof.Refused)]
    [InlineData(HttpStatusCode.NotFound, NotifierProof.Unreachable)]
    [InlineData(HttpStatusCode.InternalServerError, NotifierProof.Unreachable)]
    public async Task The_test_send_says_which_way_it_went(
        HttpStatusCode status, NotifierProof expected)
    {
        _ntfy.Status = status;

        Assert.Equal(
            expected, await Notifier().SendTestAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_notifier_that_cannot_be_reached_says_so_to_the_operator()
    {
        _ntfy.Fails = new HttpRequestException("There is no such host.");

        Assert.Equal(
            NotifierProof.Unreachable,
            await Notifier().SendTestAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_installation_with_no_notifier_has_nothing_to_prove()
    {
        Assert.Equal(
            NotifierProof.NoNotifier,
            await Notifier(notifier: null).SendTestAsync(TestContext.Current.CancellationToken));

        Assert.Empty(_ntfy.Taken);
    }

    private NtfyNotifier Notifier(
        string? publicUrl = "https://logs.example.com",
        string? accessToken = null,
        string? notifier = "https://ntfy.example.com")
    {
        var cipher = new ReversingCipher();

        return new NtfyNotifier(
            new HttpClient(_ntfy),
            new TheInstallationsNotifier(notifier is null
                ? null
                : Domain.Alerts.Notifier.Create(
                    notifier,
                    "logaffe",
                    accessToken is null ? null : cipher.Encrypt(accessToken))),
            cipher,
            AlertLinks.From(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        [AlertLinks.PublicUrlKey] = publicUrl,
                    })
                    .Build(),
                NullLogger<AlertLinks>.Instance),
            _log);
    }

    /// <summary>One publication, as the ntfy server would have received it.</summary>
    private sealed record Taken(JsonElement Published, string? Authorization);

    /// <summary>An ntfy that is not there, standing in for the operator's.</summary>
    private sealed class TakingNotifications : HttpMessageHandler
    {
        private readonly List<Taken> _taken = [];

        /// <summary>What to answer with, when it answers at all.</summary>
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        /// <summary>Thrown instead of answering, for the server that is down.</summary>
        public Exception? Fails { get; set; }

        public IReadOnlyList<Taken> Taken => _taken;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _taken.Add(new Taken(
                JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement,
                request.Headers.Authorization?.ToString()));

            return Fails is null
                ? new HttpResponseMessage(Status)
                : throw Fails;
        }
    }

    /// <summary>
    /// The installation's own log, as the lines that reached it. Nothing else on
    /// this port is asked of the notifier.
    /// </summary>
    private sealed class RecordingLog : ILogger<NtfyNotifier>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));
    }

    /// <summary>
    /// An installation that holds one notifier and answers nothing else: what
    /// this adapter asks of the port is where to send, and the rest is another
    /// test's.
    /// </summary>
    private sealed class TheInstallationsNotifier(Notifier? notifier) : IInstallation
    {
        public Task<Notifier?> ReadNotifierAsync(CancellationToken cancellationToken) =>
            Task.FromResult(notifier);

        public Task RecordNotifierAsync(Notifier? notifier, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ClaimGuard?> ReadClaimGuardAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ClaimGuard> OpenClaimAsync(
            DateTimeOffset firstRunAt, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ClaimGuard> ArmClaimAsync(
            DateTimeOffset at, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RecordClaimAsync(ClaimGuard guard, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RetentionWindow> ReadSampleRetentionAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RecordSampleRetentionAsync(
            RetentionWindow window, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<InstallationHost?> ReadHostAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RecordHostAsync(InstallationHost? host, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AlertSwitches> ReadAlertSwitchesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RecordAlertSwitchesAsync(
            AlertSwitches switches, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// A cipher that is not one, and deliberately not the identity either: a
    /// token sent as it was stored is a failing assertion rather than an
    /// indistinguishable pass.
    /// </summary>
    private sealed class ReversingCipher : ISecretCipher
    {
        public byte[] Encrypt(string secret) => Encoding.UTF8.GetBytes(Reversed(secret));

        public string Decrypt(byte[] sealedSecret) =>
            Reversed(Encoding.UTF8.GetString(sealedSecret));

        private static string Reversed(string value) => new([.. value.Reverse()]);
    }
}
