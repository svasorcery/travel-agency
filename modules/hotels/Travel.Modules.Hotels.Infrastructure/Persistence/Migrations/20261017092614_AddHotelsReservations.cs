using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travel.Modules.Hotels.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHotelsReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "hotels");

            migrationBuilder.CreateTable(
                name: "observation_inbox",
                schema: "hotels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    receiver = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    event_id = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    event_type = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: false
                    ),
                    preparation_reference = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: true
                    ),
                    protected_payload = table.Column<string>(
                        type: "character varying(4194304)",
                        maxLength: 4194304,
                        nullable: true
                    ),
                    received_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    processed_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    unmatched = table.Column<bool>(type: "boolean", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_observation_inbox", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "reservations",
                schema: "hotels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    quote_revision = table.Column<int>(type: "integer", nullable: false),
                    provider = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    environment = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false
                    ),
                    provider_account = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    booking_reference = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: true
                    ),
                    source_preparation_reference = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: true
                    ),
                    provider_request_reference = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: true
                    ),
                    outcome_diagnostic = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: true
                    ),
                    terms_json = table.Column<string>(type: "jsonb", nullable: true),
                    state_json = table.Column<string>(type: "jsonb", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    updated_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action_id = table.Column<Guid>(type: "uuid", nullable: true),
                    admitted_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    sender_finished = table.Column<bool>(type: "boolean", nullable: false),
                    protected_context = table.Column<string>(
                        type: "character varying(32768)",
                        maxLength: 32768,
                        nullable: true
                    ),
                    protected_party = table.Column<string>(
                        type: "character varying(32768)",
                        maxLength: 32768,
                        nullable: true
                    ),
                    private_purge_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reservations", x => x.id);
                    table.CheckConstraint(
                        "ck_hotels_attempt",
                        "(attempt_id IS NULL AND action_id IS NULL AND admitted_at IS NULL) OR (attempt_id IS NOT NULL AND action_id IS NOT NULL AND admitted_at IS NOT NULL)"
                    );
                    table.CheckConstraint(
                        "ck_hotels_revision",
                        "version > 0 AND quote_revision > 0"
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "ix_observation_inbox_received_at",
                schema: "hotels",
                table: "observation_inbox",
                column: "received_at"
            );

            migrationBuilder.CreateIndex(
                name: "ix_observation_inbox_receiver_event_id",
                schema: "hotels",
                table: "observation_inbox",
                columns: new[] { "receiver", "event_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "ix_reservations_attempt_id",
                schema: "hotels",
                table: "reservations",
                column: "attempt_id",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "ix_reservations_owner_id_created_at_id",
                schema: "hotels",
                table: "reservations",
                columns: new[] { "owner_id", "created_at", "id" }
            );

            migrationBuilder.CreateIndex(
                name: "ix_reservations_owner_id_id_action_id",
                schema: "hotels",
                table: "reservations",
                columns: new[] { "owner_id", "id", "action_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "ix_reservations_private_purge_at_id",
                schema: "hotels",
                table: "reservations",
                columns: new[] { "private_purge_at", "id" }
            );

            migrationBuilder.CreateIndex(
                name: "ix_reservations_provider_provider_account_environment_booking_",
                schema: "hotels",
                table: "reservations",
                columns: new[]
                {
                    "provider",
                    "provider_account",
                    "environment",
                    "booking_reference",
                },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "ix_reservations_provider_provider_account_environment_source_p",
                schema: "hotels",
                table: "reservations",
                columns: new[]
                {
                    "provider",
                    "provider_account",
                    "environment",
                    "source_preparation_reference",
                },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "observation_inbox", schema: "hotels");

            migrationBuilder.DropTable(name: "reservations", schema: "hotels");
        }
    }
}
