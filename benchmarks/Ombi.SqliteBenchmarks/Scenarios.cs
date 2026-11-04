// The scenarios. Each one takes a fresh, identically-seeded database file
// and returns a measurement. Two configurations are compared throughout:
//
//   baseline : exactly what Ombi configures today -- a bare
//              "Data Source=<file>" connection: DELETE journal mode,
//              busy_timeout 0 (any overlap is an instant SQLITE_BUSY),
//              synchronous FULL, no retry.
//              (src/Ombi/Extensions/DatabaseExtensions.cs, ConfigureSqlite)
//   improved : WAL journal mode + busy_timeout 5000ms + synchronous NORMAL.
//              WAL is sticky per file once set; the timeout is per connection,
//              so both pragmas must be applied on every connection open.
//              This is the configuration the refactor under test will apply;
//              the scenarios that only change the *pattern* (batched saves,
//              stub updates, indexes) are run under `baseline` config so the
//              pattern effect is not confounded with the config effect,
//              except contention, which is inherently about the config.
//
// A scenario that throws a non-busy exception fails the run loudly; a
// SQLITE_BUSY (SqliteErrorCode 5) is counted, not thrown, because the busy
// count IS the measurement for the contention scenario.

using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Ombi.SqliteBenchmarks;

public enum Config { Baseline, Improved }

public sealed record Result(
    string Scenario,
    string Variant,
    double MedianMs,
    double[] RunsMs,
    int BusyErrors,
    string Notes,
    int RowsWritten = 0);

public static class Scenarios
{
    public const int MovieRows = 500;
    public const int TvRoots = 20;          // x 5 children x 10 episodes
    public const int ContentRows = 500;
    public const int LookupRows = 20_000;
    public const int LookupQueries = 1_000;
    public const int Writers = 4;
    public const int WritesPerWriter = 200;

