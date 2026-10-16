using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travel.AI.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CostLedgerUsageOutcomes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "output_tokens",
                schema: "ai",
                table: "cost_ledger",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer"
            );

            migrationBuilder.AlterColumn<int>(
                name: "input_tokens",
                schema: "ai",
                table: "cost_ledger",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer"
            );

            migrationBuilder.AlterColumn<decimal>(
                name: "cost_usd",
                schema: "ai",
                table: "cost_ledger",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric"
            );

            migrationBuilder.AddColumn<string>(
                name: "outcome",
                schema: "ai",
                table: "cost_ledger",
                type: "text",
                nullable: false,
                defaultValue: "LegacyRecorded"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                LOCK TABLE ai.cost_ledger IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM ai.cost_ledger
                        WHERE input_tokens IS NULL OR output_tokens IS NULL OR cost_usd IS NULL
                            OR outcome <> 'LegacyRecorded'
                    ) THEN
                        RAISE EXCEPTION 'Cannot remove usage outcomes: retained accounting records require the nullable outcome schema.';
                    END IF;
                END $$;
                """
            );
            migrationBuilder.DropColumn(name: "outcome", schema: "ai", table: "cost_ledger");

            migrationBuilder.AlterColumn<int>(
                name: "output_tokens",
                schema: "ai",
                table: "cost_ledger",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<int>(
                name: "input_tokens",
                schema: "ai",
                table: "cost_ledger",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<decimal>(
                name: "cost_usd",
                schema: "ai",
                table: "cost_ledger",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true
            );
        }
    }
}
