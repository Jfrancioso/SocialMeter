using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialMeter.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalyzer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Analyzer",
                table: "SocialPosts",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AnalyzerNote",
                table: "SocialPosts",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Analyzer", "AnalyzerNote" },
                values: new object[] { "seed", null });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Analyzer", "AnalyzerNote" },
                values: new object[] { "seed", null });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "Analyzer", "AnalyzerNote" },
                values: new object[] { "seed", null });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "Analyzer", "AnalyzerNote" },
                values: new object[] { "seed", null });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "Analyzer", "AnalyzerNote" },
                values: new object[] { "seed", null });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "Analyzer", "AnalyzerNote" },
                values: new object[] { "seed", null });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "Analyzer", "AnalyzerNote" },
                values: new object[] { "seed", null });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "Analyzer", "AnalyzerNote" },
                values: new object[] { "seed", null });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 9,
                columns: new[] { "Analyzer", "AnalyzerNote" },
                values: new object[] { "seed", null });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "Analyzer", "AnalyzerNote" },
                values: new object[] { "seed", null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Analyzer",
                table: "SocialPosts");

            migrationBuilder.DropColumn(
                name: "AnalyzerNote",
                table: "SocialPosts");
        }
    }
}
