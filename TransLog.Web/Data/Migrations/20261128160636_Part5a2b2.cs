using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransLog.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class Part5a2b2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AiAttempts",
                table: "Captures",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AiCachedTokens",
                table: "Captures",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AiCostUsd",
                table: "Captures",
                type: "decimal(12,6)",
                precision: 12,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AiCostZar",
                table: "Captures",
                type: "decimal(12,4)",
                precision: 12,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AiInputTokens",
                table: "Captures",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AiLockedUntil",
                table: "Captures",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AiMilliseconds",
                table: "Captures",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiModel",
                table: "Captures",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AiNextTryAt",
                table: "Captures",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AiOutputTokens",
                table: "Captures",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AiPeak",
                table: "Captures",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AiPriceId",
                table: "Captures",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AiProviderId",
                table: "Captures",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiRawJson",
                table: "Captures",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AiReadAt",
                table: "Captures",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AiUsdToZar",
                table: "Captures",
                type: "decimal(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Amount",
                table: "Captures",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CaptureTypeId",
                table: "Captures",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DataJson",
                table: "Captures",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DetectRawJson",
                table: "Captures",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DetectVersion",
                table: "Captures",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ErrorMessage",
                table: "Captures",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EventDate",
                table: "Captures",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Odometer",
                table: "Captures",
                type: "decimal(12,1)",
                precision: 12,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Quantity",
                table: "Captures",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceNo",
                table: "Captures",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SystemVersion",
                table: "Captures",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TypeConfidence",
                table: "Captures",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TypeVersion",
                table: "Captures",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Vendor",
                table: "Captures",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Captures_AiPriceId",
                table: "Captures",
                column: "AiPriceId");

            migrationBuilder.CreateIndex(
                name: "IX_Captures_AiProviderId",
                table: "Captures",
                column: "AiProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_Captures_AiReadAt",
                table: "Captures",
                column: "AiReadAt");

            migrationBuilder.CreateIndex(
                name: "IX_Captures_CaptureTypeId",
                table: "Captures",
                column: "CaptureTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Captures_CompanyId_EventDate",
                table: "Captures",
                columns: new[] { "CompanyId", "EventDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Captures_Status_AiNextTryAt",
                table: "Captures",
                columns: new[] { "Status", "AiNextTryAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_Captures_AiPrices_AiPriceId",
                table: "Captures",
                column: "AiPriceId",
                principalTable: "AiPrices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Captures_AiProviders_AiProviderId",
                table: "Captures",
                column: "AiProviderId",
                principalTable: "AiProviders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Captures_CaptureTypes_CaptureTypeId",
                table: "Captures",
                column: "CaptureTypeId",
                principalTable: "CaptureTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Captures_AiPrices_AiPriceId",
                table: "Captures");

            migrationBuilder.DropForeignKey(
                name: "FK_Captures_AiProviders_AiProviderId",
                table: "Captures");

            migrationBuilder.DropForeignKey(
                name: "FK_Captures_CaptureTypes_CaptureTypeId",
                table: "Captures");

            migrationBuilder.DropIndex(
                name: "IX_Captures_AiPriceId",
                table: "Captures");

            migrationBuilder.DropIndex(
                name: "IX_Captures_AiProviderId",
                table: "Captures");

            migrationBuilder.DropIndex(
                name: "IX_Captures_AiReadAt",
                table: "Captures");

            migrationBuilder.DropIndex(
                name: "IX_Captures_CaptureTypeId",
                table: "Captures");

            migrationBuilder.DropIndex(
                name: "IX_Captures_CompanyId_EventDate",
                table: "Captures");

            migrationBuilder.DropIndex(
                name: "IX_Captures_Status_AiNextTryAt",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "AiAttempts",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "AiCachedTokens",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "AiCostUsd",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "AiCostZar",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "AiInputTokens",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "AiLockedUntil",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "AiMilliseconds",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "AiModel",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "AiNextTryAt",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "AiOutputTokens",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "AiPeak",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "AiPriceId",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "AiProviderId",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "AiRawJson",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "AiReadAt",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "AiUsdToZar",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "Amount",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "CaptureTypeId",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "DataJson",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "DetectRawJson",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "DetectVersion",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "ErrorMessage",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "EventDate",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "Odometer",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "ReferenceNo",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "SystemVersion",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "TypeConfidence",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "TypeVersion",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "Vendor",
                table: "Captures");
        }
    }
}
