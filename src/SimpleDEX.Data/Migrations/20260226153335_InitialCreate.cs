using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimpleDEX.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    OutRef = table.Column<string>(type: "text", nullable: false),
                    OwnerAddress = table.Column<string>(type: "text", nullable: false),
                    OfferSubject = table.Column<string>(type: "text", nullable: false),
                    AskSubject = table.Column<string>(type: "text", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Slot = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SpentSlot = table.Column<decimal>(type: "numeric(20,0)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.OutRef);
                });

            migrationBuilder.CreateTable(
                name: "ReducerStates",
                columns: table => new
                {
                    Name = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LatestIntersectionsJson = table.Column<string>(type: "text", nullable: false),
                    StartIntersectionJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReducerStates", x => x.Name);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "ReducerStates");
        }
    }
}
