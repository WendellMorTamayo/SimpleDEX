using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimpleDEX.Data.Migrations
{
    /// <inheritdoc />
    public partial class SplitOwnerAddressIntoOwnerPkhAndDestination : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "OwnerAddress",
                table: "Orders",
                newName: "OwnerPkh");

            migrationBuilder.AddColumn<string>(
                name: "DestinationAddress",
                table: "Orders",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DestinationAddress",
                table: "Orders");

            migrationBuilder.RenameColumn(
                name: "OwnerPkh",
                table: "Orders",
                newName: "OwnerAddress");
        }
    }
}
