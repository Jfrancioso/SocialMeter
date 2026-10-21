using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialMeter.Migrations
{
    /// <inheritdoc />
    public partial class AddDeletionSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastCheckedAt",
                table: "SocialPosts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 1,
                column: "LastCheckedAt",
                value: null);

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 2,
                column: "LastCheckedAt",
                value: null);

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 3,
                column: "LastCheckedAt",
                value: null);

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 4,
                column: "LastCheckedAt",
                value: null);

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 5,
                column: "LastCheckedAt",
                value: null);

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 6,
                column: "LastCheckedAt",
                value: null);

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 7,
                column: "LastCheckedAt",
                value: null);

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 8,
                column: "LastCheckedAt",
                value: null);

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 9,
                column: "LastCheckedAt",
                value: null);

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 10,
                column: "LastCheckedAt",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastCheckedAt",
                table: "SocialPosts");
        }
    }
}
