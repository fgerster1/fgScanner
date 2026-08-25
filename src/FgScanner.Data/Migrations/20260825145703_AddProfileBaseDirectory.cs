using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FgScanner.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileBaseDirectory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BaseDirectory",
                table: "Profiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BaseDirectory",
                table: "Profiles");
        }
    }
}
