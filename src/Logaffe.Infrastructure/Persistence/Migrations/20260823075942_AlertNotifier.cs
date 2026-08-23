using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlertNotifier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "notifier_access_token",
                table: "installation_settings",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notifier_server",
                table: "installation_settings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notifier_topic",
                table: "installation_settings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "notifier_access_token",
                table: "installation_settings");

            migrationBuilder.DropColumn(
                name: "notifier_server",
                table: "installation_settings");

            migrationBuilder.DropColumn(
                name: "notifier_topic",
                table: "installation_settings");
        }
    }
}
