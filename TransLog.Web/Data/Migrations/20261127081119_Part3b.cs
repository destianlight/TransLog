using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransLog.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class Part3b : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "Drivers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DriverVehicleLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    DriverId = table.Column<int>(type: "int", nullable: false),
                    VehicleId = table.Column<int>(type: "int", nullable: false),
                    LinkedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UnlinkedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LinkedBy = table.Column<int>(type: "int", nullable: false),
                    LinkedByUserId = table.Column<int>(type: "int", nullable: true),
                    UnlinkedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverVehicleLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DriverVehicleLinks_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DriverVehicleLinks_Drivers_CompanyId_DriverId",
                        columns: x => new { x.CompanyId, x.DriverId },
                        principalTable: "Drivers",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DriverVehicleLinks_Vehicles_CompanyId_VehicleId",
                        columns: x => new { x.CompanyId, x.VehicleId },
                        principalTable: "Vehicles",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_UserId",
                table: "Drivers",
                column: "UserId",
                unique: true,
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DriverVehicleLinks_CompanyId_DriverId_LinkedAt",
                table: "DriverVehicleLinks",
                columns: new[] { "CompanyId", "DriverId", "LinkedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DriverVehicleLinks_CompanyId_VehicleId_LinkedAt",
                table: "DriverVehicleLinks",
                columns: new[] { "CompanyId", "VehicleId", "LinkedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DriverVehicleLinks_DriverId",
                table: "DriverVehicleLinks",
                column: "DriverId",
                unique: true,
                filter: "[UnlinkedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DriverVehicleLinks_VehicleId",
                table: "DriverVehicleLinks",
                column: "VehicleId",
                unique: true,
                filter: "[UnlinkedAt] IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Drivers_AspNetUsers_UserId",
                table: "Drivers",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Drivers_AspNetUsers_UserId",
                table: "Drivers");

            migrationBuilder.DropTable(
                name: "DriverVehicleLinks");

            migrationBuilder.DropIndex(
                name: "IX_Drivers_UserId",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Drivers");
        }
    }
}
