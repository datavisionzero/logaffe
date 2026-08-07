using System.Text;
using Logaffe.Application.Ports;
using Logaffe.Domain.Tokens;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// The two token tables, in memory. It behaves as the real store does in the
/// two ways the acts turn on — a row is found by the identity that named it,
/// and a removed row is not there any more — and in no other way.
/// </summary>
internal sealed class InMemoryTokens : ITokens
{
    private readonly List<IngestToken> _ingestTokens = [];
    private readonly List<AgentToken> _agentTokens = [];

    public IReadOnlyList<IngestToken> Stored => _ingestTokens;

    public IReadOnlyList<AgentToken> StoredAgentTokens => _agentTokens;

    /// <summary>How many statements the store was asked to write.</summary>
    public int Writes { get; private set; }

    public Task<IngestToken?> FindIngestTokenAsync(
        TokenIdentifier identifier, CancellationToken cancellationToken) =>
        Task.FromResult(_ingestTokens.SingleOrDefault(t => t.Identifier == identifier));

    public Task<AgentToken?> FindAgentTokenAsync(
        TokenIdentifier identifier, CancellationToken cancellationToken) =>
        Task.FromResult(_agentTokens.SingleOrDefault(t => t.Identifier == identifier));

    public Task<IngestToken?> FindIngestTokenAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_ingestTokens.SingleOrDefault(t => t.Id == id));

    public Task<AgentToken?> FindAgentTokenAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_agentTokens.SingleOrDefault(t => t.Id == id));

    public Task<IReadOnlyList<IngestToken>> ListIngestTokensAsync(
        Guid projectId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<IngestToken>>(
            [.. _ingestTokens.Where(t => t.ProjectId == projectId).OrderBy(t => t.IssuedAt)]);

    public Task<IReadOnlyDictionary<Guid, int>> CountIngestTokensAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, int>>(
            _ingestTokens
                .GroupBy(t => t.ProjectId)
                .ToDictionary(project => project.Key, project => project.Count()));

    public Task<IReadOnlyList<AgentToken>> ListAgentTokensAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AgentToken>>([.. _agentTokens.OrderBy(t => t.IssuedAt)]);

    public Task AddAsync(IngestToken token, CancellationToken cancellationToken) =>
        Write(() => _ingestTokens.Add(token));

    public Task AddAsync(AgentToken token, CancellationToken cancellationToken) =>
        Write(() => _agentTokens.Add(token));

    public Task RemoveAsync(IngestToken token, CancellationToken cancellationToken) =>
        Write(() => _ingestTokens.Remove(token));

    public Task RemoveAsync(AgentToken token, CancellationToken cancellationToken) =>
        Write(() => _agentTokens.Remove(token));

    public Task RecordRenameAsync(AgentToken token, CancellationToken cancellationToken) =>
        Write(() => { });

    public Task RecordUseAsync(IngestToken token, CancellationToken cancellationToken) =>
        Write(() => { });

    public Task RecordUseAsync(AgentToken token, CancellationToken cancellationToken) =>
        Write(() => { });

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
