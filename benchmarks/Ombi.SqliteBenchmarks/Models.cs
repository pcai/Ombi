// Minimal models that mirror the shapes of the real Ombi entities, so the
// measured patterns are the ones the app actually runs. These are NOT the
// Ombi.Store entities: using the real OmbiContext would drag in Identity
// and the full migration history, which would dominate every measurement
// and make the harness slow and fragile. The mapping to real code is in
// README.md.
//
//   MovieRequest  ~ Ombi.Store.Entities.Requests.MovieRequests
//   TvRequest     ~ Ombi.Store.Entities.Requests.TvRequests (+ ChildRequests
//                   + EpisodeRequests, the graph TvRequestRepository.Get()
//                   loads before a whole-graph Update)
//   MediaContent  ~ Ombi.Store.Entities.PlexServerContent / RadarrCache
//                   (the External DB rows RefreshMetadata rewrites)

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Ombi.SqliteBenchmarks;

public class MovieRequest
{
    public int Id { get; set; }
    public int TheMovieDbId { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool Approved { get; set; }
    public bool Available { get; set; }
    public bool Available4K { get; set; }
    public DateTime? MarkedAsAvailable { get; set; }
    public DateTime? MarkedAsAvailable4K { get; set; }
    public int QualityProfileId { get; set; }
    public int RootPathId { get; set; }
    public string RequestedUserId { get; set; } = string.Empty;
    public DateTime RequestedDate { get; set; }
    public bool Denied { get; set; }
    public string? DeniedReason { get; set; }
}

public class TvRequest
{
    public int Id { get; set; }
    public int ExternalProviderId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int RootPathId { get; set; }
    public int QualityProfileId { get; set; }
    public List<ChildRequest> Children { get; set; } = new();
}

public class ChildRequest
{
    public int Id { get; set; }
    public int TvRequestId { get; set; }
    public int SeasonNumber { get; set; }
    public bool Approved { get; set; }
    public bool Available { get; set; }
    public List<EpisodeRequest> Episodes { get; set; } = new();
}

public class EpisodeRequest
{
    public int Id { get; set; }
    public int ChildRequestId { get; set; }
    public int EpisodeNumber { get; set; }
    public bool Available { get; set; }
}

public class MediaContent
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? ImdbId { get; set; }
    public int? TheMovieDbId { get; set; }
    public string Type { get; set; } = string.Empty;
    public bool Available { get; set; }
    public DateTime AddedAt { get; set; }
}

public class BenchContext : DbContext
{
    // The connection is supplied open (so pragmas can be applied before
    // EF uses it). EF does not dispose connections it did not open, so
    // the context must own it here: without this, every scenario leaked
    // an open connection to its file, and deleting that file for the next
    // scenario's fresh database failed with SQLITE_READONLY on recreate.
    private readonly SqliteConnection? _ownedConnection;

    public BenchContext(DbContextOptions<BenchContext> options, SqliteConnection? ownedConnection = null) : base(options)
    {
        _ownedConnection = ownedConnection;
    }

    public override void Dispose()
    {
        base.Dispose();
        _ownedConnection?.Dispose();
    }

    public DbSet<MovieRequest> MovieRequests => Set<MovieRequest>();
    public DbSet<TvRequest> TvRequests => Set<TvRequest>();
    public DbSet<ChildRequest> ChildRequests => Set<ChildRequest>();
    public DbSet<EpisodeRequest> EpisodeRequests => Set<EpisodeRequest>();
    public DbSet<MediaContent> MediaContents => Set<MediaContent>();
}
