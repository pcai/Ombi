using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ombi.Store.Migrations.OmbiSqlite
{
    /// <summary>
    /// Indexes on the keys request code looks up by. These columns are not
    /// foreign keys, so EF created no index for them by convention, and
    /// every duplicate-request check was a full table scan.
    /// SQLite only: the MySQL/Postgres snapshots pick these up when their
    /// next migration is generated from the shared OnModelCreating.
    /// </summary>
    public partial class AddLookupIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_MovieRequests_TheMovieDbId",
                table: "MovieRequests",
                column: "TheMovieDbId");

            migrationBuilder.CreateIndex(
                name: "IX_MovieRequests_ImdbId",
                table: "MovieRequests",
                column: "ImdbId");

            migrationBuilder.CreateIndex(
                name: "IX_TvRequests_TvDbId",
                table: "TvRequests",
                column: "TvDbId");

            migrationBuilder.CreateIndex(
                name: "IX_TvRequests_ExternalProviderId",
                table: "TvRequests",
                column: "ExternalProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_TvRequests_ImdbId",
                table: "TvRequests",
                column: "ImdbId");

            migrationBuilder.CreateIndex(
                name: "IX_AlbumRequests_ForeignAlbumId",
                table: "AlbumRequests",
                column: "ForeignAlbumId");

            migrationBuilder.CreateIndex(
                name: "IX_AlbumRequests_ForeignArtistId",
                table: "AlbumRequests",
                column: "ForeignArtistId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AlbumRequests_ForeignArtistId",
                table: "AlbumRequests");

            migrationBuilder.DropIndex(
                name: "IX_AlbumRequests_ForeignAlbumId",
                table: "AlbumRequests");

            migrationBuilder.DropIndex(
                name: "IX_TvRequests_ImdbId",
                table: "TvRequests");

            migrationBuilder.DropIndex(
                name: "IX_TvRequests_ExternalProviderId",
                table: "TvRequests");

            migrationBuilder.DropIndex(
                name: "IX_TvRequests_TvDbId",
                table: "TvRequests");

            migrationBuilder.DropIndex(
                name: "IX_MovieRequests_ImdbId",
                table: "MovieRequests");

            migrationBuilder.DropIndex(
                name: "IX_MovieRequests_TheMovieDbId",
                table: "MovieRequests");
        }
    }
}
