using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialMeter.Migrations
{
    /// <inheritdoc />
    public partial class AddUsageDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UsageDays",
                columns: table => new
                {
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    LivePulls = table.Column<int>(type: "int", nullable: false),
                    ClaudeInputTokens = table.Column<long>(type: "bigint", nullable: false),
                    ClaudeOutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    XPostsRead = table.Column<int>(type: "int", nullable: false),
                    XUsersRead = table.Column<int>(type: "int", nullable: false),
                    EstimatedUsd = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageDays", x => x.Day);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UsageDays");
        }
    }
}
