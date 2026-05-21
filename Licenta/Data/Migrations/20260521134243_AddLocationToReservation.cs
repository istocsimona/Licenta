using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Licenta.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationToReservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Location",
                table: "Reservations");

            migrationBuilder.AddColumn<int>(
                name: "LocationId",
                table: "Reservations",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_LocationId",
                table: "Reservations",
                column: "LocationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Reservations_Locations_LocationId",
                table: "Reservations",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "LocationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Reservations_Locations_LocationId",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_LocationId",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "Reservations");

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "Reservations",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }
    }
}
