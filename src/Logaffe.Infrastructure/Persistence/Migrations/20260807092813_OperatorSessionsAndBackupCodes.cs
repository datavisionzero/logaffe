using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OperatorSessionsAndBackupCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "operator",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    password_hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    second_factor_secret = table.Column<byte[]>(type: "bytea", nullable: false),
                    second_factor_enrolled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    only_operator = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operator", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "backup_code",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_backup_code", x => x.id);
                    table.ForeignKey(
                        name: "fk_backup_code_operator",
                        column: x => x.operator_id,
                        principalTable: "operator",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "session",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    secret_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_from = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session", x => x.id);
                    table.ForeignKey(
                        name: "fk_session_operator",
                        column: x => x.operator_id,
                        principalTable: "operator",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_backup_code_hash",
                table: "backup_code",
                column: "hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_backup_code_operator",
                table: "backup_code",
                column: "operator_id");

            migrationBuilder.CreateIndex(
                name: "ix_operator_only_one",
                table: "operator",
                column: "only_operator",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_session_operator",
                table: "session",
                column: "operator_id");

            migrationBuilder.CreateIndex(
                name: "ix_session_secret",
                table: "session",
                column: "secret_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "backup_code");

            migrationBuilder.DropTable(
                name: "session");

            migrationBuilder.DropTable(
                name: "operator");
        }
    }
}
