using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialMeter.Migrations
{
    /// <inheritdoc />
    public partial class AddRelabel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LabelFixedAt",
                table: "SocialPosts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Action",
                table: "ModerationActions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(10)",
                oldMaxLength: 10);

            migrationBuilder.AddColumn<string>(
                name: "FromLabel",
                table: "ModerationActions",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "FromScore",
                table: "ModerationActions",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "ModerationActions",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ToLabel",
                table: "ModerationActions",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LabelFixedAt",
                table: "SocialPosts");

            migrationBuilder.DropColumn(
                name: "FromLabel",
                table: "ModerationActions");

            migrationBuilder.DropColumn(
                name: "FromScore",
                table: "ModerationActions");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "ModerationActions");

            migrationBuilder.DropColumn(
                name: "ToLabel",
                table: "ModerationActions");

            migrationBuilder.AlterColumn<string>(
                name: "Action",
                table: "ModerationActions",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);
        }
    }
}
