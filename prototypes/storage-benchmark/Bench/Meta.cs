namespace Bench;

using Npgsql;

/// PROTOTYPE. Carries a few facts between invocations so that `query` and
/// `retention` know what `load` put there.
static class Meta
{
    public static async Task SetAsync(NpgsqlConnection conn, string key, string value)
    {
        await Db.ExecAsync(conn,
            "create table if not exists bench_meta (key text primary key, value text not null)");
        await using var cmd = new NpgsqlCommand(
            "insert into bench_meta (key, value) values (@key, @value) " +
            "on conflict (key) do update set value = excluded.value", conn);
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("value", value);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<string?> GetAsync(NpgsqlConnection conn, string key)
    {
        await using var cmd = new NpgsqlCommand("select value from bench_meta where key = @key", conn);
        cmd.Parameters.AddWithValue("key", key);
        var value = await cmd.ExecuteScalarAsync();
        return value as string;
    }

    /// The corpus is deterministic, so the project identities can be recovered
    /// from nothing more than how many there were.
    public static async Task<IReadOnlyList<Guid>> ProjectsAsync(NpgsqlConnection conn)
    {
        var count = int.Parse(await GetAsync(conn, "projects") ?? "20");
        return new Corpus(count).Projects;
    }
}
