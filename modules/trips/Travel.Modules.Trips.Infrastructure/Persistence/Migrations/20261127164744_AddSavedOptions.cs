using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travel.Modules.Trips.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSavedOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "protected_option",
                schema: "trips",
                table: "trip_plans",
                type: "text",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "saved_option_receipts",
                schema: "trips",
                columns: table => new
                {
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_issuer = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: false
                    ),
                    owner_subject = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_option_receipts", x => x.trip_id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "ix_saved_option_receipts_expires_at",
                schema: "trips",
                table: "saved_option_receipts",
                column: "expires_at"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fail closed, with writers excluded until both the checks and the drops finish.
            migrationBuilder.Sql(
                """
                LOCK TABLE trips.trip_plans, trips.saved_option_receipts IN ACCESS EXCLUSIVE MODE;
                DO $guard$
                BEGIN
                    IF EXISTS (SELECT 1 FROM trips.trip_plans WHERE protected_option IS NOT NULL)
                       OR EXISTS (SELECT 1 FROM trips.saved_option_receipts) THEN
                        RAISE EXCEPTION 'Saved options or receipts remain; preserve the expanded schema';
                    END IF;
                END
                $guard$;
                """
            );
            migrationBuilder.DropTable(name: "saved_option_receipts", schema: "trips");

            migrationBuilder.DropColumn(
                name: "protected_option",
                schema: "trips",
                table: "trip_plans"
            );
        }
    }
}
