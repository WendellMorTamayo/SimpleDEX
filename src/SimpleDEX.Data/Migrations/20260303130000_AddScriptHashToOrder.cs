using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimpleDEX.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScriptHashToOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ScriptHash",
                table: "Orders",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScriptHash",
                table: "Orders");
        }
    }
}