    public static BenchContext OpenContext(string file, Config config)
    {
        var conn = new SqliteConnection($"Data Source={file}");
        conn.Open();
        if (config == Config.Improved)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA synchronous=NORMAL;";
            cmd.ExecuteNonQuery();
        }
        // Baseline intentionally sets nothing: DELETE mode, busy_timeout 0.
        var options = new DbContextOptionsBuilder<BenchContext>()
            .UseSqlite(conn)
            .Options;
        return new BenchContext(options, conn);
    }

    // Fresh file per invocation, never reused: reusing a filename that a
    // previous scenario just deleted fails deterministically on this host
    // with SQLITE_READONLY when the next scenario recreates it (the
    // RefreshBatched warmup crashed exactly that way, twice, while the
    // otherwise identical per-item warmup on a fresh name passed).
    // Unique names keep every run on a genuinely fresh file, which is
    // what "fresh starting state" meant in the first place.
    public static string FreshDb(string dir, string name)
    {
        var file = Path.Combine(dir, name + "-" + Guid.NewGuid().ToString("N")[..8] + ".db");
        return file;
    }

    public static void SeedMovies(BenchContext ctx, int count)
    {
        var rows = Enumerable.Range(1, count).Select(i => new MovieRequest
        {
            TheMovieDbId = 100_000 + i,
            Title = $"Movie {i}",
            Approved = true,
            Available = false,
            Available4K = false,
            QualityProfileId = 1,
            RootPathId = 1,
            RequestedUserId = "user-1",
            RequestedDate = DateTime.UtcNow.AddDays(-i),
        }).ToList();
        ctx.MovieRequests.AddRange(rows);
        ctx.SaveChanges();
    }

    public static void SeedTv(BenchContext ctx, int roots)
    {
        foreach (var r in Enumerable.Range(1, roots))
        {
            var tv = new TvRequest
            {
                ExternalProviderId = 200_000 + r,
                Title = $"Show {r}",
                RootPathId = 1,
                QualityProfileId = 1,
            };
            foreach (var s in Enumerable.Range(1, 5))
            {
                var child = new ChildRequest { SeasonNumber = s, Approved = true };
                foreach (var e in Enumerable.Range(1, 10))
                    child.Episodes.Add(new EpisodeRequest { EpisodeNumber = e });
                tv.Children.Add(child);
            }
            ctx.TvRequests.Add(tv);
        }
        ctx.SaveChanges();
    }

    public static void SeedContent(BenchContext ctx, int count)
    {
        var rows = Enumerable.Range(1, count).Select(i => new MediaContent
        {
            Key = $"/library/metadata/{i}",
            Title = $"Content {i}",
            ImdbId = $"tt{i:0000000}",
            TheMovieDbId = 300_000 + i,
            Type = "movie",
            Available = false,
            AddedAt = DateTime.UtcNow.AddDays(-i),
        }).ToList();
        ctx.MediaContents.AddRange(rows);
        ctx.SaveChanges();
    }

    private static bool IsBusy(Exception ex) =>
        ex is SqliteException se && se.SqliteErrorCode == 5
        || ex.InnerException is SqliteException ise && ise.SqliteErrorCode == 5;

    // --- Scenario 1: PlexAvailabilityChecker shape -----------------------
    // Current code: an unmaterialized query is enumerated and SaveChanges is
    // called up to 3x per movie (non-4K branch, 4K branch, quality branch --
    // the last two can both fire). Batched: same mutations, one save at end.
    // Mirrors src/Ombi.Schedule/Jobs/Plex/PlexAvailabilityChecker.cs.
    public static Result AvailabilityPerItem(string dir) => Run(
        "availability", "per-item SaveChanges (pre-refactor Plex shape)", dir,
        ctx => { SeedMovies(ctx, MovieRows); },
        ctx =>
        {
            var movies = ctx.MovieRequests.ToList();
            var written = 0;
            foreach (var m in movies)
            {
                m.Available = true; m.MarkedAsAvailable = DateTime.UtcNow;
                written += ctx.SaveChanges();
                m.Available4K = true; m.MarkedAsAvailable4K = DateTime.UtcNow;
                written += ctx.SaveChanges();
                m.QualityProfileId = 2;
                written += ctx.SaveChanges();
            }
            return written;
        });

    public static Result AvailabilityBatched(string dir) => Run(
        "availability", "one SaveChanges at end", dir,
        ctx => { SeedMovies(ctx, MovieRows); },
        ctx =>
        {
            var movies = ctx.MovieRequests.ToList();
            foreach (var m in movies)
            {
                m.Available = true; m.MarkedAsAvailable = DateTime.UtcNow;
                m.Available4K = true; m.MarkedAsAvailable4K = DateTime.UtcNow;
                m.QualityProfileId = 2;
            }
            return ctx.SaveChanges();
        });

    // --- Scenario 2: whole-graph Db.Update vs stub property update -------
    // TvRequestRepository.Update calls Db.Update(entity). What that rewrites
    // depends on tracking, and the first version of this benchmark caught
    // that: when the graph was loaded in the SAME context (as in
    // UpdateRootPath/UpdateQualityProfile, which load via Get() then Update
    // in one scope), EF only marks the root Modified -- 1 row per save,
    // and the real cost there is loading the full graph to change one
    // field. When the graph is DETACHED (AsNoTracking load, or an entity
    // that crossed a scope boundary), Db.Update marks every reachable
    // entity Modified and rewrites the whole graph. This scenario measures
    // the detached case, which is the rewrite hazard; the stub pattern
    // (already used by MarkAsAvailable/MarkChildAsAvailable in-repo)
    // attaches an empty entity and marks only the changed property, in
    // both cases without loading the graph at all.
    public static Result WholeGraphUpdate(string dir) => Run(
        "whole-graph-update", "Db.Update(detached graph)", dir,
        ctx => { SeedTv(ctx, TvRoots); },
        ctx =>
        {
            var roots = ctx.TvRequests.AsNoTracking()
                .Include(t => t.Children).ThenInclude(c => c.Episodes)
                .ToList();
            var written = 0;
            foreach (var tv in roots)
            {
                tv.RootPathId = 7;
                ctx.Update(tv);
                written += ctx.SaveChanges();
            }
            return written;
        });

    public static Result StubPropertyUpdate(string dir) => Run(
        "whole-graph-update", "stub + IsModified on one property", dir,
        ctx => { SeedTv(ctx, TvRoots); },
        ctx =>
        {
            var ids = ctx.TvRequests.Select(t => t.Id).ToList();
            var written = 0;
            foreach (var id in ids)
            {
                var stub = new TvRequest { Id = id, RootPathId = 7 };
                ctx.Attach(stub);
                ctx.Entry(stub).Property(x => x.RootPathId).IsModified = true;
                written += ctx.SaveChanges();
            }
            return written;
        });

    // --- Scenario 3: RefreshMetadata shape -------------------------------
    // Pre-refactor code: SaveChangesAsync per movie/show across all six
    // Start* methods, even when nothing changed. It now saves every 100
    // items plus a final save per pass. Mirrors
    // src/Ombi.Schedule/Jobs/Ombi/RefreshMetadata.cs (External DB).
    public static Result RefreshPerItem(string dir) => Run(
        "refresh-metadata", "SaveChanges per item (pre-refactor)", dir,
        ctx => { SeedContent(ctx, ContentRows); },
        ctx =>
        {
            var written = 0;
            foreach (var c in ctx.MediaContents.ToList())
            {
                c.Available = true;
                written += ctx.SaveChanges();
            }
            return written;
        });

    public static Result RefreshBatched(string dir) => Run(
        "refresh-metadata", "one SaveChanges at end", dir,
        ctx => { SeedContent(ctx, ContentRows); },
        ctx =>
        {
            foreach (var c in ctx.MediaContents.ToList()) c.Available = true;
            return ctx.SaveChanges();
        });

    // --- Scenario 4: concurrent writers on one file ----------------------
    // The contention case: several Quartz jobs (content sync, availability
    // checker, request flow) writing the same .db at once, with no job
    // mutual exclusion. Each writer inserts rows with a save per row.
    public static Result Contention(Config config, string dir)
    {
        var name = config == Config.Baseline ? "contention-baseline" : "contention-improved";
        var file = FreshDb(dir, name);
        using (var ctx = OpenContext(file, config))
        {
            ctx.Database.EnsureCreated();
        }
        var busy = 0;
        var written = 0;
        var sw = Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, Writers).Select(w => Task.Run(() =>
        {
            using var ctx = OpenContext(file, config);
            for (var i = 0; i < WritesPerWriter; i++)
            {
                ctx.MediaContents.Add(new MediaContent
                {
                    Key = $"w{w}/{i}", Title = $"W{w} {i}", Type = "movie",
                    AddedAt = DateTime.UtcNow,
                });
                try { Interlocked.Add(ref written, ctx.SaveChanges()); }
                catch (Exception ex) when (IsBusy(ex))
                {
                    Interlocked.Increment(ref busy);
                    ctx.ChangeTracker.Clear();
                }
            }
        })).ToArray();
        Task.WaitAll(tasks);
        sw.Stop();
        // Single-run scenario (parallel timing is not repeatable enough to
        // median meaningfully); the busy count is the headline number.
        return new Result("contention",
            config == Config.Baseline
                ? "DELETE + busy_timeout 0 (pre-refactor)"
                : "WAL + busy_timeout 5000 + synchronous NORMAL",
            sw.Elapsed.TotalMilliseconds, new[] { sw.Elapsed.TotalMilliseconds },
            busy,
            $"{Writers} writers x {WritesPerWriter} inserts, save per row",
            written);
    }

    // --- Scenario 5: lookup key with / without an index ------------------
    // Snapshots index FKs and RequestedUserId but not the keys the sync and
    // availability code actually looks up by (MovieRequests.TheMovieDbId,
    // TvRequests.ExternalProviderId, content Key/ImdbId/TheMovieDbId).
    public static Result Lookup(bool indexed, string dir)
    {
        var file = FreshDb(dir, indexed ? "lookup-indexed" : "lookup-plain");
        using (var ctx = OpenContext(file, Config.Baseline))
        {
            ctx.Database.EnsureCreated();
            SeedContent(ctx, LookupRows);
            if (indexed)
                ctx.Database.ExecuteSqlRaw(
                    "CREATE INDEX IX_MediaContents_TheMovieDbId ON MediaContents (TheMovieDbId);");
        }
        var rnd = new Random(42);
        var targets = Enumerable.Range(0, LookupQueries)
            .Select(_ => 300_000 + rnd.Next(1, LookupRows + 1)).ToList();
        var runs = new List<double>();
        for (var run = 0; run < Program.Iterations; run++)
        {
            using var ctx = OpenContext(file, Config.Baseline);
            var sw = Stopwatch.StartNew();
            foreach (var t in targets)
                _ = ctx.MediaContents.AsNoTracking()
                    .FirstOrDefault(c => c.TheMovieDbId == t);
            sw.Stop();
            runs.Add(sw.Elapsed.TotalMilliseconds);
        }
        runs.Sort();
        return new Result("lookup",
            indexed ? "indexed TheMovieDbId" : "no index (pre-refactor)",
            runs[runs.Count / 2], runs.ToArray(), 0,
            $"{LookupQueries} point lookups over {LookupRows} rows",
            LookupQueries);
    }

    // Shared runner: warmup (discarded) + N timed runs on fresh files.
    // Seeding is outside the timed section; only the measured pattern is.
    private static Result Run(string scenario, string variant, string dir,
        Action<BenchContext> seed, Func<BenchContext, int> work)
    {
        // Warmup, discarded. Named per scenario, not shared "warmup":
        // a shared name meant each scenario's fresh file collided with
        // the previous scenario's still-open connection.
        {
            var file = FreshDb(dir, scenario + "-warmup");
            using var ctx = OpenContext(file, Config.Baseline);
            ctx.Database.EnsureCreated();
            seed(ctx);
            // Seeding leaves entities tracked; a fresh scope in the real app
            // would not have them. Clear so the work phase measures what it
            // claims to (and so stub Attach does not collide with a tracked
            // instance of the same key, which crashed the first run).
            ctx.ChangeTracker.Clear();
            work(ctx);
        }
        var runs = new List<double>();
        var busy = 0;
        var written = 0;
        for (var run = 0; run < Program.Iterations; run++)
        {
            var file = FreshDb(dir, $"{scenario}-{run}");
            using var ctx = OpenContext(file, Config.Baseline);
            ctx.Database.EnsureCreated();
            seed(ctx);
            ctx.ChangeTracker.Clear();
            var sw = Stopwatch.StartNew();
            try { written = work(ctx); }
            catch (Exception ex) when (IsBusy(ex)) { busy++; }
            sw.Stop();
            runs.Add(sw.Elapsed.TotalMilliseconds);
        }
        runs.Sort();
        return new Result(scenario, variant, runs[runs.Count / 2],
            runs.ToArray(), busy, "", written);
    }
}
