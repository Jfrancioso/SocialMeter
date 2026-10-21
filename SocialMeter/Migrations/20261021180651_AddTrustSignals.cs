using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialMeter.Migrations
{
    /// <inheritdoc />
    public partial class AddTrustSignals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FilteredPosts",
                table: "TitleSnapshots",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "AuthorCreatedAt",
                table: "SocialPosts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthorId",
                table: "SocialPosts",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AuthorReputation",
                table: "SocialPosts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "SocialPosts",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Excluded",
                table: "SocialPosts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ExclusionReason",
                table: "SocialPosts",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TrustWeight",
                table: "SocialPosts",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "AuthorCreatedAt", "AuthorId", "AuthorReputation", "ContentHash", "Excluded", "ExclusionReason", "TrustWeight" },
                values: new object[] { new DateTime(2017, 2, 7, 0, 0, 0, 0, DateTimeKind.Utc), "seed-user-1", 1510, "38048634510713d5be985382f0928a40", false, null, 1.0 });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "AuthorCreatedAt", "AuthorId", "AuthorReputation", "ContentHash", "Excluded", "ExclusionReason", "TrustWeight" },
                values: new object[] { new DateTime(2017, 3, 16, 0, 0, 0, 0, DateTimeKind.Utc), "seed-user-2", 1820, "915c17416e0f1628b2abd6491055620e", false, null, 1.0 });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "AuthorCreatedAt", "AuthorId", "AuthorReputation", "ContentHash", "Excluded", "ExclusionReason", "TrustWeight" },
                values: new object[] { new DateTime(2017, 4, 22, 0, 0, 0, 0, DateTimeKind.Utc), "seed-user-3", 2130, "7c522467cc0055586b33b761c73335f8", false, null, 1.0 });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "AuthorCreatedAt", "AuthorId", "AuthorReputation", "ContentHash", "Excluded", "ExclusionReason", "TrustWeight" },
                values: new object[] { new DateTime(2017, 5, 29, 0, 0, 0, 0, DateTimeKind.Utc), "seed-user-4", 2440, "4e581b07d0548a991ab539322809e7be", false, null, 1.0 });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "AuthorCreatedAt", "AuthorId", "AuthorReputation", "ContentHash", "Excluded", "ExclusionReason", "TrustWeight" },
                values: new object[] { new DateTime(2017, 7, 5, 0, 0, 0, 0, DateTimeKind.Utc), "seed-user-5", 2750, "d49d9c8629e8071adc409a25f33ee7b0", false, null, 1.0 });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "AuthorCreatedAt", "AuthorId", "AuthorReputation", "ContentHash", "Excluded", "ExclusionReason", "TrustWeight" },
                values: new object[] { new DateTime(2017, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), "seed-user-6", 3060, "c514606bf42c14f363eadaf42dfde6a9", false, null, 1.0 });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "AuthorCreatedAt", "AuthorId", "AuthorReputation", "ContentHash", "Excluded", "ExclusionReason", "TrustWeight" },
                values: new object[] { new DateTime(2017, 9, 17, 0, 0, 0, 0, DateTimeKind.Utc), "seed-user-7", 3370, "d58f149fe0e43eb189531862f4519d5a", false, null, 1.0 });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "AuthorCreatedAt", "AuthorId", "AuthorReputation", "ContentHash", "Excluded", "ExclusionReason", "TrustWeight" },
                values: new object[] { new DateTime(2017, 10, 24, 0, 0, 0, 0, DateTimeKind.Utc), "seed-user-8", 3680, "a209b85d91c0fc6039968495b8517410", false, null, 1.0 });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 9,
                columns: new[] { "AuthorCreatedAt", "AuthorId", "AuthorReputation", "ContentHash", "Excluded", "ExclusionReason", "TrustWeight" },
                values: new object[] { new DateTime(2017, 11, 30, 0, 0, 0, 0, DateTimeKind.Utc), "seed-user-9", 3990, "09f2d78bee50323a4bb29f4cb00a33dc", false, null, 1.0 });

            migrationBuilder.UpdateData(
                table: "SocialPosts",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "AuthorCreatedAt", "AuthorId", "AuthorReputation", "ContentHash", "Excluded", "ExclusionReason", "TrustWeight" },
                values: new object[] { new DateTime(2018, 1, 6, 0, 0, 0, 0, DateTimeKind.Utc), "seed-user-10", 4300, "5d731cbdefb294db506a19e8a5dbfddf", false, null, 1.0 });

            migrationBuilder.CreateIndex(
                name: "IX_SocialPosts_ContentHash",
                table: "SocialPosts",
                column: "ContentHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SocialPosts_ContentHash",
                table: "SocialPosts");

            migrationBuilder.DropColumn(
                name: "FilteredPosts",
                table: "TitleSnapshots");

            migrationBuilder.DropColumn(
                name: "AuthorCreatedAt",
                table: "SocialPosts");

            migrationBuilder.DropColumn(
                name: "AuthorId",
                table: "SocialPosts");

            migrationBuilder.DropColumn(
                name: "AuthorReputation",
                table: "SocialPosts");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "SocialPosts");

            migrationBuilder.DropColumn(
                name: "Excluded",
                table: "SocialPosts");

            migrationBuilder.DropColumn(
                name: "ExclusionReason",
                table: "SocialPosts");

            migrationBuilder.DropColumn(
                name: "TrustWeight",
                table: "SocialPosts");
        }
    }
}
