using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProjectAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "project_access",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_by = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_access", x => new { x.project_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_project_access_project",
                        column: x => x.project_id,
                        principalTable: "project",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_project_access_user",
                        column: x => x.user_id,
                        principalTable: "identity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_project_access_user",
                table: "project_access",
                column: "user_id");

            // Every user that exists gets every project that exists, before the
            // first request is narrowed by any of it (ADR 0055). An upgrade that
            // silently took access away from somebody who had it would be the
            // worst possible first impression of this feature, and the direction
            // the two rules point is deliberately asymmetric: an upgrade loses
            // nobody, and a new account gains nothing.
            //
            // Granted by the user themselves, because there is nobody else it
            // could truthfully name: this is the schema catching up with what
            // was already true, not somebody handing anything out.
            migrationBuilder.Sql(
                """
                insert into project_access (project_id, user_id, granted_by, granted_at)
                select p.id, u.id, u.id, now()
                from project p
                cross join identity u
                where u.kind = 0
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_access");
        }
    }
}
