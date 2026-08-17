using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logaffe.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The claim is guarded by a secret or by a window, and the second factor is
    /// the operator's to enrol (ADR 0040, ADR 0041).
    /// </summary>
    /// <remarks>
    /// The one row an installation holds about itself is <b>renamed rather than
    /// replaced</b>. What EF scaffolded was a drop and a create, and that would
    /// have thrown away the instant the installation first ran — which on an
    /// unclaimed installation in window mode means the next start writes a first
    /// run that is not one and opens a fresh thirty minutes on a window that had
    /// lapsed. A restart does not extend the window (ADR 0034), and neither does
    /// an upgrade.
    /// </remarks>
    public partial class ClaimGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(name: "claim_window", newName: "claim_guard");

            migrationBuilder.RenameColumn(
                name: "only_window", table: "claim_guard", newName: "only_guard");

            migrationBuilder.RenameIndex(
                name: "ix_claim_window_only_one",
                newName: "ix_claim_guard_only_one",
                table: "claim_guard");

            // There is no RenameConstraint on the builder, and a primary key that
            // kept the old name would be a row the next migration cannot find by
            // the name the model gives it.
            migrationBuilder.Sql(
                "ALTER TABLE claim_guard RENAME CONSTRAINT pk_claim_window TO pk_claim_guard;");

            // Null on every installation that already exists, which is what it
            // means: none of them drew a secret, because until now there was
            // nothing to draw.
            migrationBuilder.AddColumn<byte[]>(
                name: "drawn_secret_hash",
                table: "claim_guard",
                type: "bytea",
                nullable: true);

            // Both together: an account that has enrolled no second factor holds
            // neither the secret nor the date.
            migrationBuilder.AlterColumn<byte[]>(
                name: "second_factor_secret",
                table: "operator",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "bytea");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "second_factor_enrolled_at",
                table: "operator",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "drawn_secret_hash", table: "claim_guard");

            migrationBuilder.Sql(
                "ALTER TABLE claim_guard RENAME CONSTRAINT pk_claim_guard TO pk_claim_window;");

            migrationBuilder.RenameIndex(
                name: "ix_claim_guard_only_one",
                newName: "ix_claim_window_only_one",
                table: "claim_guard");

            migrationBuilder.RenameColumn(
                name: "only_guard", table: "claim_guard", newName: "only_window");

            migrationBuilder.RenameTable(name: "claim_guard", newName: "claim_window");

            // Going back means every account has a second factor again, and one
            // that has none cannot be made to. Postgres refuses the alteration
            // rather than inventing a secret, which is the honest failure.
            migrationBuilder.AlterColumn<byte[]>(
                name: "second_factor_secret",
                table: "operator",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0],
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "second_factor_enrolled_at",
                table: "operator",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }
    }
}
