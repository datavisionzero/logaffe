using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HostsAndSamples : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "host_id",
                table: "project",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "host",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_host", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "installation_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sample_retention_days = table.Column<int>(type: "integer", nullable: false),
                    only_settings = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_installation_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "filesystem_reading",
                columns: table => new
                {
                    host_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    mount_path = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    used = table.Column<long>(type: "bigint", nullable: false),
                    total = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_filesystem_reading", x => new { x.host_id, x.receipt_time, x.mount_path });
                    table.ForeignKey(
                        name: "fk_filesystem_reading_host",
                        column: x => x.host_id,
                        principalTable: "host",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "host_sample",
                columns: table => new
                {
                    host_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cpu = table.Column<float>(type: "real", nullable: false),
                    memory_used = table.Column<long>(type: "bigint", nullable: false),
                    memory_total = table.Column<long>(type: "bigint", nullable: false),
                    load_1 = table.Column<float>(type: "real", nullable: false),
                    load_5 = table.Column<float>(type: "real", nullable: false),
                    load_15 = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_host_sample", x => new { x.host_id, x.receipt_time });
                    table.ForeignKey(
                        name: "fk_host_sample_host",
                        column: x => x.host_id,
                        principalTable: "host",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "host_token",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_id = table.Column<Guid>(type: "uuid", nullable: false),
                    identifier = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    secret = table.Column<byte[]>(type: "bytea", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_host_token", x => x.id);
                    table.ForeignKey(
                        name: "fk_host_token_host",
                        column: x => x.host_id,
                        principalTable: "host",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_project_host",
                table: "project",
                column: "host_id");

            migrationBuilder.CreateIndex(
                name: "ix_host_name",
                table: "host",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_host_token_host",
                table: "host_token",
                column: "host_id");

            migrationBuilder.CreateIndex(
                name: "ix_host_token_identifier",
                table: "host_token",
                column: "identifier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_installation_settings_only_one",
                table: "installation_settings",
                column: "only_settings",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_project_host",
                table: "project",
                column: "host_id",
                principalTable: "host",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_project_host",
                table: "project");

            migrationBuilder.DropTable(
                name: "filesystem_reading");

            migrationBuilder.DropTable(
                name: "host_sample");

            migrationBuilder.DropTable(
                name: "host_token");

            migrationBuilder.DropTable(
                name: "installation_settings");

            migrationBuilder.DropTable(
                name: "host");

            migrationBuilder.DropIndex(
                name: "ix_project_host",
                table: "project");

            migrationBuilder.DropColumn(
                name: "host_id",
                table: "project");
        }
    }
}
