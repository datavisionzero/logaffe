namespace Bench;

using Npgsql;

/// PROTOTYPE. The candidate log entry table, and the index sets whose cost we
/// want to attribute separately.
static class Schema
{
    /// The columns follow CONTEXT.md: a level, two timestamps, the template, the
    /// rendered message, the properties, an optional exception, and the promoted
    /// properties lifted out into first-class fields.
    const string TableDdl = """
        create table log_entry (
            id               bigint      not null,
            project_id       uuid        not null,
            event_time       timestamptz not null,
            receipt_time     timestamptz not null,
            level            smallint    not null,
            logger_name      text,
            instance         text,
            trace_id         text,
            span_id          text,
            message_template text        not null,
            rendered_message text        not null,
            exception        text,
            properties       jsonb,
            truncation       smallint    not null default 0
        );

        alter table log_entry
            alter column message_template set compression lz4,
            alter column rendered_message set compression lz4,
            alter column exception         set compression lz4,
            alter column properties        set compression lz4;

        -- ADR 0023 says this table cannot be left on autovacuum defaults, because
        -- a predictable fraction of it expires every day.
        alter table log_entry set (
            autovacuum_vacuum_scale_factor        = 0.01,
            autovacuum_vacuum_threshold           = 20000,
            autovacuum_vacuum_cost_limit          = 2000,
            autovacuum_analyze_scale_factor       = 0.02,
            autovacuum_vacuum_insert_scale_factor = 0.02
        );
        """;

    public record Index(string Name, string Ddl);

    static readonly Index Pk = new(
        "pk_log_entry",
        "alter table log_entry add constraint pk_log_entry primary key (id)");

    /// The cursor of docs/querying.md: newest first by event time, identity
    /// breaking ties.
    static readonly Index Paging = new(
        "ix_log_entry_paging",
        "create index ix_log_entry_paging on log_entry (project_id, event_time desc, id desc)");

    /// The live tail (ADR 0009) and the retention sweep (ADR 0023) both run on
    /// receipt time.
    static readonly Index Receipt = new(
        "ix_log_entry_receipt",
        "create index ix_log_entry_receipt on log_entry (project_id, receipt_time, id)");

    /// ADR 0010. Leading with project_id needs btree_gin, and is what keeps a
    /// search from touching every project's trigrams.
    static readonly Index TrgmComposite = new(
        "ix_log_entry_trgm",
        "create index ix_log_entry_trgm on log_entry using gin (project_id, rendered_message gin_trgm_ops)");

    static readonly Index TrgmPlain = new(
        "ix_log_entry_trgm",
        "create index ix_log_entry_trgm on log_entry using gin (rendered_message gin_trgm_ops)");

    static readonly Index TrgmCompositeNoFastupdate = new(
        "ix_log_entry_trgm",
        "create index ix_log_entry_trgm on log_entry using gin (project_id, rendered_message gin_trgm_ops) with (fastupdate = off)");

    static readonly Index Logger = new(
        "ix_log_entry_logger",
        "create index ix_log_entry_logger on log_entry (project_id, logger_name, event_time desc)");

    static readonly Index Instance = new(
        "ix_log_entry_instance",
        "create index ix_log_entry_instance on log_entry (project_id, instance, event_time desc)");

    /// "Warning and above" is the threshold people actually ask for, and a
    /// partial index over it is a fraction of the size of a full one on level.
    static readonly Index LevelPartial = new(
        "ix_log_entry_warn",
        "create index ix_log_entry_warn on log_entry (project_id, event_time desc, id desc) where level >= 3");

    public static readonly Dictionary<string, Index[]> Sets = new()
    {
        ["none"] = [Pk],
        ["paging"] = [Pk, Paging, Receipt],
        ["full"] = [Pk, Paging, Receipt, TrgmComposite, Logger, Instance, LevelPartial],
        ["full-plain-gin"] = [Pk, Paging, Receipt, TrgmPlain, Logger, Instance, LevelPartial],
        ["full-fastupdate-off"] = [Pk, Paging, Receipt, TrgmCompositeNoFastupdate, Logger, Instance, LevelPartial],
        ["full-no-pk"] = [Paging, Receipt, TrgmComposite, Logger, Instance, LevelPartial],
    };

    public static async Task ResetAsync(NpgsqlConnection conn)
    {
        await Db.ExecAsync(conn, "create extension if not exists pg_trgm");
        await Db.ExecAsync(conn, "create extension if not exists btree_gin");
        await Db.ExecAsync(conn, "drop table if exists log_entry");
        await Db.ExecAsync(conn, TableDdl);
    }

    public static async Task<TimeSpan> CreateIndexesAsync(NpgsqlConnection conn, string setName)
    {
        if (!Sets.TryGetValue(setName, out var set))
            throw new ArgumentException($"unknown index set '{setName}'; known: {string.Join(", ", Sets.Keys)}");

        var started = DateTime.UtcNow;
        foreach (var index in set)
        {
            var indexStarted = DateTime.UtcNow;
            await Db.ExecAsync(conn, index.Ddl);
            Console.WriteLine($"  {index.Name,-24} built in {(DateTime.UtcNow - indexStarted).TotalSeconds,8:F1}s");
        }
        return DateTime.UtcNow - started;
    }
}
