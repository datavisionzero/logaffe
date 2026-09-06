using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Identities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The order is what a failure in the middle would leave behind. The
            // table is created and filled before anything is dropped, so an
            // installation that stops halfway still holds its operator's
            // password rather than having lost the only copy of it.
            migrationBuilder.DropForeignKey(
                name: "fk_backup_code_operator",
                table: "backup_code");

            migrationBuilder.DropForeignKey(
                name: "fk_session_operator",
                table: "session");

            migrationBuilder.RenameColumn(
                name: "operator_id",
                table: "session",
                newName: "user_id");

            migrationBuilder.RenameIndex(
                name: "ix_session_operator",
                table: "session",
                newName: "ix_session_user");

            migrationBuilder.RenameColumn(
                name: "operator_id",
                table: "backup_code",
                newName: "user_id");

            migrationBuilder.RenameIndex(
                name: "ix_backup_code_operator",
                table: "backup_code",
                newName: "ix_backup_code_user");

            migrationBuilder.CreateTable(
                name: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    administrator = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    state = table.Column<int>(type: "integer", nullable: true),
                    password_hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    second_factor_secret = table.Column<byte[]>(type: "bytea", nullable: true),
                    second_factor_enrolled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identity", x => x.id);
                    table.CheckConstraint("ck_identity_kind", " \"kind\" in (0, 1) ");
                    table.CheckConstraint("ck_identity_owner", "\"kind\" = 0 and \"owner_id\" is null\nor \"kind\" = 1 and \"owner_id\" is not null and not \"administrator\"");
                    table.CheckConstraint("ck_identity_user", "\"kind\" = 0\n    and \"email\" is not null and \"normalized_email\" is not null\n    and \"state\" in (0, 1, 2)\nor \"kind\" = 1\n    and \"email\" is null and \"normalized_email\" is null\n    and \"state\" is null and \"password_hash\" is null\n    and \"second_factor_secret\" is null");
                    table.ForeignKey(
                        name: "fk_identity_owner",
                        column: x => x.owner_id,
                        principalTable: "identity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_identity_normalized_email",
                table: "identity",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_identity_owner",
                table: "identity",
                column: "owner_id");

            // The operator becomes the first administrator, keeping its id — so
            // every session and every backup code still points at it — and
            // keeping its password, its second factor and its enrolment date.
            // Nobody is signed out and nobody has to reset anything.
            //
            // The address is the one thing it cannot bring, because it never had
            // one (ADR 0015). It is parked in the reserved `.invalid` domain,
            // where nothing can be delivered and no real address can collide,
            // and the bootstrap on this same start reads the real one out of the
            // configuration and writes it here — refusing to start if the
            // configuration does not name one (ADR 0054). So this value is never
            // an address anybody signs in with; it exists for the seconds
            // between two steps of one start.
            migrationBuilder.Sql(
                """
                insert into identity (
                    id, kind, name, administrator, created_at,
                    email, normalized_email, state,
                    password_hash, second_factor_secret, second_factor_enrolled_at)
                select
                    id, 0, 'Operator', true, claimed_at,
                    id::text || '@bootstrap.invalid',
                    id::text || '@bootstrap.invalid',
                    1,
                    password_hash, second_factor_secret, second_factor_enrolled_at
                from operator
                """);

            migrationBuilder.AddForeignKey(
                name: "fk_backup_code_user",
                table: "backup_code",
                column: "user_id",
                principalTable: "identity",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_session_user",
                table: "session",
                column: "user_id",
                principalTable: "identity",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            // Last, and only once everything that was in them is somewhere else.
            // The claim is gone entirely — the guard, its window and the hash of
            // the secret it drew (ADR 0054).
            migrationBuilder.DropTable(name: "operator");
            migrationBuilder.DropTable(name: "claim_guard");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_backup_code_user",
                table: "backup_code");

            migrationBuilder.DropForeignKey(
                name: "fk_session_user",
                table: "session");

            migrationBuilder.DropTable(
                name: "identity");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "session",
                newName: "operator_id");

            migrationBuilder.RenameIndex(
                name: "ix_session_user",
                table: "session",
                newName: "ix_session_operator");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "backup_code",
                newName: "operator_id");

            migrationBuilder.RenameIndex(
                name: "ix_backup_code_user",
                table: "backup_code",
                newName: "ix_backup_code_operator");

            migrationBuilder.CreateTable(
                name: "claim_guard",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    drawn_secret_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    only_guard = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_claim_guard", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "operator",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    second_factor_secret = table.Column<byte[]>(type: "bytea", nullable: true),
                    only_operator = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    password_hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    second_factor_enrolled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operator", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_claim_guard_only_one",
                table: "claim_guard",
                column: "only_guard",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_operator_only_one",
                table: "operator",
                column: "only_operator",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_backup_code_operator",
                table: "backup_code",
                column: "operator_id",
                principalTable: "operator",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_session_operator",
                table: "session",
                column: "operator_id",
                principalTable: "operator",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
