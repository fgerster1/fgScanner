using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FgScanner.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFieldLengthAndMemo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxLength",
                table: "FieldDefinitions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Memo",
                table: "FieldDefinitions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxLength",
                table: "FieldDefinitions");

            migrationBuilder.DropColumn(
                name: "Memo",
                table: "FieldDefinitions");
        }
    }
}
