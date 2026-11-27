using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransLog.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class Part3a : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Drivers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Surname = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Cell = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IdNumber = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: true),
                    LicenceNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    LicenceCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    LicenceExpiry = table.Column<DateOnly>(type: "date", nullable: true),
                    PrdpExpiry = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Drivers", x => x.Id);
                    table.UniqueConstraint("AK_Drivers_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_Drivers_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Vehicles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    RegistrationNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FleetNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Make = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Year = table.Column<int>(type: "int", nullable: true),
                    VehicleType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    FuelType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TankCapacity = table.Column<decimal>(type: "decimal(8,1)", precision: 8, scale: 1, nullable: true),
                    CurrentOdometer = table.Column<int>(type: "int", nullable: true),
                    LicenceDiscExpiry = table.Column<DateOnly>(type: "date", nullable: true),
                    RoadworthyExpiry = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.Id);
                    table.UniqueConstraint("AK_Vehicles_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_Vehicles_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VehicleAliases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    VehicleId = table.Column<int>(type: "int", nullable: false),
                    Alias = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VehicleAliases_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VehicleAliases_Vehicles_CompanyId_VehicleId",
                        columns: x => new { x.CompanyId, x.VehicleId },
                        principalTable: "Vehicles",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_CompanyId_Cell",
                table: "Drivers",
                columns: new[] { "CompanyId", "Cell" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_CompanyId_IdNumber",
                table: "Drivers",
                columns: new[] { "CompanyId", "IdNumber" },
                unique: true,
                filter: "[IdNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleAliases_CompanyId_Alias",
                table: "VehicleAliases",
                columns: new[] { "CompanyId", "Alias" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleAliases_CompanyId_VehicleId",
                table: "VehicleAliases",
                columns: new[] { "CompanyId", "VehicleId" });

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_CompanyId_RegistrationNo",
                table: "Vehicles",
                columns: new[] { "CompanyId", "RegistrationNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Drivers");

            migrationBuilder.DropTable(
                name: "VehicleAliases");

            migrationBuilder.DropTable(
                name: "Vehicles");
        }
    }
}
