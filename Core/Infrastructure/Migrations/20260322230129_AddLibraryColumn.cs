using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Translarr.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Library",
                table: "subtitle_entries",
                type: "TEXT",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_subtitle_entries_Library_Series_Season",
                table: "subtitle_entries",
                columns: new[] { "Library", "Series", "Season" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_subtitle_entries_Library_Series_Season",
                table: "subtitle_entries");

            migrationBuilder.DropColumn(
                name: "Library",
                table: "subtitle_entries");
        }
    }
}
