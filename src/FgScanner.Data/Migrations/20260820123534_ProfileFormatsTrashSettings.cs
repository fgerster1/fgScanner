using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FgScanner.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProfileFormatsTrashSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CsvDelimiter",
                table: "Profiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "ExportCsv",
                table: "Profiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ExportJson",
                table: "Profiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ExportXlsx",
                table: "Profiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ExportXml",
                table: "Profiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "TrashItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OriginalGroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GroupName = table.Column<string>(type: "TEXT", nullable: false),
                    GroupDirectoryPath = table.Column<string>(type: "TEXT", nullable: false),
                    DocumentSequence = table.Column<int>(type: "INTEGER", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    FilesJson = table.Column<string>(type: "TEXT", nullable: false),
                    TrashFolderPath = table.Column<string>(type: "TEXT", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrashItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrashItems_DeletedUtc",
                table: "TrashItems",
                column: "DeletedUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "TrashItems");

            migrationBuilder.DropColumn(
                name: "CsvDelimiter",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "ExportCsv",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "ExportJson",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "ExportXlsx",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "ExportXml",
                table: "Profiles");
        }
    }
}
