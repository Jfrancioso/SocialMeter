using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialMeter.Migrations
{
    /// <inheritdoc />
    public partial class AddCriticsScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CriticsCheckedAt",
                table: "TitleSnapshots",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CriticsScore",
                table: "TitleSnapshots",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CriticsCheckedAt",
                table: "TitleSnapshots");

            migrationBuilder.DropColumn(
                name: "CriticsScore",
                table: "TitleSnapshots");
        }
    }
}
