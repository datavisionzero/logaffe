using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InstallationHost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "host_id",
                table: "installation_settings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mount_path",
                table: "installation_settings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_installation_settings_host",
                table: "installation_settings",
                column: "host_id");

            migrationBuilder.AddForeignKey(
                name: "fk_installation_settings_host",
                table: "installation_settings",
                column: "host_id",
                principalTable: "host",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_installation_settings_host",
                table: "installation_settings");

            migrationBuilder.DropIndex(
                name: "ix_installation_settings_host",
                table: "installation_settings");

            migrationBuilder.DropColumn(
                name: "host_id",
                table: "installation_settings");

            migrationBuilder.DropColumn(
                name: "mount_path",
                table: "installation_settings");
        }
    }
}
