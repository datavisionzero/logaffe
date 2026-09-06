using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgentTokenOwners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nullable first, because there is nothing to point at yet.
            migrationBuilder.AddColumn<Guid>(
                name: "identity_id",
                table: "agent_token",
                type: "uuid",
                nullable: true);

            // An agent identity per token that already exists, owned by the
            // installation's first administrator — which on an upgraded
            // installation is the account that used to be the operator. The
            // agent takes the token's name, because that is what a record of
            // what it did reads as (ADR 0052).
            migrationBuilder.Sql(
                """
                insert into identity (id, kind, name, administrator, owner_id, created_at)
                select
                    gen_random_uuid(), 1, t.name, false,
                    (select id from identity
                     where kind = 0 and administrator
                     order by created_at
                     limit 1),
                    t.issued_at
                from agent_token t
                where exists (select 1 from identity where kind = 0 and administrator)
                """);

            // Paired by name and issue date, which is what the two rows were
            // just written from and is unique across them: the insert above made
            // exactly one agent per token.
            migrationBuilder.Sql(
                """
                update agent_token t
                set identity_id = i.id
                from identity i
                where i.kind = 1
                  and i.name = t.name
                  and i.created_at = t.issued_at
                  and t.identity_id is null
                """);

            // An installation holding agent tokens and no administrator to own
            // them is not a state this product can reach — issuing one needs a
            // session — but if it somehow held, those tokens belong to nobody
            // and can resolve to nothing. Removing them is what a credential
            // that admits nothing already is.
            migrationBuilder.Sql("delete from agent_token where identity_id is null");

            migrationBuilder.AlterColumn<Guid>(
                name: "identity_id",
                table: "agent_token",
                type: "uuid",
                nullable: false);

            migrationBuilder.CreateIndex(
                name: "ix_agent_token_identity",
                table: "agent_token",
                column: "identity_id");

            migrationBuilder.AddForeignKey(
                name: "fk_agent_token_identity",
                table: "agent_token",
                column: "identity_id",
                principalTable: "identity",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_agent_token_identity",
                table: "agent_token");

            migrationBuilder.DropIndex(
                name: "ix_agent_token_identity",
                table: "agent_token");

            migrationBuilder.DropColumn(
                name: "identity_id",
                table: "agent_token");
        }
    }
}
