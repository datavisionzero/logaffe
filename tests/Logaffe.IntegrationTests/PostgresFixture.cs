using Npgsql;
using Testcontainers.PostgreSql;

namespace Logaffe.IntegrationTests;

/// <summary>
/// One Postgres for the whole run, and a fresh database inside it per test.
/// </summary>
/// <remarks>
/// The image is pinned to the major version an installation actually runs, so
/// that what these tests prove about migrations and indexes is what production
/// will do rather than what some other server would.
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18").Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>
    /// A database of its own, so that one test's schema is never another's
    /// starting point.
    /// </summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"logaffe_{Guid.NewGuid():n}"[..24];

        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"""CREATE DATABASE "{name}" """, connection);
        await command.ExecuteNonQueryAsync();

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = name,

            // Npgsql pools per connection string and a database of its own makes
            // a connection string of its own, so a run leaves one pool per test
            // behind. That is why pooling was off here — with the default idle
            // lifetime of five minutes, the pools outlive the tests that made
            // them and the run walks into `too many clients already`.
            //
            // What it cost was invisible until #46: with no pool, every request
            // an installation serves opens a physical connection, TLS setup
            // included, and on a loaded runner that occasionally took longer
            // than the caller was willing to wait. The request the caller gave
            // up on was the one authenticating an agent token, which stands in
            // front of every MCP request — so the failure moved between tests
            // and looked like anything but a connection.
            //
            // So: a pool, bounded rather than absent. A short idle lifetime and
            // a pruner that runs against it take the connections back within
            // seconds of a test finishing, which is the problem the pool was
            // turned off for, and a small ceiling means one test's pool cannot
            // be what exhausts the server.
            ConnectionIdleLifetime = 5,
            ConnectionPruningInterval = 1,
            MaxPoolSize = 10,
        }.ConnectionString;
    }
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
