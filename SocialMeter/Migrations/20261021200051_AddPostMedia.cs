using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialMeter.Migrations
{
    /// <inheritdoc />
    public partial class AddPostMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MediaJson",
                table: "SocialPosts",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Sensitive",
                table: "SocialPosts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "MediaJson", "Sensitive" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "MediaJson", "Sensitive" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "MediaJson", "Sensitive" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "MediaJson", "Sensitive" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "MediaJson", "Sensitive" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "MediaJson", "Sensitive" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "MediaJson", "Sensitive" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "MediaJson", "Sensitive" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 9,
                columns: new[] { "MediaJson", "Sensitive" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "MediaJson", "Sensitive" },
                values: new object[] { null, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MediaJson",
                table: "SocialPosts");

            migrationBuilder.DropColumn(
                name: "Sensitive",
                table: "SocialPosts");
        }
    }
}
