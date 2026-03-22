using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Translarr.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHasBitmapSubtitlesOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasBitmapSubtitlesOnly",
                table: "subtitle_entries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasBitmapSubtitlesOnly",
                table: "subtitle_entries");
        }
    }
}
