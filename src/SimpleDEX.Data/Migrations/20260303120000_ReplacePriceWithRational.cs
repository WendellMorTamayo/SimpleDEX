using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimpleDEX.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplacePriceWithRational : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Price",
                table: "Orders",
                newName: "PriceNum");

            migrationBuilder.AddColumn<decimal>(
                name: "PriceDen",
                table: "Orders",
                type: "numeric(20,0)",
                nullable: false,
                defaultValue: 1m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PriceDen",
                table: "Orders");

            migrationBuilder.RenameColumn(
                name: "PriceNum",
                table: "Orders",
                newName: "Price");
        }
    }
}
