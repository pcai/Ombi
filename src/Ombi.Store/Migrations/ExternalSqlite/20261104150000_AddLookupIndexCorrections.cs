using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ombi.Store.Migrations.ExternalSqlite
{
    /// <summary>
    /// Corrects the lookup indexes added in 20261104000000_AddLookupIndexes:
    /// drops the Type index (SQLite's planner picked it for provider-id
    /// lookups and walked half the table), the three indexes that
    /// duplicated existing alternate-key unique constraints (Key,
    /// EmbyId, JellyfinId), and the PlexEpisode composite, which the
    /// once-per-sync episode key load no longer queries and which made
    /// the model disagree with the snapshot (EF treats it as covering
    /// the foreign key, so the model no longer wanted the single-column
    /// foreign-key index). Adds the two indexes the review found still
    /// missing: JellyfinEpisode by JellyfinId, and PlexServerContent by
    /// (Title, ReleaseYear, Type) for PlexContentSync's title matching.
    /// SQLite only: the MySQL/Postgres snapshots pick these up when
    /// their next migration is generated from the shared OnModelCreating.
    /// </summary>
    public partial class AddLookupIndexCorrections : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlexServerContent_Key",
                table: "PlexServerContent");

            migrationBuilder.DropIndex(
                name: "IX_PlexServerContent_Type",
                table: "PlexServerContent");

            migrationBuilder.DropIndex(
                name: "IX_PlexEpisode_GrandparentKey_Key",
                table: "PlexEpisode");

            migrationBuilder.DropIndex(
                name: "IX_EmbyContent_EmbyId",
                table: "EmbyContent");

            migrationBuilder.DropIndex(
                name: "IX_JellyfinContent_JellyfinId",
                table: "JellyfinContent");

            migrationBuilder.CreateIndex(
                name: "IX_PlexServerContent_Title_ReleaseYear_Type",
                table: "PlexServerContent",
                columns: new[] { "Title", "ReleaseYear", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_JellyfinEpisode_JellyfinId",
                table: "JellyfinEpisode",
                column: "JellyfinId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JellyfinEpisode_JellyfinId",
                table: "JellyfinEpisode");

            migrationBuilder.DropIndex(
                name: "IX_PlexServerContent_Title_ReleaseYear_Type",
                table: "PlexServerContent");

            migrationBuilder.CreateIndex(
                name: "IX_JellyfinContent_JellyfinId",
                table: "JellyfinContent",
                column: "JellyfinId");

            migrationBuilder.CreateIndex(
                name: "IX_EmbyContent_EmbyId",
                table: "EmbyContent",
                column: "EmbyId");

            migrationBuilder.CreateIndex(
                name: "IX_PlexEpisode_GrandparentKey_Key",
                table: "PlexEpisode",
                columns: new[] { "GrandparentKey", "Key" });

            migrationBuilder.CreateIndex(
                name: "IX_PlexServerContent_Type",
                table: "PlexServerContent",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_PlexServerContent_Key",
                table: "PlexServerContent",
                column: "Key");
        }
    }
}
