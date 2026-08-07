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
            // a connection string of its own, so a pooled run leaves one idle
            // pool per test behind for five minutes and the run walks into
            // `too many clients already` on the way through. Nothing here opens
            // a connection often enough for a pool to be worth that.
            Pooling = false,
        }.ConnectionString;
    }
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
