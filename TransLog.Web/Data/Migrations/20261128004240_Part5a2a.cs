using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransLog.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class Part5a2a : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AiProviderId",
                table: "Companies",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AiProviders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    BaseUrl = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ApiKeyProtected = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ApiKeyHint = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    OfficialDomain = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PricingUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiProviders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiProviders_AspNetUsers_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlatformSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformSettings", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PlatformSettings_AspNetUsers_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AiPrices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AiProviderId = table.Column<int>(type: "int", nullable: false),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    InputPerM = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: false),
                    CachedInputPerM = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: false),
                    OutputPerM = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: false),
                    OffPeakInputPerM = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: true),
                    OffPeakCachedPerM = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: true),
                    OffPeakOutputPerM = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: true),
                    PeakHoursUtc = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PeakDays = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Source = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiPrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiPrices_AiProviders_AiProviderId",
                        column: x => x.AiProviderId,
                        principalTable: "AiProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiPrices_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Companies_AiProviderId",
                table: "Companies",
                column: "AiProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_AiPrices_AiProviderId_Model_EffectiveFrom",
                table: "AiPrices",
                columns: new[] { "AiProviderId", "Model", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_AiPrices_CreatedByUserId",
                table: "AiPrices",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AiProviders_Key",
                table: "AiProviders",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiProviders_UpdatedByUserId",
                table: "AiProviders",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformSettings_UpdatedByUserId",
                table: "PlatformSettings",
                column: "UpdatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Companies_AiProviders_AiProviderId",
                table: "Companies",
                column: "AiProviderId",
                principalTable: "AiProviders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Companies_AiProviders_AiProviderId",
                table: "Companies");

            migrationBuilder.DropTable(
                name: "AiPrices");

            migrationBuilder.DropTable(
                name: "PlatformSettings");

            migrationBuilder.DropTable(
                name: "AiProviders");

            migrationBuilder.DropIndex(
                name: "IX_Companies_AiProviderId",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "AiProviderId",
                table: "Companies");
        }
    }
}
