using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SystematiaBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SourcesCvFollowUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AlsoOn",
                table: "Vacancies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DedupKey",
                table: "Vacancies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExperienceMonths",
                table: "Vacancies",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FilterReason",
                table: "Vacancies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FollowUpDoneAt",
                table: "Vacancies",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FollowUpDraft",
                table: "Vacancies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FollowUpRemindedAt",
                table: "Vacancies",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PostedAt",
                table: "Vacancies",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CvJson",
                table: "UserSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastFeedSyncAt",
                table: "UserSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CachedTokens",
                table: "LlmUsages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Model",
                table: "LlmUsages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReasoningTokens",
                table: "LlmUsages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Vacancies_DedupKey",
                table: "Vacancies",
                column: "DedupKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vacancies_DedupKey",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "AlsoOn",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "DedupKey",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "ExperienceMonths",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "FilterReason",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "FollowUpDoneAt",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "FollowUpDraft",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "FollowUpRemindedAt",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "PostedAt",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "CvJson",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "LastFeedSyncAt",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "CachedTokens",
                table: "LlmUsages");

            migrationBuilder.DropColumn(
                name: "Model",
                table: "LlmUsages");

            migrationBuilder.DropColumn(
                name: "ReasoningTokens",
                table: "LlmUsages");
        }
    }
}
