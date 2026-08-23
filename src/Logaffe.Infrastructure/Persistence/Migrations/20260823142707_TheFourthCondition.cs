using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheFourthCondition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "alert_on_failing",
                table: "installation_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "alert_on_failing",
                table: "installation_settings");
        }
    }
}
