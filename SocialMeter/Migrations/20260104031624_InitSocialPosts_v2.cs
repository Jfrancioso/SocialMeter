using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SocialMeter.Migrations
{
    /// <inheritdoc />
    public partial class InitSocialPosts_v2 : Migration
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
                    MovieId = table.Column<int>(type: "int", nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Author = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Sentiment = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Upvotes = table.Column<int>(type: "int", nullable: false),
                    Comments = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SourceUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Subreddit = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialPosts", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "SocialPosts",
                columns: new[] { "Id", "Author", "Comments", "Content", "CreatedAt", "MovieId", "Platform", "Sentiment", "SourceUrl", "Subreddit", "Upvotes" },
                values: new object[,]
                {
                    { 1, "movie_fan_2024", 45, "Just watched Dune 2, absolutely incredible! The visuals were stunning and the story was so engaging.", new DateTime(2024, 3, 15, 10, 30, 0, 0, DateTimeKind.Unspecified), 693134, "reddit", "positive", "https://reddit.com/r/movies/example1", "movies", 234 },
                    { 2, "cinema_critic", 23, "Honestly disappointed. The pacing felt off and I found myself bored in the middle.", new DateTime(2024, 3, 14, 15, 20, 0, 0, DateTimeKind.Unspecified), 693134, "reddit", "negative", "https://reddit.com/r/TrueFilm/example2", "TrueFilm", 67 },
                    { 3, "scifi_lover", 89, "This is what cinema is all about! Villeneuve is a master. Best film of the year so far.", new DateTime(2024, 3, 16, 9, 15, 0, 0, DateTimeKind.Unspecified), 693134, "reddit", "positive", "https://reddit.com/r/movies/example3", "movies", 456 },
                    { 4, "casual_viewer", 5, "It was okay I guess. Not as mind-blowing as everyone says but decent.", new DateTime(2024, 3, 13, 20, 45, 0, 0, DateTimeKind.Unspecified), 693134, "reddit", "neutral", "https://reddit.com/r/moviesuggestions/example4", "moviesuggestions", 12 },
                    { 5, "dune_superfan", 134, "AMAZING! Everything I hoped for and more. The score, the acting, the cinematography - perfection!", new DateTime(2024, 3, 17, 14, 0, 0, 0, DateTimeKind.Unspecified), 693134, "reddit", "positive", "https://reddit.com/r/dune/example5", "dune", 789 },
                    { 6, "book_reader", 67, "As a book fan, I was really let down. They changed too much and it lost the essence.", new DateTime(2024, 3, 12, 11, 30, 0, 0, DateTimeKind.Unspecified), 693134, "reddit", "negative", "https://reddit.com/r/books/example6", "books", 45 },
                    { 7, "weekend_watcher", 18, "Solid movie. Good action, good visuals. Would watch again.", new DateTime(2024, 3, 15, 19, 20, 0, 0, DateTimeKind.Unspecified), 693134, "reddit", "positive", "https://reddit.com/r/movies/example7", "movies", 123 },
                    { 8, "film_student", 34, "The cinematography alone makes this worth watching. Roger Deakins would be proud.", new DateTime(2024, 3, 16, 8, 45, 0, 0, DateTimeKind.Unspecified), 693134, "reddit", "positive", "https://reddit.com/r/cinematography/example8", "cinematography", 234 },
                    { 9, "history_buff", 78, "Disappointing sequel. Doesn't live up to the original at all.", new DateTime(2024, 11, 22, 10, 0, 0, 0, DateTimeKind.Unspecified), 558449, "reddit", "negative", "https://reddit.com/r/movies/example9", "movies", 345 },
                    { 10, "action_fan", 23, "The action sequences were epic! Really enjoyed it.", new DateTime(2024, 11, 23, 14, 30, 0, 0, DateTimeKind.Unspecified), 558449, "reddit", "positive", "https://reddit.com/r/movies/example10", "movies", 156 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_SocialPosts_MovieId",
                table: "SocialPosts",
                column: "MovieId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SocialPosts");
        }
    }
}
