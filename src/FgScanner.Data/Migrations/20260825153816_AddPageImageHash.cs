using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FgScanner.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPageImageHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageHash",
                table: "Pages",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageHash",
                table: "Pages");
        }
    }
}
