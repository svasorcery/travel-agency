using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travel.Modules.Flights.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderReadModelProjectedStreamVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "projected_stream_version",
                schema: "flights",
                table: "order_read_model",
                type: "bigint",
                nullable: false,
                defaultValue: -1L
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "projected_stream_version",
                schema: "flights",
                table: "order_read_model"
            );
        }
    }
}
