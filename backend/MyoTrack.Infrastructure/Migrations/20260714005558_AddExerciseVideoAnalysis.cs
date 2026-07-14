using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyoTrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExerciseVideoAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExerciseVideoAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnalysisJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaKey = table.Column<string>(type: "text", nullable: false),
                    OverlayVideoKey = table.Column<string>(type: "text", nullable: true),
                    AnalyzedExercise = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: true),
                    RepCount = table.Column<int>(type: "integer", nullable: false),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExerciseVideoAnalyses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseVideoAnalyses_AnalysisJobId",
                table: "ExerciseVideoAnalyses",
                column: "AnalysisJobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseVideoAnalyses_UserId_CreatedAt",
                table: "ExerciseVideoAnalyses",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExerciseVideoAnalyses");
        }
    }
}
