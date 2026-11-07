using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReadingQuality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Quality",
                table: "Readings",
                type: "text",
                nullable: false,
                defaultValue: "interval-energy");

            migrationBuilder.AddColumn<string>(
                name: "SourceRevision",
                table: "Readings",
                type: "text",
                nullable: false,
                defaultValue: "1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Quality",
                table: "Readings");

            migrationBuilder.DropColumn(
                name: "SourceRevision",
                table: "Readings");
        }
    }
}
