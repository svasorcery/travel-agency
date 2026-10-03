using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travel.Modules.Flights.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSavedTravelers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "saved_travelers",
                schema: "flights",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<Guid>(type: "uuid", nullable: false),
                    protected_details_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    updated_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_travelers", x => x.id);
                    table.CheckConstraint(
                        "ck_saved_travelers_identifiers",
                        "id <> '00000000-0000-0000-0000-000000000000'::uuid AND owner_user_id <> '00000000-0000-0000-0000-000000000000'::uuid AND revision <> '00000000-0000-0000-0000-000000000000'::uuid"
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "ix_saved_travelers_owner_created_id",
                schema: "flights",
                table: "saved_travelers",
                columns: new[] { "owner_user_id", "created_at", "id" },
                descending: new[] { false, true, true }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "saved_travelers", schema: "flights");
        }
    }
}
