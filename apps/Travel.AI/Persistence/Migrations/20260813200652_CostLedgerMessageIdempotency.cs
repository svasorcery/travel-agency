using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travel.AI.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CostLedgerMessageIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "message_identity",
                schema: "ai",
                table: "cost_ledger",
                type: "text",
                nullable: true
            );

            migrationBuilder.Sql(
                """
                UPDATE ai.cost_ledger
                SET message_identity = 'travel.ai.nl-search.requested';
                """
            );

            migrationBuilder.AlterColumn<string>(
                name: "message_identity",
                schema: "ai",
                table: "cost_ledger",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true
            );

            migrationBuilder.CreateIndex(
                name: "ix_cost_ledger_message_identity_correlation_id",
                schema: "ai",
                table: "cost_ledger",
                columns: new[] { "message_identity", "correlation_id" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_cost_ledger_message_identity_correlation_id",
                schema: "ai",
                table: "cost_ledger"
            );

            migrationBuilder.DropColumn(
                name: "message_identity",
                schema: "ai",
                table: "cost_ledger"
            );
        }
    }
}
