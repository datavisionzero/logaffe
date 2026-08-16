using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Groups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_project_name",
                table: "project");

            migrationBuilder.AddColumn<Guid>(
                name: "group_id",
                table: "project",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "project_group",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_group", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_project_group_id_name",
                table: "project",
                columns: new[] { "group_id", "name" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_project_group_name",
                table: "project_group",
                column: "name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_project_project_group",
                table: "project",
                column: "group_id",
                principalTable: "project_group",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_project_project_group",
                table: "project");

            migrationBuilder.DropTable(
                name: "project_group");

            migrationBuilder.DropIndex(
                name: "ix_project_group_id_name",
                table: "project");

            migrationBuilder.DropColumn(
                name: "group_id",
                table: "project");

            migrationBuilder.CreateIndex(
                name: "ix_project_name",
                table: "project",
                column: "name",
                unique: true);
        }
    }
}
