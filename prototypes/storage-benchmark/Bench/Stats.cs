namespace Bench;

using Npgsql;

/// PROTOTYPE. Where the bytes went. ADR 0010 claims the trigram index is the
/// second-largest thing this product stores; this is what checks it.
static class Stats
{
    public sealed record Relation(string Name, string Kind, long Bytes)
    {
        public string Pretty => Bytes switch
        {
            >= 1L << 30 => $"{Bytes / (double)(1L << 30):F2} GiB",
            >= 1L << 20 => $"{Bytes / (double)(1L << 20):F1} MiB",
            _ => $"{Bytes / 1024.0:F0} KiB",
        };
    }

    public sealed record Snapshot(
        long Entries,
        long HeapBytes,
        long ToastBytes,
        long TotalBytes,
        List<Relation> Indexes,
        long DeadTuples,
        long? GinPendingPages)
    {
        public double BytesPerEntry => Entries == 0 ? 0 : (double)TotalBytes / Entries;
    }

    public static async Task<Snapshot> SnapshotAsync(NpgsqlConnection conn)
    {
        var entries = await Db.ScalarAsync<long>(conn,
            "select coalesce(n_live_tup, 0) from pg_stat_user_tables where relname = 'log_entry'");
        if (entries == 0)
            entries = await Db.ScalarAsync<long>(conn, "select count(*) from log_entry");

        var heap = await Db.ScalarAsync<long>(conn, "select pg_relation_size('log_entry')");
        var total = await Db.ScalarAsync<long>(conn, "select pg_total_relation_size('log_entry')");
        var toast = await Db.ScalarAsync<long>(conn,
            """
            select coalesce(pg_total_relation_size(reltoastrelid), 0)
            from pg_class where relname = 'log_entry'
            """);
        var dead = await Db.ScalarAsync<long>(conn,
            "select coalesce(n_dead_tup, 0) from pg_stat_user_tables where relname = 'log_entry'");

        var indexes = new List<Relation>();
        await using (var cmd = new NpgsqlCommand(
            """
            select indexrelname, pg_relation_size(indexrelid)
            from pg_stat_user_indexes where relname = 'log_entry'
            order by pg_relation_size(indexrelid) desc
            """, conn))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                indexes.Add(new Relation(reader.GetString(0), "index", reader.GetInt64(1)));
        }

        long? pending = null;
        try
        {
            pending = await Db.ScalarAsync<long>(conn,
                "select pending_pages from pgstatginindex('ix_log_entry_trgm')");
        }
        catch (PostgresException)
        {
            // No GIN index in this index set, or pgstattuple unavailable.
        }

        return new Snapshot(entries, heap, toast, total, indexes, dead, pending);
    }

    public static void Print(Snapshot s)
    {
        Console.WriteLine($"  entries           {s.Entries:N0}");
        Console.WriteLine($"  heap              {new Relation("", "", s.HeapBytes).Pretty}");
        Console.WriteLine($"  toast             {new Relation("", "", s.ToastBytes).Pretty}");
        foreach (var index in s.Indexes)
        {
            var share = s.HeapBytes == 0 ? 0 : 100.0 * index.Bytes / s.HeapBytes;
            Console.WriteLine($"  {index.Name,-24} {index.Pretty,12}   ({share,5:F1}% of heap)");
        }
        Console.WriteLine($"  total             {new Relation("", "", s.TotalBytes).Pretty}");
        Console.WriteLine($"  bytes per entry   {s.BytesPerEntry:F0}");
        Console.WriteLine($"  dead tuples       {s.DeadTuples:N0}");
        if (s.GinPendingPages is { } pages) Console.WriteLine($"  gin pending pages {pages:N0}");
    }
}
