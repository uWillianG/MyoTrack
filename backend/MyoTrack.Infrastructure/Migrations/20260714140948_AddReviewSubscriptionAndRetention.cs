using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyoTrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewSubscriptionAndRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReviewNote",
                table: "WorkoutPlans",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReviewStatus",
                table: "WorkoutPlans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewedAt",
                table: "WorkoutPlans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedByUserId",
                table: "WorkoutPlans",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MediaExpiredAt",
                table: "MealPhotoAnalyses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MediaExpiredAt",
                table: "ExerciseVideoAnalyses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewNote",
                table: "DietPlans",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReviewStatus",
                table: "DietPlans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewedAt",
                table: "DietPlans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedByUserId",
                table: "DietPlans",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Plan = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    StripeCustomerId = table.Column<string>(type: "text", nullable: true),
                    StripeSubscriptionId = table.Column<string>(type: "text", nullable: true),
                    CurrentPeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSubscriptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_StripeCustomerId",
                table: "UserSubscriptions",
                column: "StripeCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_UserId",
                table: "UserSubscriptions",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserSubscriptions");

            migrationBuilder.DropColumn(
                name: "ReviewNote",
                table: "WorkoutPlans");

            migrationBuilder.DropColumn(
                name: "ReviewStatus",
                table: "WorkoutPlans");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "WorkoutPlans");

            migrationBuilder.DropColumn(
                name: "ReviewedByUserId",
                table: "WorkoutPlans");

            migrationBuilder.DropColumn(
                name: "MediaExpiredAt",
                table: "MealPhotoAnalyses");

            migrationBuilder.DropColumn(
                name: "MediaExpiredAt",
                table: "ExerciseVideoAnalyses");

            migrationBuilder.DropColumn(
                name: "ReviewNote",
                table: "DietPlans");

            migrationBuilder.DropColumn(
                name: "ReviewStatus",
                table: "DietPlans");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "DietPlans");

            migrationBuilder.DropColumn(
                name: "ReviewedByUserId",
                table: "DietPlans");
        }
    }
}
