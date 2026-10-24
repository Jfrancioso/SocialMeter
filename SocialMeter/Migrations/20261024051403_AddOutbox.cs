using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialMeter.Migrations
{
    /// <inheritdoc />
    public partial class AddOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboxItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MediaType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TmdbId = table.Column<int>(type: "int", nullable: false),
                    TitleName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ForPostId = table.Column<int>(type: "int", nullable: true),
                    AgainstPostId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecidedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PostedText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PostedUri = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Error = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxItems_CreatedAt",
                table: "OutboxItems",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutboxItems");
        }
    }
}
