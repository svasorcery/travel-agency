using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travel.AI.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CostLedgerInit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "ai");

            migrationBuilder.CreateTable(
                name: "cost_ledger",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature = table.Column<string>(type: "text", nullable: false),
                    model = table.Column<string>(type: "text", nullable: false),
                    input_tokens = table.Column<int>(type: "integer", nullable: false),
                    output_tokens = table.Column<int>(type: "integer", nullable: false),
                    cost_usd = table.Column<decimal>(type: "numeric", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cost_ledger", x => x.id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "ix_cost_ledger_correlation_id",
                schema: "ai",
                table: "cost_ledger",
                column: "correlation_id"
            );

            migrationBuilder.CreateIndex(
                name: "ix_cost_ledger_occurred_at",
                schema: "ai",
                table: "cost_ledger",
                column: "occurred_at"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "cost_ledger", schema: "ai");
        }
    }
}
