using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlertConditions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "muted",
                table: "project",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "alert_on_filling_up",
                table: "installation_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "alert_on_flooding",
                table: "installation_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "alert_on_gone_quiet",
                table: "installation_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "alert_condition_state",
                columns: table => new
                {
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    condition = table.Column<short>(type: "smallint", nullable: false),
                    latched = table.Column<int>(type: "integer", nullable: false),
                    notified_level = table.Column<int>(type: "integer", nullable: false),
                    notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alert_condition_state", x => new { x.subject_id, x.condition });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alert_condition_state");

            migrationBuilder.DropColumn(
                name: "muted",
                table: "project");

            migrationBuilder.DropColumn(
                name: "alert_on_filling_up",
                table: "installation_settings");

            migrationBuilder.DropColumn(
                name: "alert_on_flooding",
                table: "installation_settings");

            migrationBuilder.DropColumn(
                name: "alert_on_gone_quiet",
                table: "installation_settings");
        }
    }
}
