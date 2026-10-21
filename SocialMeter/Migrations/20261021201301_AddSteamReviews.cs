using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialMeter.Migrations
{
    /// <inheritdoc />
    public partial class AddSteamReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EngagementMinutes",
                table: "SocialPosts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "VerdictUp",
                table: "SocialPosts",
                type: "bit",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "EngagementMinutes", "VerdictUp" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "EngagementMinutes", "VerdictUp" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "EngagementMinutes", "VerdictUp" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "EngagementMinutes", "VerdictUp" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "EngagementMinutes", "VerdictUp" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "EngagementMinutes", "VerdictUp" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "EngagementMinutes", "VerdictUp" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "EngagementMinutes", "VerdictUp" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 9,
                columns: new[] { "EngagementMinutes", "VerdictUp" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "EngagementMinutes", "VerdictUp" },
                values: new object[] { null, null });

            migrationBuilder.CreateIndex(
                name: "IX_SocialPosts_Platform",
                table: "SocialPosts",
                column: "Platform");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SocialPosts_Platform",
                table: "SocialPosts");

            migrationBuilder.DropColumn(
                name: "EngagementMinutes",
                table: "SocialPosts");

            migrationBuilder.DropColumn(
                name: "VerdictUp",
                table: "SocialPosts");
        }
    }
}
