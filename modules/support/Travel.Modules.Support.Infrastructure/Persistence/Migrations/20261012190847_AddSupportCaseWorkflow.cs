using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travel.Modules.Support.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportCaseWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "support");

            migrationBuilder.CreateTable(
                name: "cases",
                schema: "support",
                columns: table => new
                {
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    protected_title = table.Column<string>(
                        type: "character varying(32768)",
                        maxLength: 32768,
                        nullable: false
                    ),
                    title_written_at_revision = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    category = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    priority = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false
                    ),
                    triage_source = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false
                    ),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_stream_version = table.Column<long>(type: "bigint", nullable: true),
                    order_verified_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    updated_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    resolved_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    purge_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cases", x => x.case_id);
                    table.CheckConstraint(
                        "ck_cases_order_link",
                        "(order_id IS NULL AND order_stream_version IS NULL AND order_verified_at IS NULL) OR (order_id IS NOT NULL AND order_stream_version IS NOT NULL AND order_stream_version > 0 AND order_verified_at IS NOT NULL)"
                    );
                    table.CheckConstraint(
                        "ck_cases_resolution",
                        "(state = 'Resolved' AND resolved_at IS NOT NULL AND purge_at IS NOT NULL) OR (state <> 'Resolved' AND resolved_at IS NULL AND purge_at IS NULL)"
                    );
                    table.CheckConstraint(
                        "ck_cases_revision",
                        "revision > 0 AND title_written_at_revision > 0"
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "action_receipts",
                schema: "support",
                columns: table => new
                {
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_kind = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false
                    ),
                    expected_revision = table.Column<int>(type: "integer", nullable: false),
                    applied_revision = table.Column<int>(type: "integer", nullable: false),
                    applied_state = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    applied_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    previous_category = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: true
                    ),
                    previous_priority = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: true
                    ),
                    requested_category = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: true
                    ),
                    requested_priority = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_action_receipts", x => new { x.case_id, x.action_id });
                    table.CheckConstraint(
                        "ck_receipts_revision",
                        "expected_revision >= 0 AND applied_revision > 0"
                    );
                    table.ForeignKey(
                        name: "fk_action_receipts_cases_case_id",
                        column: x => x.case_id,
                        principalSchema: "support",
                        principalTable: "cases",
                        principalColumn: "case_id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "messages",
                schema: "support",
                columns: table => new
                {
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_kind = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false
                    ),
                    protected_body = table.Column<string>(
                        type: "character varying(32768)",
                        maxLength: 32768,
                        nullable: false
                    ),
                    written_at_revision = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_messages", x => new { x.case_id, x.message_id });
                    table.CheckConstraint("ck_messages_revision", "written_at_revision > 0");
                    table.ForeignKey(
                        name: "fk_messages_cases_case_id",
                        column: x => x.case_id,
                        principalSchema: "support",
                        principalTable: "cases",
                        principalColumn: "case_id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "ix_cases_created_at_case_id",
                schema: "support",
                table: "cases",
                columns: new[] { "created_at", "case_id" }
            );

            migrationBuilder.CreateIndex(
                name: "ix_cases_owner_id_created_at_case_id",
                schema: "support",
                table: "cases",
                columns: new[] { "owner_id", "created_at", "case_id" }
            );

            migrationBuilder.CreateIndex(
                name: "ix_cases_state_purge_at_case_id",
                schema: "support",
                table: "cases",
                columns: new[] { "state", "purge_at", "case_id" }
            );

            migrationBuilder.CreateIndex(
                name: "ix_messages_case_id_written_at_revision_message_id",
                schema: "support",
                table: "messages",
                columns: new[] { "case_id", "written_at_revision", "message_id" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "action_receipts", schema: "support");

            migrationBuilder.DropTable(name: "messages", schema: "support");

            migrationBuilder.DropTable(name: "cases", schema: "support");
        }
    }
}
