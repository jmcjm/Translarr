using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Translarr.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameAlreadyHasToAlreadyHad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop the old index that references AlreadyHad (which doesn't exist yet)
            migrationBuilder.DropIndex(
                name: "IX_subtitle_entries_IsProcessed_IsWanted_AlreadyHas",
                table: "subtitle_entries");

            // Rename the column from AlreadyHas to AlreadyHad
            migrationBuilder.RenameColumn(
                name: "AlreadyHas",
                table: "subtitle_entries",
                newName: "AlreadyHad");

            // Create the index with the correct column name
            migrationBuilder.CreateIndex(
                name: "IX_subtitle_entries_IsProcessed_IsWanted_AlreadyHad",
                table: "subtitle_entries",
                columns: new[] { "IsProcessed", "IsWanted", "AlreadyHad" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop the new index
            migrationBuilder.DropIndex(
                name: "IX_subtitle_entries_IsProcessed_IsWanted_AlreadyHad",
                table: "subtitle_entries");

            // Rename the column back from AlreadyHad to AlreadyHas
            migrationBuilder.RenameColumn(
                name: "AlreadyHad",
                table: "subtitle_entries",
                newName: "AlreadyHas");

            // Recreate the old index with the original column name
            migrationBuilder.CreateIndex(
                name: "IX_subtitle_entries_IsProcessed_IsWanted_AlreadyHas",
                table: "subtitle_entries",
                columns: new[] { "IsProcessed", "IsWanted", "AlreadyHas" });
        }
    }
}
