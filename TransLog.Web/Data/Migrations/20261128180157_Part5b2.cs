using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransLog.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class Part5b2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DriverEditsLocked",
                table: "Companies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "RemovedAt",
                table: "Captures",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RemovedByUserId",
                table: "Captures",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StatusBeforeRemove",
                table: "Captures",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CaptureEdits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    CaptureId = table.Column<int>(type: "int", nullable: false),
                    FieldKey = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    OldValue = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ByDriver = table.Column<bool>(type: "bit", nullable: false),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaptureEdits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CaptureEdits_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CaptureEdits_Captures_CompanyId_CaptureId",
                        columns: x => new { x.CompanyId, x.CaptureId },
                        principalTable: "Captures",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CaptureEdits_CompanyId_CaptureId",
                table: "CaptureEdits",
                columns: new[] { "CompanyId", "CaptureId" });

            migrationBuilder.CreateIndex(
                name: "IX_CaptureEdits_UserId",
                table: "CaptureEdits",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CaptureEdits");

            migrationBuilder.DropColumn(
                name: "DriverEditsLocked",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "RemovedAt",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "RemovedByUserId",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "StatusBeforeRemove",
                table: "Captures");
        }
    }
}
