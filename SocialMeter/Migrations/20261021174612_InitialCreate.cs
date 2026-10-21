using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SocialMeter.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SocialPosts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TmdbId = table.Column<int>(type: "int", nullable: false),
                    MediaType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Author = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Sentiment = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    SentimentScore = table.Column<double>(type: "float", nullable: false),
                    Upvotes = table.Column<int>(type: "int", nullable: false),
                    Comments = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SourceUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Community = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialPosts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TitleSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TmdbId = table.Column<int>(type: "int", nullable: false),
                    MediaType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Confidence = table.Column<double>(type: "float", nullable: false),
                    TotalPosts = table.Column<int>(type: "int", nullable: false),
                    Positive = table.Column<int>(type: "int", nullable: false),
                    Neutral = table.Column<int>(type: "int", nullable: false),
                    Negative = table.Column<int>(type: "int", nullable: false),
                    Momentum = table.Column<int>(type: "int", nullable: true),
                    LastRefreshedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TitleSnapshots", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "SocialPosts",
                columns: new[] { "Id", "Author", "Comments", "Community", "Content", "CreatedAt", "ExternalId", "FetchedAt", "MediaType", "Platform", "Sentiment", "SentimentScore", "SourceUrl", "TmdbId", "Upvotes" },
                values: new object[,]
                {
                    { 1, "movie_fan_2024", 45, "movies", "Just watched Dune 2, absolutely incredible! The visuals were stunning and the story was so engaging.", new DateTime(2024, 3, 15, 10, 30, 0, 0, DateTimeKind.Utc), "seed-1", new DateTime(2024, 3, 18, 0, 0, 0, 0, DateTimeKind.Utc), "Movie", "reddit", "Positive", 0.84999999999999998, "https://www.reddit.com/r/movies/", 693134, 234 },
                    { 2, "cinema_critic", 23, "TrueFilm", "Honestly disappointed. The pacing felt off and I found myself bored in the middle.", new DateTime(2024, 3, 14, 15, 20, 0, 0, DateTimeKind.Utc), "seed-2", new DateTime(2024, 3, 18, 0, 0, 0, 0, DateTimeKind.Utc), "Movie", "reddit", "Negative", -0.69999999999999996, "https://www.reddit.com/r/TrueFilm/", 693134, 67 },
                    { 3, "scifi_lover", 89, "movies", "This is what cinema is all about! Villeneuve is a master. Best film of the year so far.", new DateTime(2024, 3, 16, 9, 15, 0, 0, DateTimeKind.Utc), "seed-3", new DateTime(2024, 3, 18, 0, 0, 0, 0, DateTimeKind.Utc), "Movie", "reddit", "Positive", 0.69999999999999996, "https://www.reddit.com/r/movies/", 693134, 456 },
                    { 4, "casual_viewer", 5, "moviesuggestions", "It was okay I guess. Not as mind-blowing as everyone says but decent.", new DateTime(2024, 3, 13, 20, 45, 0, 0, DateTimeKind.Utc), "seed-4", new DateTime(2024, 3, 18, 0, 0, 0, 0, DateTimeKind.Utc), "Movie", "reddit", "Neutral", 0.050000000000000003, "https://www.reddit.com/r/moviesuggestions/", 693134, 12 },
                    { 5, "dune_superfan", 134, "dune", "AMAZING! Everything I hoped for and more. The score, the acting, the cinematography - perfection!", new DateTime(2024, 3, 17, 14, 0, 0, 0, DateTimeKind.Utc), "seed-5", new DateTime(2024, 3, 18, 0, 0, 0, 0, DateTimeKind.Utc), "Movie", "reddit", "Positive", 0.90000000000000002, "https://www.reddit.com/r/dune/", 693134, 789 },
                    { 6, "book_reader", 67, "books", "As a book fan, I was really let down. They changed too much and it lost the essence.", new DateTime(2024, 3, 12, 11, 30, 0, 0, DateTimeKind.Utc), "seed-6", new DateTime(2024, 3, 18, 0, 0, 0, 0, DateTimeKind.Utc), "Movie", "reddit", "Negative", -0.59999999999999998, "https://www.reddit.com/r/books/", 693134, 45 },
                    { 7, "weekend_watcher", 18, "movies", "Solid movie. Good action, good visuals. Would watch again.", new DateTime(2024, 3, 15, 19, 20, 0, 0, DateTimeKind.Utc), "seed-7", new DateTime(2024, 3, 18, 0, 0, 0, 0, DateTimeKind.Utc), "Movie", "reddit", "Positive", 0.59999999999999998, "https://www.reddit.com/r/movies/", 693134, 123 },
                    { 8, "film_student", 34, "cinematography", "The cinematography alone makes this worth watching. Roger Deakins would be proud.", new DateTime(2024, 3, 16, 8, 45, 0, 0, DateTimeKind.Utc), "seed-8", new DateTime(2024, 3, 18, 0, 0, 0, 0, DateTimeKind.Utc), "Movie", "reddit", "Positive", 0.45000000000000001, "https://www.reddit.com/r/cinematography/", 693134, 234 },
                    { 9, "history_buff", 78, "movies", "Disappointing sequel. Doesn't live up to the original at all.", new DateTime(2024, 11, 22, 10, 0, 0, 0, DateTimeKind.Utc), "seed-9", new DateTime(2024, 3, 18, 0, 0, 0, 0, DateTimeKind.Utc), "Movie", "reddit", "Negative", -0.69999999999999996, "https://www.reddit.com/r/movies/", 558449, 345 },
                    { 10, "action_fan", 23, "movies", "The action sequences were epic! Really enjoyed it.", new DateTime(2024, 11, 23, 14, 30, 0, 0, DateTimeKind.Utc), "seed-10", new DateTime(2024, 3, 18, 0, 0, 0, 0, DateTimeKind.Utc), "Movie", "reddit", "Positive", 0.75, "https://www.reddit.com/r/movies/", 558449, 156 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_SocialPosts_Title",
                table: "SocialPosts",
                columns: new[] { "TmdbId", "MediaType" });

            migrationBuilder.CreateIndex(
                name: "UX_SocialPosts_Platform_ExternalId",
                table: "SocialPosts",
                columns: new[] { "Platform", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_TitleSnapshots_Title",
                table: "TitleSnapshots",
                columns: new[] { "TmdbId", "MediaType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SocialPosts");

            migrationBuilder.DropTable(
                name: "TitleSnapshots");
        }
    }
}
