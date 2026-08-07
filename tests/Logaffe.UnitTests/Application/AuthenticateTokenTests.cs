using System.Text;
using Logaffe.Application.Operations;
using Logaffe.Application.Ports;
using Logaffe.Domain.Tokens;

namespace Logaffe.UnitTests.Application;

public sealed class AuthenticateTokenTests
{
    private static readonly DateTimeOffset Issued = new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Project = Guid.CreateVersion7();

    [Fact]
    public async Task A_token_admits_a_delivery_to_the_project_that_holds_it()
    {
        var issued = TokenText.Mint(TokenKind.Ingest);

        Assert.Equal(Project, await Authenticating(issued).AdmittedProjectAsync(
            Bearer(issued), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_agent_token_admits_a_read()
    {
        var issued = TokenText.Mint(TokenKind.Agent);

        Assert.True(await Authenticating(issued).AdmitsReadAsync(
            Bearer(issued), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_secret_that_is_not_the_stored_one_admits_nothing()
    {
        // The identifier of a real row, and a secret that is not its own: the
        // case a stolen identifier gets, and it is the ordinary 401.
        var issued = TokenText.Mint(TokenKind.Ingest);
        var forged = TokenText.From(TokenKind.Ingest, issued.Identifier, OtherSecretThan(issued));

        Assert.Null(await Authenticating(issued).AdmittedProjectAsync(
            Bearer(forged), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_identifier_that_names_no_row_costs_what_a_secret_that_mismatches_costs()
    {
        // ADR 0031: the 401 says nothing about which of the two it was, and a
        // lookup that returned early would say it in the timing.
        var issued = TokenText.Mint(TokenKind.Ingest);

        var mismatch = new StubCipher();
        var forged = TokenText.From(TokenKind.Ingest, issued.Identifier, OtherSecretThan(issued));
        Assert.Null(await Authenticating(issued, cipher: mismatch).AdmittedProjectAsync(
            Bearer(forged), TestContext.Current.CancellationToken));

        var miss = new StubCipher();
        var stranger = TokenText.Mint(TokenKind.Ingest);
        Assert.Null(await Authenticating(issued, cipher: miss).AdmittedProjectAsync(
            Bearer(stranger), TestContext.Current.CancellationToken));

        Assert.Equal(1, mismatch.Decryptions);
        Assert.Equal(mismatch.Decryptions, miss.Decryptions);
        Assert.Equal(1, miss.ComparableSecretsRead);
    }

    [Fact]
    public async Task A_token_of_the_other_kind_is_refused_without_the_database_being_asked()
    {
        // Pasting one where the other belongs is the mistake ADR 0021 makes
        // possible, and the prefix is what fails it at the door.
        var agent = TokenText.Mint(TokenKind.Agent);
        var ingest = TokenText.Mint(TokenKind.Ingest);

        var atIngest = new StubTokens();
        Assert.Null(await Authenticating(ingest, atIngest).AdmittedProjectAsync(
            Bearer(agent), TestContext.Current.CancellationToken));

        var atMcp = new StubTokens();
        Assert.False(await Authenticating(agent, atMcp).AdmitsReadAsync(
            Bearer(ingest), TestContext.Current.CancellationToken));

        Assert.Equal(0, atIngest.Lookups);
        Assert.Equal(0, atMcp.Lookups);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer")]
    [InlineData("Bearer ")]
    [InlineData("Basic logaffe_ingest_abcdefghijkm_secret")]
    [InlineData("Bearer not-a-token")]
    [InlineData("Bearerlogaffe_ingest_abcdefghijkm_secret")]
    public async Task A_header_that_is_not_a_presented_token_never_reaches_the_database(
        string? authorization)
    {
        var tokens = new StubTokens();

        Assert.Null(await Authenticating(TokenText.Mint(TokenKind.Ingest), tokens)
            .AdmittedProjectAsync(authorization, TestContext.Current.CancellationToken));

        Assert.Equal(0, tokens.Lookups);
    }

    [Fact]
    public async Task The_scheme_is_read_case_insensitively()
    {
        var issued = TokenText.Mint(TokenKind.Ingest);

        Assert.Equal(Project, await Authenticating(issued).AdmittedProjectAsync(
            $"bearer {issued.Text}", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_first_use_of_a_token_is_written()
    {
        // Null is what tells a token that was issued and never deployed apart
        // from one that has gone quiet, so it is never the case that is skipped.
        var issued = TokenText.Mint(TokenKind.Ingest);
        var tokens = new StubTokens();
        var clock = new FixedClock(Issued.AddDays(1));

        _ = await Authenticating(issued, tokens, clock: clock).AdmittedProjectAsync(
            Bearer(issued), TestContext.Current.CancellationToken);

        Assert.Equal(1, tokens.Writes);
        Assert.Equal(clock.GetUtcNow(), tokens.IngestToken!.LastUsedAt);
    }

    [Fact]
    public async Task A_use_within_the_interval_is_not_written_again()
    {
        // ADR 0033: on the hottest path in the product, an UPDATE per delivery
        // buys a precision nothing asks for.
        var issued = TokenText.Mint(TokenKind.Ingest);
        var tokens = new StubTokens();
        var clock = new FixedClock(Issued.AddDays(1));
        var authenticate = Authenticating(issued, tokens, clock: clock);

        _ = await authenticate.AdmittedProjectAsync(
            Bearer(issued), TestContext.Current.CancellationToken);
        var first = tokens.IngestToken!.LastUsedAt;

        clock.Now += AuthenticateToken.UseWriteInterval - TimeSpan.FromSeconds(1);
        Assert.Equal(Project, await authenticate.AdmittedProjectAsync(
            Bearer(issued), TestContext.Current.CancellationToken));

        Assert.Equal(1, tokens.Writes);
        Assert.Equal(first, tokens.IngestToken.LastUsedAt);
    }

    [Fact]
    public async Task A_use_after_the_interval_is_written()
    {
        var issued = TokenText.Mint(TokenKind.Agent);
        var tokens = new StubTokens();
        var clock = new FixedClock(Issued.AddDays(1));
        var authenticate = Authenticating(issued, tokens, clock: clock);

        _ = await authenticate.AdmitsReadAsync(
            Bearer(issued), TestContext.Current.CancellationToken);

        clock.Now += AuthenticateToken.UseWriteInterval;
        Assert.True(await authenticate.AdmitsReadAsync(
            Bearer(issued), TestContext.Current.CancellationToken));

        Assert.Equal(2, tokens.Writes);
        Assert.Equal(clock.GetUtcNow(), tokens.AgentToken!.LastUsedAt);
    }

    [Fact]
    public async Task A_token_that_admits_nothing_records_no_use()
    {
        // The timestamp stays a statement about the credential rather than
        // about who has been guessing at it.
        var issued = TokenText.Mint(TokenKind.Ingest);
        var forged = TokenText.From(TokenKind.Ingest, issued.Identifier, OtherSecretThan(issued));
        var tokens = new StubTokens();

        Assert.Null(await Authenticating(issued, tokens).AdmittedProjectAsync(
            Bearer(forged), TestContext.Current.CancellationToken));

        Assert.Equal(0, tokens.Writes);
        Assert.Null(tokens.IngestToken!.LastUsedAt);
    }

    private static string Bearer(TokenText token) => $"Bearer {token.Text}";

    private static string OtherSecretThan(TokenText token)
    {
        string secret;
        do
        {
            secret = TokenAlphabet.Random(TokenText.SecretLength);
        }
        while (secret == token.Secret);

        return secret;
    }

    /// <summary>
    /// An installation holding exactly <paramref name="issued"/>, with the
    /// project of an ingest token being <see cref="Project"/>.
    /// </summary>
    private static AuthenticateToken Authenticating(
        TokenText issued,
        StubTokens? tokens = null,
        StubCipher? cipher = null,
        FixedClock? clock = null)
    {
        tokens ??= new StubTokens();
        cipher ??= new StubCipher();

        if (issued.Kind == TokenKind.Ingest)
        {
            tokens.IngestToken = IngestToken.Issue(
                Project, issued.Identifier, cipher.Encrypt(issued.Secret), Issued);
        }
        else
        {
            tokens.AgentToken = AgentToken.Issue(
                "agent", issued.Identifier, cipher.Encrypt(issued.Secret), Issued);
        }

        // What the stub cipher was asked before the operation ran is setup, not
        // a cost the authentication paid.
        cipher.Forget();

        return new AuthenticateToken(
            tokens, cipher, new DummySecret(cipher), clock ?? new FixedClock(Issued.AddDays(1)));
    }

    private sealed class StubTokens : ITokens
    {
        public IngestToken? IngestToken { get; set; }

        public AgentToken? AgentToken { get; set; }

        public int Lookups { get; private set; }

        public int Writes { get; private set; }

        public Task<IngestToken?> FindIngestTokenAsync(
            TokenIdentifier identifier, CancellationToken cancellationToken)
        {
            Lookups++;
            return Task.FromResult(
                IngestToken?.Identifier == identifier ? IngestToken : null);
        }

        public Task<AgentToken?> FindAgentTokenAsync(
            TokenIdentifier identifier, CancellationToken cancellationToken)
        {
            Lookups++;
            return Task.FromResult(
                AgentToken?.Identifier == identifier ? AgentToken : null);
        }

        public Task RecordUseAsync(IngestToken token, CancellationToken cancellationToken)
        {
            Writes++;
            return Task.CompletedTask;
        }

        public Task RecordUseAsync(AgentToken token, CancellationToken cancellationToken)
        {
            Writes++;
            return Task.CompletedTask;
        }

        // The rest of the port is the operator's acts, which nothing on this
        // path reaches. Refusing loudly is what keeps that true: an
        // authentication that started listing or writing tokens would fail here
        // rather than quietly pass.
        public Task<IngestToken?> FindIngestTokenAsync(
            Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AgentToken?> FindAgentTokenAsync(
            Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<IngestToken>> ListIngestTokensAsync(
            Guid projectId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AgentToken>> ListAgentTokensAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task AddAsync(IngestToken token, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task AddAsync(AgentToken token, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RemoveAsync(IngestToken token, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RemoveAsync(AgentToken token, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RecordRenameAsync(AgentToken token, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// A cipher in name only — it counts what it was asked, which is what the
    /// equal-cost rule of ADR 0031 is stated in.
    /// </summary>
    private sealed class StubCipher : ISecretCipher
    {
        public int Decryptions { get; private set; }

        /// <summary>
        /// How many of those decryptions produced something a presented secret
        /// could be compared against, which is every one of them: the dummy
        /// opens exactly as a real row does.
        /// </summary>
        public int ComparableSecretsRead { get; private set; }

        public void Forget() => Decryptions = ComparableSecretsRead = 0;

        public byte[] Encrypt(string secret) => Encoding.UTF8.GetBytes(secret);

        public string Decrypt(byte[] sealedSecret)
        {
            Decryptions++;
            var secret = Encoding.UTF8.GetString(sealedSecret);

            if (secret.Length == TokenText.SecretLength && TokenAlphabet.Covers(secret))
            {
                ComparableSecretsRead++;
            }

            return secret;
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
