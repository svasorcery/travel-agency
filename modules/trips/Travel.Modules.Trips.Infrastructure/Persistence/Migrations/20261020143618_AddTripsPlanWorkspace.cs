using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travel.Modules.Trips.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTripsPlanWorkspace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "trips");

            migrationBuilder.CreateTable(
                name: "trip_plans",
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
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    updated_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    protected_payload = table.Column<string>(type: "text", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_plans", x => x.trip_id);
                    table.CheckConstraint("ck_trip_plans_revision_positive", "revision > 0");
                }
            );

            migrationBuilder.CreateIndex(
                name: "ix_trip_plans_owner_issuer_owner_subject_updated_at_trip_id",
                schema: "trips",
                table: "trip_plans",
                columns: new[] { "owner_issuer", "owner_subject", "updated_at", "trip_id" },
                descending: new[] { false, false, true, false }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "trip_plans", schema: "trips");
        }
    }
}
