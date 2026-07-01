using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class NcCharacterBuildLock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "nc_skills",
                table: "profile",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "nc_stats",
                table: "profile",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "stats_and_skills_locked",
                table: "profile",
                type: "INTEGER",
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
