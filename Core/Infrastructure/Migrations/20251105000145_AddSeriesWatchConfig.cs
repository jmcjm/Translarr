using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Translarr.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSeriesWatchConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "series_watch_configs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SeriesName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    SeasonName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    AutoWatch = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_series_watch_configs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_series_watch_configs_SeriesName",
                table: "series_watch_configs",
                column: "SeriesName");

            migrationBuilder.CreateIndex(
                name: "IX_series_watch_configs_SeriesName_SeasonName",
                table: "series_watch_configs",
                columns: new[] { "SeriesName", "SeasonName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "series_watch_configs");
        }
    }
}
