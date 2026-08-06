namespace Bench;

using Npgsql;

/// PROTOTYPE. Connection plumbing for the scratch database.
static class Db
{
    public const string ConnectionString =
        "Host=localhost;Port=55432;Username=postgres;Password=prototype;" +
        "Database=logaffe_prototype;Include Error Detail=true;Timeout=15;Command Timeout=0";

    public static async Task<NpgsqlConnection> OpenAsync()
    {
        var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    public static async Task ExecAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<T?> ScalarAsync<T>(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        var value = await cmd.ExecuteScalarAsync();
        // Postgres is happy to hand back int4 where the caller wants int8.
        return value is null or DBNull ? default : (T)Convert.ChangeType(value, typeof(T));
    }
}
