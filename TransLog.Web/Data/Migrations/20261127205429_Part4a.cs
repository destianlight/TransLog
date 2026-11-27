using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransLog.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class Part4a : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Captures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    ClientKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<int>(type: "int", nullable: true),
                    DriverId = table.Column<int>(type: "int", nullable: true),
                    CapturedByUserId = table.Column<int>(type: "int", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Captures", x => x.Id);
                    table.UniqueConstraint("AK_Captures_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_Captures_AspNetUsers_CapturedByUserId",
                        column: x => x.CapturedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Captures_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Captures_Drivers_CompanyId_DriverId",
                        columns: x => new { x.CompanyId, x.DriverId },
                        principalTable: "Drivers",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Captures_Vehicles_CompanyId_VehicleId",
                        columns: x => new { x.CompanyId, x.VehicleId },
                        principalTable: "Vehicles",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CaptureImages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    CaptureId = table.Column<int>(type: "int", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ThumbPath = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    Bytes = table.Column<int>(type: "int", nullable: false),
                    Sha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Rotation = table.Column<int>(type: "int", nullable: false),
                    RotatedBy = table.Column<int>(type: "int", nullable: true),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Latitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    Longitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    Accuracy = table.Column<int>(type: "int", nullable: true),
                    LocationTime = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaptureImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CaptureImages_Captures_CompanyId_CaptureId",
                        columns: x => new { x.CompanyId, x.CaptureId },
                        principalTable: "Captures",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CaptureImages_CompanyId_CaptureId",
                table: "CaptureImages",
                columns: new[] { "CompanyId", "CaptureId" });

            migrationBuilder.CreateIndex(
                name: "IX_CaptureImages_CompanyId_Sha256",
                table: "CaptureImages",
                columns: new[] { "CompanyId", "Sha256" });

            migrationBuilder.CreateIndex(
                name: "IX_Captures_CapturedByUserId",
                table: "Captures",
                column: "CapturedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Captures_CompanyId_CapturedAt",
                table: "Captures",
                columns: new[] { "CompanyId", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Captures_CompanyId_ClientKey",
                table: "Captures",
                columns: new[] { "CompanyId", "ClientKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Captures_CompanyId_DriverId_CapturedAt",
                table: "Captures",
                columns: new[] { "CompanyId", "DriverId", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Captures_CompanyId_VehicleId_CapturedAt",
                table: "Captures",
                columns: new[] { "CompanyId", "VehicleId", "CapturedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CaptureImages");

            migrationBuilder.DropTable(
                name: "Captures");
        }
    }
}
