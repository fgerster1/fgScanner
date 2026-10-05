using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FgScanner.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPageOrientationSettled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OrientationSettled",
                table: "Pages",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // A page already read (Yes = 2) or tried (Failed = 3) has had its orientation chance;
            // without this its next re-OCR would turn it again, the very thing the column stops.
            migrationBuilder.Sql("UPDATE Pages SET OrientationSettled = 1 WHERE OcrStatus IN (2, 3);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OrientationSettled",
                table: "Pages");
        }
    }
}
