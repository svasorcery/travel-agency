using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travel.Modules.Flights.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderPassengerCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "passenger_count",
                schema: "flights",
                table: "order_read_model",
                type: "integer",
                nullable: false,
                defaultValue: 1
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_read_model_passenger_count",
                schema: "flights",
                table: "order_read_model",
                sql: "passenger_count BETWEEN 1 AND 9"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_order_read_model_passenger_count",
                schema: "flights",
                table: "order_read_model"
            );

            migrationBuilder.DropColumn(
                name: "passenger_count",
                schema: "flights",
                table: "order_read_model"
            );
        }
    }
}
