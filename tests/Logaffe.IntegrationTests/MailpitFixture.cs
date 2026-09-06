using System.Net.Http.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Logaffe.Application.Ports;

namespace Logaffe.IntegrationTests;

/// <summary>
/// One Mailpit for the whole run: what the mail tests send through, and what
/// they read the delivered message back out of (ADR 0053).
/// </summary>
/// <remarks>
/// A real SMTP conversation rather than a substituted port, because what is
/// worth proving here is the part no double can vouch for — that MailKit and a
/// server agree on the handshake, and that what arrives is the two bodies that
/// were written. Mailpit belongs in development and in tests and nowhere near
/// production, which is why it is here and in
/// <c>deploy/docker-compose.dev.yml</c> and in no third place.
/// </remarks>
public sealed class MailpitFixture : IAsyncLifetime
{
    private const int Smtp = 1025;

    private const int Api = 8025;

    private readonly IContainer _container = new ContainerBuilder("axllent/mailpit:latest")
        .WithPortBinding(Smtp, assignRandomHostPort: true)
        .WithPortBinding(Api, assignRandomHostPort: true)
        .WithEnvironment("MP_SMTP_AUTH_ACCEPT_ANY", "1")
        .WithEnvironment("MP_SMTP_AUTH_ALLOW_INSECURE", "1")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(
            request => request.ForPort(Api).ForPath("/readyz")))
        .Build();

    private HttpClient _api = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        _api = new HttpClient
        {
            BaseAddress = new Uri($"http://{_container.Hostname}:{_container.GetMappedPublicPort(Api)}"),
        };
    }

    public async ValueTask DisposeAsync()
    {
        _api.Dispose();
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Settings pointed at this Mailpit, in the shape an installation reads them
    /// in.
    /// </summary>
    public SmtpSettings Settings(string publicUrl = "http://logs.example.com") =>
        SmtpSettings.Read(
            _container.Hostname,
            _container.GetMappedPublicPort(Smtp).ToString(),
            username: null,
            password: null,
            security: "none",
            from: "logaffe@example.com",
            fromName: "logaffe",
            publicUrl,
            development: true);

    /// <summary>Throws away what earlier tests sent, so that each starts empty.</summary>
    public async Task ForgetAsync(CancellationToken cancellationToken) =>
        (await _api.DeleteAsync("/api/v1/messages", cancellationToken))
            .EnsureSuccessStatusCode();

    /// <summary>
    /// The one message that arrived, in both of its bodies. It waits, because a
    /// server that has accepted a message has not necessarily finished filing
    /// it.
    /// </summary>
    public async Task<Delivered> OnlyMessageAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var listed = await _api.GetFromJsonAsync<Listing>(
                "/api/v1/messages?limit=5", cancellationToken);

            if (listed is { Messages.Count: > 0 })
            {
                Assert.Single(listed.Messages);

                return (await _api.GetFromJsonAsync<Delivered>(
                    $"/api/v1/message/{listed.Messages[0].Id}", cancellationToken))!;
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new InvalidOperationException("Nothing was delivered within five seconds.");
    }

    /// <summary>How many messages are waiting, which some tests assert is none.</summary>
    public async Task<int> CountAsync(CancellationToken cancellationToken) =>
        (await _api.GetFromJsonAsync<Listing>("/api/v1/messages?limit=50", cancellationToken))
            ?.Messages.Count ?? 0;

    private sealed record Listing(IReadOnlyList<Summary> Messages);

    private sealed record Summary(string Id);

    /// <summary>What Mailpit says about a message it took.</summary>
    public sealed record Delivered(
        string Subject, string Text, string Html, IReadOnlyList<Recipient> To);

    /// <summary>Who it went to.</summary>
    public sealed record Recipient(string Address);
}

/// <inheritdoc cref="MailpitFixture"/>
[CollectionDefinition(nameof(MailpitCollection))]
public sealed class MailpitCollection : ICollectionFixture<MailpitFixture>;
