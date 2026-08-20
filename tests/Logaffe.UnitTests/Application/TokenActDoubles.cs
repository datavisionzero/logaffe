using System.Text;
using Logaffe.Application.Ports;
using Logaffe.Domain.Tokens;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// The three token tables, in memory. It behaves as the real store does in the
/// two ways the acts turn on — a row is found by the identity that named it,
/// and a removed row is not there any more — and in no other way.
/// </summary>
internal sealed class InMemoryTokens : ITokens
{
    private readonly List<IngestToken> _ingestTokens = [];
    private readonly List<AgentToken> _agentTokens = [];
    private readonly List<HostToken> _hostTokens = [];

    public IReadOnlyList<IngestToken> Stored => _ingestTokens;

    public IReadOnlyList<AgentToken> StoredAgentTokens => _agentTokens;

    public IReadOnlyList<HostToken> StoredHostTokens => _hostTokens;

    /// <summary>How many statements the store was asked to write.</summary>
    public int Writes { get; private set; }

    /// <summary>
    /// How many statements the store was asked to list tokens with. It is what
    /// says a list of every project's tokens is one read rather than one per
    /// project, which is the whole of what a settings tree costs here.
    /// </summary>
    public int Reads { get; private set; }

    public Task<IngestToken?> FindIngestTokenAsync(
        TokenIdentifier identifier, CancellationToken cancellationToken) =>
        Task.FromResult(_ingestTokens.SingleOrDefault(t => t.Identifier == identifier));

    public Task<AgentToken?> FindAgentTokenAsync(
        TokenIdentifier identifier, CancellationToken cancellationToken) =>
        Task.FromResult(_agentTokens.SingleOrDefault(t => t.Identifier == identifier));

    public Task<HostToken?> FindHostTokenAsync(
        TokenIdentifier identifier, CancellationToken cancellationToken) =>
        Task.FromResult(_hostTokens.SingleOrDefault(t => t.Identifier == identifier));

    public Task<IngestToken?> FindIngestTokenAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_ingestTokens.SingleOrDefault(t => t.Id == id));

    public Task<AgentToken?> FindAgentTokenAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_agentTokens.SingleOrDefault(t => t.Id == id));

    public Task<HostToken?> FindHostTokenAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_hostTokens.SingleOrDefault(t => t.Id == id));

    public Task<IReadOnlyList<HeldToken>> ListIngestTokensAsync(
        Guid projectId, CancellationToken cancellationToken) =>
        Read<IReadOnlyList<HeldToken>>(
            [.. _ingestTokens
                .Where(t => t.ProjectId == projectId)
                .OrderBy(t => t.IssuedAt)
                .Select(Held)]);

    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<HeldToken>>> ListIngestTokensAsync(
        CancellationToken cancellationToken) =>
        Read<IReadOnlyDictionary<Guid, IReadOnlyList<HeldToken>>>(
            _ingestTokens
                .OrderBy(t => t.IssuedAt)
                .GroupBy(t => t.ProjectId)
                .ToDictionary(
                    project => project.Key,
                    IReadOnlyList<HeldToken> (project) => [.. project.Select(Held)]));

    public Task<IReadOnlyList<HeldToken>> ListHostTokensAsync(
        Guid hostId, CancellationToken cancellationToken) =>
        Read<IReadOnlyList<HeldToken>>(
            [.. _hostTokens
                .Where(t => t.HostId == hostId)
                .OrderBy(t => t.IssuedAt)
                .Select(Held)]);

    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<HeldToken>>> ListHostTokensAsync(
        CancellationToken cancellationToken) =>
        Read<IReadOnlyDictionary<Guid, IReadOnlyList<HeldToken>>>(
            _hostTokens
                .OrderBy(t => t.IssuedAt)
                .GroupBy(t => t.HostId)
                .ToDictionary(
                    host => host.Key,
                    IReadOnlyList<HeldToken> (host) => [.. host.Select(Held)]));

    public Task<IReadOnlyList<AgentToken>> ListAgentTokensAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AgentToken>>([.. _agentTokens.OrderBy(t => t.IssuedAt)]);

    public Task AddAsync(IngestToken token, CancellationToken cancellationToken) =>
        Write(() => _ingestTokens.Add(token));

    public Task AddAsync(AgentToken token, CancellationToken cancellationToken) =>
        Write(() => _agentTokens.Add(token));

    public Task AddAsync(HostToken token, CancellationToken cancellationToken) =>
        Write(() => _hostTokens.Add(token));

    public Task RemoveAsync(IngestToken token, CancellationToken cancellationToken) =>
        Write(() => _ingestTokens.Remove(token));

    public Task RemoveAsync(AgentToken token, CancellationToken cancellationToken) =>
        Write(() => _agentTokens.Remove(token));

    public Task RemoveAsync(HostToken token, CancellationToken cancellationToken) =>
        Write(() => _hostTokens.Remove(token));

    public Task RecordRenameAsync(AgentToken token, CancellationToken cancellationToken) =>
        Write(() => { });

    public Task RecordUseAsync(IngestToken token, CancellationToken cancellationToken) =>
        Write(() => { });

    public Task RecordUseAsync(AgentToken token, CancellationToken cancellationToken) =>
        Write(() => { });

    public Task RecordUseAsync(HostToken token, CancellationToken cancellationToken) =>
        Write(() => { });

    private Task<T> Read<T>(T answer)
    {
        Reads++;
        return Task.FromResult(answer);
    }

    private static HeldToken Held(IngestToken token) =>
        new(token.Id, token.Identifier, token.IssuedAt, token.LastUsedAt);

    private static HeldToken Held(HostToken token) =>
        new(token.Id, token.Identifier, token.IssuedAt, token.LastUsedAt);

    private Task Write(Action write)
    {
        write();
        Writes++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// A cipher that is not one, and is deliberately not the identity either: it
/// reverses what it is given, so that a row holding the secret in the clear is a
/// failing assertion rather than an indistinguishable pass.
/// </summary>
internal sealed class ReversingCipher : ISecretCipher
{
    public byte[] Encrypt(string secret) => Encoding.UTF8.GetBytes(Reversed(secret));

    public string Decrypt(byte[] sealedSecret) =>
        Reversed(Encoding.UTF8.GetString(sealedSecret));

    private static string Reversed(string value) => new([.. value.Reverse()]);
}

/// <summary>A clock the test moves.</summary>
internal sealed class StoppedClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
