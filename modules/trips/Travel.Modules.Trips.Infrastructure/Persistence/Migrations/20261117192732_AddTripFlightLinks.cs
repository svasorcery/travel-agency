using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travel.Modules.Trips.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTripFlightLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trip_flight_links",
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
                    links_revision = table.Column<long>(type: "bigint", nullable: false),
                    protected_payload = table.Column<string>(
                        type: "character varying(8192)",
                        maxLength: 8192,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_flight_links", x => x.trip_id);
                    table.CheckConstraint(
                        "ck_trip_flight_links_revision_positive",
                        "links_revision > 0"
                    );
                    table.ForeignKey(
                        name: "fk_trip_flight_links_trip_plans_trip_id",
                        column: x => x.trip_id,
                        principalSchema: "trips",
                        principalTable: "trip_plans",
                        principalColumn: "trip_id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "trip_flight_links", schema: "trips");
        }
    }
}
