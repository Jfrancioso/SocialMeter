using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialMeter.Migrations
{
    /// <inheritdoc />
    public partial class AddPostModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "HiddenAt",
                table: "SocialPosts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HiddenReason",
                table: "SocialPosts",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ModerationActions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PostId = table.Column<int>(type: "int", nullable: false),
                    MediaType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TmdbId = table.Column<int>(type: "int", nullable: false),
                    TitleName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Actor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModerationActions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModerationActions_At",
                table: "ModerationActions",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationActions_PostId",
                table: "ModerationActions",
                column: "PostId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModerationActions");

            migrationBuilder.DropColumn(
                name: "HiddenAt",
                table: "SocialPosts");

            migrationBuilder.DropColumn(
                name: "HiddenReason",
                table: "SocialPosts");
        }
    }
}
