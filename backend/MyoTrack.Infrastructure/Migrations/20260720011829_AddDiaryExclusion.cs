using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyoTrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDiaryExclusion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ExcludedFromDiary",
                table: "MealPhotoAnalyses",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExcludedFromDiary",
                table: "MealPhotoAnalyses");
        }
    }
}
