using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ombi.Store.Migrations.ExternalSqlite
{
    /// <summary>
    /// Indexes on the keys sync and availability code looks up by. These
    /// columns are not foreign keys, so EF created no index for them by
    /// convention, and every point lookup on them was a full table scan
    /// over a table that grows with library size.
    /// SQLite only: the MySQL/Postgres snapshots pick these up when their
    /// next migration is generated from the shared OnModelCreating.
    /// </summary>
    public partial class AddLookupIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PlexServerContent_Key",
                table: "PlexServerContent",
                column: "Key");

            migrationBuilder.CreateIndex(
                name: "IX_PlexServerContent_ImdbId",
                table: "PlexServerContent",
                column: "ImdbId");

            migrationBuilder.CreateIndex(
                name: "IX_PlexServerContent_TheMovieDbId",
                table: "PlexServerContent",
                column: "TheMovieDbId");

            migrationBuilder.CreateIndex(
                name: "IX_PlexServerContent_TvDbId",
                table: "PlexServerContent",
                column: "TvDbId");

            migrationBuilder.CreateIndex(
                name: "IX_PlexServerContent_Type",
                table: "PlexServerContent",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_PlexEpisode_GrandparentKey_Key",
                table: "PlexEpisode",
                columns: new[] { "GrandparentKey", "Key" });

            migrationBuilder.CreateIndex(
                name: "IX_EmbyContent_EmbyId",
                table: "EmbyContent",
                column: "EmbyId");

            migrationBuilder.CreateIndex(
                name: "IX_EmbyContent_ImdbId",
                table: "EmbyContent",
                column: "ImdbId");

            migrationBuilder.CreateIndex(
                name: "IX_EmbyContent_TheMovieDbId",
                table: "EmbyContent",
                column: "TheMovieDbId");

            migrationBuilder.CreateIndex(
                name: "IX_EmbyContent_TvDbId",
                table: "EmbyContent",
                column: "TvDbId");

            migrationBuilder.CreateIndex(
                name: "IX_JellyfinContent_JellyfinId",
                table: "JellyfinContent",
                column: "JellyfinId");

            migrationBuilder.CreateIndex(
                name: "IX_JellyfinContent_ImdbId",
                table: "JellyfinContent",
                column: "ImdbId");

            migrationBuilder.CreateIndex(
                name: "IX_JellyfinContent_TheMovieDbId",
                table: "JellyfinContent",
                column: "TheMovieDbId");

            migrationBuilder.CreateIndex(
                name: "IX_JellyfinContent_TvDbId",
                table: "JellyfinContent",
                column: "TvDbId");

            migrationBuilder.CreateIndex(
                name: "IX_RadarrCache_TheMovieDbId",
                table: "RadarrCache",
                column: "TheMovieDbId");

            migrationBuilder.CreateIndex(
                name: "IX_SonarrCache_TvDbId",
                table: "SonarrCache",
                column: "TvDbId");

            migrationBuilder.CreateIndex(
                name: "IX_SonarrCache_TheMovieDbId",
                table: "SonarrCache",
                column: "TheMovieDbId");

            migrationBuilder.CreateIndex(
                name: "IX_SonarrEpisodeCache_TvDbId_SeasonNumber_EpisodeNumber",
                table: "SonarrEpisodeCache",
                columns: new[] { "TvDbId", "SeasonNumber", "EpisodeNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_LidarrArtistCache_ArtistId",
                table: "LidarrArtistCache",
                column: "ArtistId");

            migrationBuilder.CreateIndex(
                name: "IX_LidarrArtistCache_ForeignArtistId",
                table: "LidarrArtistCache",
                column: "ForeignArtistId");

            migrationBuilder.CreateIndex(
                name: "IX_LidarrAlbumCache_ArtistId",
                table: "LidarrAlbumCache",
                column: "ArtistId");

            migrationBuilder.CreateIndex(
                name: "IX_LidarrAlbumCache_ForeignAlbumId",
                table: "LidarrAlbumCache",
                column: "ForeignAlbumId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPlayedMovie_TheMovieDbId",
                table: "UserPlayedMovie",
                column: "TheMovieDbId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPlayedEpisode_TheMovieDbId",
                table: "UserPlayedEpisode",
                column: "TheMovieDbId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserPlayedEpisode_TheMovieDbId",
                table: "UserPlayedEpisode");

            migrationBuilder.DropIndex(
                name: "IX_UserPlayedMovie_TheMovieDbId",
                table: "UserPlayedMovie");

            migrationBuilder.DropIndex(
                name: "IX_LidarrAlbumCache_ForeignAlbumId",
                table: "LidarrAlbumCache");

            migrationBuilder.DropIndex(
                name: "IX_LidarrAlbumCache_ArtistId",
                table: "LidarrAlbumCache");

            migrationBuilder.DropIndex(
                name: "IX_LidarrArtistCache_ForeignArtistId",
                table: "LidarrArtistCache");

            migrationBuilder.DropIndex(
                name: "IX_LidarrArtistCache_ArtistId",
                table: "LidarrArtistCache");

            migrationBuilder.DropIndex(
                name: "IX_SonarrEpisodeCache_TvDbId_SeasonNumber_EpisodeNumber",
                table: "SonarrEpisodeCache");

            migrationBuilder.DropIndex(
                name: "IX_SonarrCache_TheMovieDbId",
                table: "SonarrCache");

            migrationBuilder.DropIndex(
                name: "IX_SonarrCache_TvDbId",
                table: "SonarrCache");

            migrationBuilder.DropIndex(
                name: "IX_RadarrCache_TheMovieDbId",
                table: "RadarrCache");

            migrationBuilder.DropIndex(
                name: "IX_JellyfinContent_TvDbId",
                table: "JellyfinContent");

            migrationBuilder.DropIndex(
                name: "IX_JellyfinContent_TheMovieDbId",
                table: "JellyfinContent");

            migrationBuilder.DropIndex(
                name: "IX_JellyfinContent_ImdbId",
                table: "JellyfinContent");

            migrationBuilder.DropIndex(
                name: "IX_JellyfinContent_JellyfinId",
                table: "JellyfinContent");

            migrationBuilder.DropIndex(
                name: "IX_EmbyContent_TvDbId",
                table: "EmbyContent");

            migrationBuilder.DropIndex(
                name: "IX_EmbyContent_TheMovieDbId",
                table: "EmbyContent");

            migrationBuilder.DropIndex(
                name: "IX_EmbyContent_ImdbId",
                table: "EmbyContent");

            migrationBuilder.DropIndex(
                name: "IX_EmbyContent_EmbyId",
                table: "EmbyContent");

            migrationBuilder.DropIndex(
                name: "IX_PlexEpisode_GrandparentKey_Key",
                table: "PlexEpisode");

            migrationBuilder.DropIndex(
                name: "IX_PlexServerContent_Type",
                table: "PlexServerContent");

            migrationBuilder.DropIndex(
                name: "IX_PlexServerContent_TvDbId",
                table: "PlexServerContent");

            migrationBuilder.DropIndex(
                name: "IX_PlexServerContent_TheMovieDbId",
                table: "PlexServerContent");

            migrationBuilder.DropIndex(
                name: "IX_PlexServerContent_ImdbId",
                table: "PlexServerContent");

            migrationBuilder.DropIndex(
                name: "IX_PlexServerContent_Key",
                table: "PlexServerContent");
        }
    }
}
