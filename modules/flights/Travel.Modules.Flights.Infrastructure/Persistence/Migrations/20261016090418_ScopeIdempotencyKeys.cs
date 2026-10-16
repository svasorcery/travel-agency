using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travel.Modules.Flights.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScopeIdempotencyKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_idempotency_keys",
                schema: "flights",
                table: "idempotency_keys"
            );

            migrationBuilder.AddPrimaryKey(
                name: "pk_idempotency_keys",
                schema: "flights",
                table: "idempotency_keys",
                columns: new[] { "key", "user_id", "route" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Never delete a retained receipt to make the former global key fit.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT key FROM flights.idempotency_keys GROUP BY key HAVING count(*) > 1) THEN
                        RAISE EXCEPTION 'Cannot restore global idempotency key: retained scoped keys overlap. Keep the scoped schema or wait for approved expiry cleanup.';
                    END IF;
                END $$;
                """
            );
            migrationBuilder.DropPrimaryKey(
                name: "pk_idempotency_keys",
                schema: "flights",
                table: "idempotency_keys"
            );

            migrationBuilder.AddPrimaryKey(
                name: "pk_idempotency_keys",
                schema: "flights",
                table: "idempotency_keys",
                column: "key"
            );
        }
    }
}
