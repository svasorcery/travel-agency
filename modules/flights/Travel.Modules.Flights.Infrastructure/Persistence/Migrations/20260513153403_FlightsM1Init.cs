using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travel.Modules.Flights.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FlightsM1Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "flights");

            migrationBuilder.CreateTable(
                name: "deeplink_offers_cache",
                schema: "flights",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    criteria_hash = table.Column<string>(type: "text", nullable: false),
                    offers_json = table.Column<string>(type: "jsonb", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    expires_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deeplink_offers_cache", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                schema: "flights",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    route = table.Column<string>(type: "text", nullable: false),
                    body_hash = table.Column<string>(type: "text", nullable: false),
                    response_hash = table.Column<string>(type: "text", nullable: true),
                    response_status = table.Column<int>(type: "integer", nullable: false),
                    response_body = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    expires_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_keys", x => x.key);
                }
            );

            migrationBuilder.CreateTable(
                name: "order_read_model",
                schema: "flights",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    aggregate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider_order_id = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    itinerary_json = table.Column<string>(type: "jsonb", nullable: false),
                    passenger_info_json = table.Column<string>(type: "jsonb", nullable: false),
                    ticket_numbers = table.Column<string[]>(type: "text[]", nullable: false),
                    booked_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    ticketed_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    cancelled_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    refunded_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_read_model", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "webhook_inbox",
                schema: "flights",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    event_id = table.Column<string>(type: "text", nullable: false),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    raw_payload = table.Column<string>(type: "jsonb", nullable: false),
                    signature = table.Column<string>(type: "text", nullable: false),
                    received_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    processed_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_inbox", x => x.id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "ix_deeplink_offers_cache_criteria_hash_expires_at",
                schema: "flights",
                table: "deeplink_offers_cache",
                columns: new[] { "criteria_hash", "expires_at" }
            );

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_keys_expires_at",
                schema: "flights",
                table: "idempotency_keys",
                column: "expires_at"
            );

            migrationBuilder.CreateIndex(
                name: "ix_order_read_model_aggregate_id",
                schema: "flights",
                table: "order_read_model",
                column: "aggregate_id",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "ix_order_read_model_user_id_booked_at",
                schema: "flights",
                table: "order_read_model",
                columns: new[] { "user_id", "booked_at" },
                descending: new[] { false, true }
            );

            migrationBuilder.CreateIndex(
                name: "ix_webhook_inbox_source_event_id",
                schema: "flights",
                table: "webhook_inbox",
                columns: new[] { "source", "event_id" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "deeplink_offers_cache", schema: "flights");

            migrationBuilder.DropTable(name: "idempotency_keys", schema: "flights");

            migrationBuilder.DropTable(name: "order_read_model", schema: "flights");

            migrationBuilder.DropTable(name: "webhook_inbox", schema: "flights");
        }
    }
}
