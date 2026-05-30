using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Licenta.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCityToLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Locations",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "City",
                table: "Locations");
        }
    }
}
