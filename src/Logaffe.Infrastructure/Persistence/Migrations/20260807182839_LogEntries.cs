using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LogEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gin", ",,")
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateTable(
                name: "log_entry",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    receipt_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    level = table.Column<short>(type: "smallint", nullable: false),
                    logger_name = table.Column<string>(type: "text", nullable: true),
                    instance = table.Column<string>(type: "text", nullable: true),
                    trace_id = table.Column<byte[]>(type: "bytea", nullable: true),
                    span_id = table.Column<byte[]>(type: "bytea", nullable: true),
                    message_template = table.Column<string>(type: "text", nullable: false),
                    rendered_message = table.Column<string>(type: "text", nullable: false),
                    exception = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "jsonb", nullable: true),
                    message_truncated = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    exception_truncated = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_log_entry", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_log_entry_instance",
                table: "log_entry",
                columns: new[] { "project_id", "instance", "event_time" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_log_entry_logger_name",
                table: "log_entry",
                columns: new[] { "project_id", "logger_name", "event_time" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_log_entry_paging",
                table: "log_entry",
                columns: new[] { "project_id", "event_time", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_log_entry_receipt",
                table: "log_entry",
                columns: new[] { "project_id", "receipt_time", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_log_entry_search",
                table: "log_entry",
                columns: new[] { "project_id", "rendered_message" })
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "", "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_log_entry_trace",
                table: "log_entry",
                columns: new[] { "project_id", "trace_id", "event_time" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_log_entry_warning_and_above",
                table: "log_entry",
                columns: new[] { "project_id", "event_time", "id" },
                descending: new[] { false, true, true },
                filter: "level >= 3");

            // Written out here because there is no model API for storage
            // parameters, and it is required rather than suggested: ADR 0023
            // sweeps expired rows instead of dropping partitions, and the
            // default trigger waits until a fifth of a table is dead, which is
            // the wrong shape for one where a predictable fraction expires every
            // day. The numbers are docs/storage.md's.
            migrationBuilder.Sql("""
                ALTER TABLE log_entry SET (
                    autovacuum_vacuum_scale_factor        = 0.01,
                    autovacuum_vacuum_threshold           = 20000,
                    autovacuum_vacuum_cost_limit          = 2000,
                    autovacuum_analyze_scale_factor       = 0.02,
                    autovacuum_vacuum_insert_scale_factor = 0.02
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "log_entry");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:btree_gin", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
