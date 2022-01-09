using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

namespace memeweaver.Migrations
{
    public partial class initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Playables",
                columns: table => new
                {
                    PlayableId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Location = table.Column<string>(type: "varchar(767)", nullable: false),
                    PlayCount = table.Column<int>(type: "int", nullable: false),
                    StoragePath = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Playables", x => x.PlayableId);
                });

            migrationBuilder.CreateTable(
                name: "ServerSettings",
                columns: table => new
                {
                    ServerSettingId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    GuildId = table.Column<decimal>(type: "DECIMAL(20)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerSettings", x => x.ServerSettingId);
                });

            migrationBuilder.CreateTable(
                name: "PlayableServerSetting",
                columns: table => new
                {
                    PlayablesPlayableId = table.Column<long>(type: "bigint", nullable: false),
                    ServerSettingsServerSettingId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayableServerSetting", x => new { x.PlayablesPlayableId, x.ServerSettingsServerSettingId });
                    table.ForeignKey(
                        name: "FK_PlayableServerSetting_Playables_PlayablesPlayableId",
                        column: x => x.PlayablesPlayableId,
                        principalTable: "Playables",
                        principalColumn: "PlayableId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayableServerSetting_ServerSettings_ServerSettingsServerSet~",
                        column: x => x.ServerSettingsServerSettingId,
                        principalTable: "ServerSettings",
                        principalColumn: "ServerSettingId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Playables_Location",
                table: "Playables",
                column: "Location",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayableServerSetting_ServerSettingsServerSettingId",
                table: "PlayableServerSetting",
                column: "ServerSettingsServerSettingId");

            migrationBuilder.CreateIndex(
                name: "IX_ServerSettings_GuildId",
                table: "ServerSettings",
                column: "GuildId",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayableServerSetting");

            migrationBuilder.DropTable(
                name: "Playables");

            migrationBuilder.DropTable(
                name: "ServerSettings");
        }
    }
}
