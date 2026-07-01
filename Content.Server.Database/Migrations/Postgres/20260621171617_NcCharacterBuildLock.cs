using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class NcCharacterBuildLock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<JsonDocument>(
                name: "nc_skills",
                table: "profile",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<JsonDocument>(
                name: "nc_stats",
                table: "profile",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "stats_and_skills_locked",
                table: "profile",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "nc_skills",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "nc_stats",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "stats_and_skills_locked",
                table: "profile");
        }
    }
}
