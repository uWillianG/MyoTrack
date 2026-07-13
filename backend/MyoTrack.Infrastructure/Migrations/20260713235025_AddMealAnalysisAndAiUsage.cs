using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyoTrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMealAnalysisAndAiUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiUsageLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<int>(type: "integer", nullable: false),
                    Model = table.Column<string>(type: "text", nullable: false),
                    InputTokens = table.Column<long>(type: "bigint", nullable: false),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUsageLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MealPhotoAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnalysisJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaKey = table.Column<string>(type: "text", nullable: false),
                    ItemsJson = table.Column<string>(type: "jsonb", nullable: false),
                    TotalKcal = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalProteinG = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalCarbsG = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalFatG = table.Column<decimal>(type: "numeric", nullable: false),
                    UserAdjusted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealPhotoAnalyses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageLogs_UserId_CreatedAt",
                table: "AiUsageLogs",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MealPhotoAnalyses_AnalysisJobId",
                table: "MealPhotoAnalyses",
                column: "AnalysisJobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MealPhotoAnalyses_UserId_CreatedAt",
                table: "MealPhotoAnalyses",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiUsageLogs");

            migrationBuilder.DropTable(
                name: "MealPhotoAnalyses");
        }
    }
}
