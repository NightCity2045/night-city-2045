using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddNCCharacterNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "nc_character_notes",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    owner_profile_id = table.Column<int>(type: "INTEGER", nullable: false),
                    target_profile_id = table.Column<int>(type: "INTEGER", nullable: false),
                    custom_name = table.Column<string>(type: "TEXT", nullable: false),
                    color_tag = table.Column<byte>(type: "INTEGER", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_nc_character_notes", x => x.id);
                    table.ForeignKey(
                        name: "FK_nc_character_notes_profile_owner_profile_id",
                        column: x => x.owner_profile_id,
                        principalTable: "profile",
                        principalColumn: "profile_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_nc_character_notes_profile_target_profile_id",
                        column: x => x.target_profile_id,
                        principalTable: "profile",
                        principalColumn: "profile_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_nc_character_notes_owner_profile_id_target_profile_id",
                table: "nc_character_notes",
                columns: new[] { "owner_profile_id", "target_profile_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_nc_character_notes_target_profile_id",
                table: "nc_character_notes",
                column: "target_profile_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "nc_character_notes");
        }
    }
}
