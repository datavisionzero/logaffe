using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Logaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "change",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_kind = table.Column<int>(type: "integer", nullable: false),
                    actor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    subject = table.Column<int>(type: "integer", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    act = table.Column<int>(type: "integer", nullable: false),
                    field = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    moved_from = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    moved_to = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_change", x => x.id);
                    table.ForeignKey(
                        name: "fk_change_actor",
                        column: x => x.actor_id,
                        principalTable: "identity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_change_actor_id",
                table: "change",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_change_subject",
                table: "change",
                columns: new[] { "subject", "subject_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "change");
        }
    }
}
