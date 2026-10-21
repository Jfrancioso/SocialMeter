using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SocialMeter.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSeedPosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 8);

            migrationBuilder.DeleteData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 9);

            migrationBuilder.DeleteData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 10);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "SocialPosts",
                columns: new[] { "Id", "Analyzer", "AnalyzerNote", "Author", "AuthorCreatedAt", "AuthorId", "AuthorReputation", "Comments", "Community", "Content", "ContentHash", "CreatedAt", "EngagementMinutes", "Excluded", "ExclusionReason", "ExternalId", "FetchedAt", "LastCheckedAt", "MediaJson", "MediaType", "Platform", "Sensitive", "Sentiment", "SentimentScore", "SourceUrl", "Substance", "TmdbId", "TrustWeight", "Upvotes", "VerdictUp" },
                values: new object[,]
                {
                    { 1, "seed", null, "movie_fan_2024", new DateTime(2017, 2, 7, 0, 0, 0, 0, DateTimeKind.Utc), "seed-user-1", 1510, 45, "movies", "Just watched Dune 2, absolutely incredible! The visuals were stunning and the story was so engaging.", "38048634510713d5be985382f0928a40", new DateTime(2024, 3, 15, 10, 30, 0, 0, DateTimeKind.Utc), null, false, null, "seed-1", new DateTime(2024, 3, 18, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Movie", "reddit", false, "Positive", 0.84999999999999998, "https://www.reddit.com/r/movies/", 0.46000000000000002, 693134, 1.0, 234, null },
                    { 2, "seed", null, "cinema_critic", new DateTime(2017, 3, 16, 0, 0, 0, 0, DateTimeKind.Utc), "seed-user-2", 1820, 23, "TrueFilm", "Honestly disappointed. The pacing felt off and I found myself bored in the middle.", "915c17416e0f1628b2abd6491055620e", new DateTime(2024, 3, 14, 15, 20, 0, 0, DateTimeKind.Utc), null, false, null, "seed-2", new DateTime(2024, 3, 18, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Movie", "reddit", false, "Negative", -0.69999999999999996, "https://www.reddit.com/r/TrueFilm/", 0.35999999999999999, 693134, 1.0, 67, null },
                    { 3, "seed", null, "scifi_lover", new DateTime(2017, 4, 22, 0, 0, 0, 0, DateTimeKind.Utc), "seed-user-3", 2130, 89, "movies", "This is what cinema is all about! Villeneuve is a master. Best film of the year so far.", "7c522467cc0055586b33b761c73335f8", new DateTime(2024, 3, 16, 9, 15, 0, 0, DateTimeKind.Utc), null, false, null, "seed-3", new DateTime(2024, 3, 18, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Movie", "reddit", false, "Positive", 0.69999999999999996, "https://www.reddit.com/r/movies/", 0.33000000000000002, 693134, 1.0, 456, null },
                    { 4, "seed", null, "casual_viewer", new DateTime(2017, 5, 29, 0, 0, 0, 0, DateTimeKind.Utc), "seed-user-4", 2440, 5, "moviesuggestions", "It was okay I guess. Not as mind-blowing as everyone says but decent.", "4e581b07d0548a991ab539322809e7be", new DateTime(2024, 3, 13, 20, 45, 0, 0, DateTimeKind.Utc), null, false, null, "seed-4", new DateTime(2024, 3, 18, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Movie", "reddit", false, "Neutral", 0.050000000000000003, "https://www.reddit.com/r/moviesuggestions/", 0.28000000000000003, 693134, 1.0, 12, null },
                    { 5, "seed", null, "dune_superfan", new DateTime(2017, 7, 5, 0, 0, 0, 0, DateTimeKind.Utc), "seed-user-5", 2750, 134, "dune", "AMAZING! Everything I hoped for and more. The score, the acting, the cinematography - perfection!", "d49d9c8629e8071adc409a25f33ee7b0", new DateTime(2024, 3, 17, 14, 0, 0, 0, DateTimeKind.Utc), null, false, null, "seed-5", new DateTime(2024, 3, 18, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Movie", "reddit", false, "Positive", 0.90000000000000002, "https://www.reddit.com/r/dune/", 0.55000000000000004, 693134, 1.0, 789, null },
                    { 6, "seed", null, "book_reader", new DateTime(2017, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), "seed-user-6", 3060, 67, "books", "As a book fan, I was really let down. They changed too much and it lost the essence.", "c514606bf42c14f363eadaf42dfde6a9", new DateTime(2024, 3, 12, 11, 30, 0, 0, DateTimeKind.Utc), null, false, null, "seed-6", new DateTime(2024, 3, 18, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Movie", "reddit", false, "Negative", -0.59999999999999998, "https://www.reddit.com/r/books/", 0.29999999999999999, 693134, 1.0, 45, null },
                    { 7, "seed", null, "weekend_watcher", new DateTime(2017, 9, 17, 0, 0, 0, 0, DateTimeKind.Utc), "seed-user-7", 3370, 18, "movies", "Solid movie. Good action, good visuals. Would watch again.", "d58f149fe0e43eb189531862f4519d5a", new DateTime(2024, 3, 15, 19, 20, 0, 0, DateTimeKind.Utc), null, false, null, "seed-7", new DateTime(2024, 3, 18, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Movie", "reddit", false, "Positive", 0.59999999999999998, "https://www.reddit.com/r/movies/", 0.34000000000000002, 693134, 1.0, 123, null },
                    { 8, "seed", null, "film_student", new DateTime(2017, 10, 24, 0, 0, 0, 0, DateTimeKind.Utc), "seed-user-8", 3680, 34, "cinematography", "The cinematography alone makes this worth watching. Roger Deakins would be proud.", "a209b85d91c0fc6039968495b8517410", new DateTime(2024, 3, 16, 8, 45, 0, 0, DateTimeKind.Utc), null, false, null, "seed-8", new DateTime(2024, 3, 18, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Movie", "reddit", false, "Positive", 0.45000000000000001, "https://www.reddit.com/r/cinematography/", 0.34000000000000002, 693134, 1.0, 234, null },
                    { 9, "seed", null, "history_buff", new DateTime(2017, 11, 30, 0, 0, 0, 0, DateTimeKind.Utc), "seed-user-9", 3990, 78, "movies", "Disappointing sequel. Doesn't live up to the original at all.", "09f2d78bee50323a4bb29f4cb00a33dc", new DateTime(2024, 11, 22, 10, 0, 0, 0, DateTimeKind.Utc), null, false, null, "seed-9", new DateTime(2024, 3, 18, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Movie", "reddit", false, "Negative", -0.69999999999999996, "https://www.reddit.com/r/movies/", 0.41999999999999998, 558449, 1.0, 345, null },
                    { 10, "seed", null, "action_fan", new DateTime(2018, 1, 6, 0, 0, 0, 0, DateTimeKind.Utc), "seed-user-10", 4300, 23, "movies", "The action sequences were epic! Really enjoyed it.", "5d731cbdefb294db506a19e8a5dbfddf", new DateTime(2024, 11, 23, 14, 30, 0, 0, DateTimeKind.Utc), null, false, null, "seed-10", new DateTime(2024, 3, 18, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Movie", "reddit", false, "Positive", 0.75, "https://www.reddit.com/r/movies/", 0.23000000000000001, 558449, 1.0, 156, null }
                });
        }
    }
}
