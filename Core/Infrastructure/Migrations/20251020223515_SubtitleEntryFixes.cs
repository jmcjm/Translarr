using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Translarr.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SubtitleEntryFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ForceProcess",
                table: "subtitle_entries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_subtitle_entries_ForceProcess",
                table: "subtitle_entries",
                column: "ForceProcess");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_subtitle_entries_ForceProcess",
                table: "subtitle_entries");

            migrationBuilder.DropColumn(
                name: "ForceProcess",
                table: "subtitle_entries");
        }
    }
}
