using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OneTimeSecrets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "one_time_secret",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<int>(type: "integer", nullable: false),
                    secret_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    pending_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    pending_normalized_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_one_time_secret", x => x.id);
                    table.ForeignKey(
                        name: "fk_one_time_secret_user",
                        column: x => x.user_id,
                        principalTable: "identity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_one_time_secret_hash",
                table: "one_time_secret",
                column: "secret_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_one_time_secret_live",
                table: "one_time_secret",
                columns: new[] { "user_id", "purpose" },
                unique: true,
                filter: "\"used_at\" is null");

            migrationBuilder.CreateIndex(
                name: "ix_one_time_secret_pending",
                table: "one_time_secret",
                column: "pending_normalized_email",
                filter: "\"used_at\" is null and \"pending_normalized_email\" is not null");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "one_time_secret");
        }
    }
}
