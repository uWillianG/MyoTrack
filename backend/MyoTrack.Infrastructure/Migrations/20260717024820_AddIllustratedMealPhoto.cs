using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyoTrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIllustratedMealPhoto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IllustratedMediaKey",
                table: "MealPhotoAnalyses",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IllustratedMediaKey",
                table: "MealPhotoAnalyses");
        }
    }
}
