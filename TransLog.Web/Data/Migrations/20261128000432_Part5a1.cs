using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransLog.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class Part5a1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CaptureTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FieldsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Prompt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CurrentVersion = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaptureTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CaptureTypes_AspNetUsers_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CaptureTypeCompanies",
                columns: table => new
                {
                    CaptureTypeId = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaptureTypeCompanies", x => new { x.CaptureTypeId, x.CompanyId });
                    table.ForeignKey(
                        name: "FK_CaptureTypeCompanies_CaptureTypes_CaptureTypeId",
                        column: x => x.CaptureTypeId,
                        principalTable: "CaptureTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CaptureTypeCompanies_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CaptureTypeVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CaptureTypeId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FieldsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Prompt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    CompanyIds = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaptureTypeVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CaptureTypeVersions_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CaptureTypeVersions_CaptureTypes_CaptureTypeId",
                        column: x => x.CaptureTypeId,
                        principalTable: "CaptureTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CaptureTypeCompanies_CompanyId",
                table: "CaptureTypeCompanies",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_CaptureTypes_Key",
                table: "CaptureTypes",
                column: "Key",
                unique: true,
                filter: "[Scope] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CaptureTypes_Kind_Key",
                table: "CaptureTypes",
                columns: new[] { "Kind", "Key" });

            migrationBuilder.CreateIndex(
                name: "IX_CaptureTypes_UpdatedByUserId",
                table: "CaptureTypes",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CaptureTypeVersions_CaptureTypeId_Version",
                table: "CaptureTypeVersions",
                columns: new[] { "CaptureTypeId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CaptureTypeVersions_CreatedByUserId",
                table: "CaptureTypeVersions",
                column: "CreatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CaptureTypeCompanies");

            migrationBuilder.DropTable(
                name: "CaptureTypeVersions");

            migrationBuilder.DropTable(
                name: "CaptureTypes");
        }
    }
}
