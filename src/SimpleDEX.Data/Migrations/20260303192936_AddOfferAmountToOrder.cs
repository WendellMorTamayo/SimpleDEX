using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimpleDEX.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOfferAmountToOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "OfferAmount",
                table: "Orders",
                type: "numeric(20,0)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OfferAmount",
                table: "Orders");
        }
    }
}
