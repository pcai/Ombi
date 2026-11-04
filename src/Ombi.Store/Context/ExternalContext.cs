using System.Collections.Generic;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Ombi.Helpers;
using Ombi.Store.Entities;

namespace Ombi.Store.Context
{
    public abstract class ExternalContext : DbContext
    {
        protected ExternalContext(DbContextOptions<ExternalContext> options) : base(options)
        {

        }

        /// <summary>
        /// This allows a sub class to call the base class 'DbContext' non typed constructor
        /// This is need because instances of the subclasses will use a specific typed DbContextOptions
        /// which can not be converted into the parameter in the above constructor
        /// </summary>
        /// <param name="options"></param>
        protected ExternalContext(DbContextOptions options)
            : base(options)
        {
        }

        public DbSet<PlexServerContent> PlexServerContent { get; set; }
        public DbSet<PlexSeasonsContent> PlexSeasonsContent { get; set; }
        public DbSet<PlexEpisode> PlexEpisode { get; set; }
        public DbSet<PlexWatchlistHistory> PlexWatchlistHistory { get; set; }
        public DbSet<RadarrCache> RadarrCache { get; set; }
        public DbSet<CouchPotatoCache> CouchPotatoCache { get; set; }
        public DbSet<EmbyContent> EmbyContent { get; set; }
        public DbSet<EmbyEpisode> EmbyEpisode { get; set; }
        public DbSet<JellyfinEpisode> JellyfinEpisode { get; set; }
        public DbSet<JellyfinContent> JellyfinContent { get; set; }
        
        public DbSet<SonarrCache> SonarrCache { get; set; }
        public DbSet<LidarrArtistCache> LidarrArtistCache { get; set; }
        public DbSet<LidarrAlbumCache> LidarrAlbumCache { get; set; }
        public DbSet<SonarrEpisodeCache> SonarrEpisodeCache { get; set; }
        public DbSet<SickRageCache> SickRageCache { get; set; }
        public DbSet<SickRageEpisodeCache> SickRageEpisodeCache { get; set; }
        public DbSet<UserPlayedMovie> UserPlayedMovie { get; set; }
        public DbSet<UserPlayedEpisode> UserPlayedEpisode { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<PlexServerContent>().HasMany(x => (ICollection<PlexEpisode>) x.Episodes)
                .WithOne(x => (PlexServerContent) x.Series)
                .HasPrincipalKey(x => x.Key)
                .HasForeignKey(x => x.GrandparentKey);

            builder.Entity<EmbyEpisode>()
                .HasOne(p => (EmbyContent) p.Series)
                .WithMany(b => (ICollection<EmbyEpisode>) b.Episodes)
                .HasPrincipalKey(x => x.EmbyId)
                .HasForeignKey(p => p.ParentId);

            builder.Entity<JellyfinEpisode>()
                .HasOne(p => (JellyfinContent) p.Series)
                .WithMany(b => (ICollection<JellyfinEpisode>) b.Episodes)
                .HasPrincipalKey(x => x.JellyfinId)
                .HasForeignKey(p => p.ParentId);

            // Indexes on the keys sync and availability code looks up by.
            // These columns are not foreign keys, so EF creates no index
            // for them by convention, and every point lookup on them is a
            // full table scan over a table that grows with library size.
            builder.Entity<PlexServerContent>()
                .HasIndex(x => x.ImdbId);
            builder.Entity<PlexServerContent>()
                .HasIndex(x => x.TheMovieDbId);
            builder.Entity<PlexServerContent>()
                .HasIndex(x => x.TvDbId);
            // PlexContentSync matches movies and shows on Title,
            // ReleaseYear and Type when it cannot match on the key.
            builder.Entity<PlexServerContent>()
                .HasIndex(x => new { x.Title, x.ReleaseYear, x.Type });
            builder.Entity<EmbyContent>()
                .HasIndex(x => x.ImdbId);
            builder.Entity<EmbyContent>()
                .HasIndex(x => x.TheMovieDbId);
            builder.Entity<EmbyContent>()
                .HasIndex(x => x.TvDbId);
            builder.Entity<JellyfinContent>()
                .HasIndex(x => x.ImdbId);
            builder.Entity<JellyfinContent>()
                .HasIndex(x => x.TheMovieDbId);
            builder.Entity<JellyfinContent>()
                .HasIndex(x => x.TvDbId);
            // JellyfinEpisodeSync looks up episodes by JellyfinId per episode.
            builder.Entity<JellyfinEpisode>()
                .HasIndex(x => x.JellyfinId);
            builder.Entity<RadarrCache>()
                .HasIndex(x => x.TheMovieDbId);
            builder.Entity<SonarrCache>()
                .HasIndex(x => x.TvDbId);
            builder.Entity<SonarrCache>()
                .HasIndex(x => x.TheMovieDbId);
            // The per-episode availability check filters on all three.
            builder.Entity<SonarrEpisodeCache>()
                .HasIndex(x => new { x.TvDbId, x.SeasonNumber, x.EpisodeNumber });
            builder.Entity<LidarrArtistCache>()
                .HasIndex(x => x.ArtistId);
            builder.Entity<LidarrArtistCache>()
                .HasIndex(x => x.ForeignArtistId);
            builder.Entity<LidarrAlbumCache>()
                .HasIndex(x => x.ArtistId);
            builder.Entity<LidarrAlbumCache>()
                .HasIndex(x => x.ForeignAlbumId);
            // Played lookups filter on TheMovieDbId (the UserId filter
            // happens in memory over that result).
            builder.Entity<UserPlayedMovie>()
                .HasIndex(x => x.TheMovieDbId);
            builder.Entity<UserPlayedEpisode>()
                .HasIndex(x => x.TheMovieDbId);

            base.OnModelCreating(builder);
        }
    }
}
