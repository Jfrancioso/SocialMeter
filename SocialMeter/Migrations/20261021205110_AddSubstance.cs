using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialMeter.Migrations
{
    /// <inheritdoc />
    public partial class AddSubstance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Substance",
                table: "SocialPosts",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 1,
                column: "Substance",
                value: 0.46000000000000002);

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 2,
                column: "Substance",
                value: 0.35999999999999999);

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 3,
                column: "Substance",
                value: 0.33000000000000002);

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 4,
                column: "Substance",
                value: 0.28000000000000003);

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 5,
                column: "Substance",
                value: 0.55000000000000004);

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 6,
                column: "Substance",
                value: 0.29999999999999999);

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 7,
                column: "Substance",
                value: 0.34000000000000002);

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 8,
                column: "Substance",
                value: 0.34000000000000002);

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 9,
                column: "Substance",
                value: 0.41999999999999998);

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 10,
                column: "Substance",
                value: 0.23000000000000001);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Substance",
                table: "SocialPosts");
        }
    }
}
