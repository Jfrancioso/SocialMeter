using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialMeter.Migrations
{
    /// <inheritdoc />
    public partial class AddScoreHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Momentum",
                table: "TitleSnapshots");

            migrationBuilder.AddColumn<double>(
                name: "EffectivePosts",
                table: "TitleSnapshots",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "Margin",
                table: "TitleSnapshots",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RawScore",
                table: "TitleSnapshots",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ScoreHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TmdbId = table.Column<int>(type: "int", nullable: false),
                    MediaType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    RawScore = table.Column<int>(type: "int", nullable: false),
                    EffectivePosts = table.Column<double>(type: "float", nullable: false),
                    Margin = table.Column<int>(type: "int", nullable: false),
                    TotalPosts = table.Column<int>(type: "int", nullable: false),
                    Reconstructed = table.Column<bool>(type: "bit", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoreHistory", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UX_ScoreHistory_Title_Day",
                table: "ScoreHistory",
                columns: new[] { "TmdbId", "MediaType", "Day" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScoreHistory");

            migrationBuilder.DropColumn(
                name: "EffectivePosts",
                table: "TitleSnapshots");

            migrationBuilder.DropColumn(
                name: "Margin",
                table: "TitleSnapshots");

            migrationBuilder.DropColumn(
                name: "RawScore",
                table: "TitleSnapshots");

            migrationBuilder.AddColumn<int>(
                name: "Momentum",
                table: "TitleSnapshots",
                type: "int",
                nullable: true);
        }
    }
}
