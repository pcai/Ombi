using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Ombi.Core.Models;
using Ombi.Extensions;
using Ombi.Store.Context.Sqlite;

namespace Ombi.Tests
{
    [TestFixture]
    public class SqlitePragmasInterceptorTests
    {
        private string _dir;
        private string _file;

        [SetUp]
        public void Setup()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ombi-opttest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _file = Path.Combine(_dir, "OmbiExternal.db");
        }

        [TearDown]
        public void Teardown()
        {
            try
            {
                Directory.Delete(_dir, true);
            }
            catch
            {
                // Temp files only; a leftover directory is harmless.
            }
        }

        private ExternalSqliteContext BuildContext(bool migrate = true)
        {
            var options = new DbContextOptionsBuilder<ExternalSqliteContext>();
            // Production-shaped connection string: Default Timeout=60 is
            // what the real configuration carries.
            DatabaseExtensions.ConfigureSqlite(options,
                new PerDatabaseConfiguration("Sqlite", $"Data Source={_file};Default Timeout=60"));
            var ctx = new ExternalSqliteContext(options.Options);
            // The context migrates only on first construction per process,
            // so migrate explicitly: a no-op when already migrated.
            // Skip it for the timed context: its own close would run
            // optimize first and hide (or move) the stall being measured.
            if (migrate)
            {
                ctx.Database.Migrate();
            }
            return ctx;
        }

        private void Raw(string sql)
        {
            using var conn = new SqliteConnection($"Data Source={_file}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private static bool StatsExist(string file)
        {
            using var conn = new SqliteConnection($"Data Source={file}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE name='sqlite_stat1';";
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        private void SeedAndDropStats()
        {
            Raw(@"WITH RECURSIVE c(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM c WHERE x < 5000)
                  INSERT INTO ""PlexEpisode"" (EpisodeNumber, GrandparentKey, Key, ParentKey, SeasonNumber, Title)
                  SELECT 1, NULL, 'k' || x, '5', 1, 'Episode ' || x FROM c;");
            // Whatever ran before must not leave statistics behind: the
            // stall only happens when they are missing.
            Raw("DROP TABLE IF EXISTS sqlite_stat1;");
            Assert.That(StatsExist(_file), Is.False);
        }

        [Test]
        public void ConnectionClose_WithMissingStatsAndHeldWriteLock_ShouldNotStall()
        {
            // Regression: PRAGMA optimize runs on every connection close
            // and needs the write lock when statistics are missing. The
            // wait runs to the ADO.NET command timeout (60 s with the
            // production connection string), not the busy timeout, so a
            // read stalled for a full minute behind a writer.
            using (var ctx = BuildContext())
            {
                SeedAndDropStats();
            }

            using var writer = new SqliteConnection($"Data Source={_file}");
            writer.Open();
            using (var lockCmd = writer.CreateCommand())
            {
                lockCmd.CommandText = "BEGIN IMMEDIATE; INSERT INTO \"PlexEpisode\" (EpisodeNumber, GrandparentKey, Key, ParentKey, SeasonNumber, Title) VALUES (1, NULL, 'hold', '5', 1, 'Hold');";
                lockCmd.ExecuteNonQuery();
            }

            var elapsed = Stopwatch.StartNew();
            using (var ctx = BuildContext(migrate: false))
            {
                // No explicit OpenConnection: the connection must open and
                // close per operation, as in production, so the close —
                // and the optimize it triggers — happens here.
                // The equality query uses IX_PlexEpisode_GrandparentKey:
                // optimize only considers analyzing a table the connection
                // has queried by index, so without it the close is a
                // no-op and the stall never reproduces.
                _ = ctx.PlexEpisode.Where(e => e.GrandparentKey == "missing").CountAsync().GetAwaiter().GetResult();
                var count = ctx.PlexEpisode.CountAsync().GetAwaiter().GetResult();
                Assert.That(count, Is.GreaterThan(0));
            }
            elapsed.Stop();

            Assert.That(elapsed.Elapsed.TotalSeconds, Is.LessThan(10),
                $"Closing the connection took {elapsed.Elapsed.TotalSeconds:F1}s with statistics missing and a write lock held");
        }

        [Test]
        public void ConnectionClose_WithFreshStatsAndHeldWriteLock_ShouldNotStall()
        {
            // With fresh statistics optimize is a no-op that takes no
            // lock, so the close stays fast even with a writer active.
            using (var ctx = BuildContext())
            {
                SeedAndDropStats();
            }
            Raw("ANALYZE;");
            Assert.That(StatsExist(_file), Is.True);

            using var writer = new SqliteConnection($"Data Source={_file}");
            writer.Open();
            using (var lockCmd = writer.CreateCommand())
            {
                lockCmd.CommandText = "BEGIN IMMEDIATE; INSERT INTO \"PlexEpisode\" (EpisodeNumber, GrandparentKey, Key, ParentKey, SeasonNumber, Title) VALUES (1, NULL, 'hold', '5', 1, 'Hold');";
                lockCmd.ExecuteNonQuery();
            }

            var elapsed = Stopwatch.StartNew();
            using (var ctx = BuildContext(migrate: false))
            {
                // No explicit OpenConnection: see the test above.
                // Same index-using query: without it optimize has no
                // reason to analyze and the test would pass vacuously.
                _ = ctx.PlexEpisode.Where(e => e.GrandparentKey == "missing").CountAsync().GetAwaiter().GetResult();
                _ = ctx.PlexEpisode.CountAsync().GetAwaiter().GetResult();
            }
            elapsed.Stop();

            Assert.That(elapsed.Elapsed.TotalSeconds, Is.LessThan(10),
                $"Closing the connection took {elapsed.Elapsed.TotalSeconds:F1}s with fresh statistics and a write lock held");
        }

        [Test]
        public void ConnectionClose_WithMissingStatsAndNoWriter_ShouldBuildStatistics()
        {
            // The fix must bound the wait, not remove the work: with no
            // writer in the way, closing still runs optimize and the
            // statistics appear.
            using (var ctx = BuildContext())
            {
                SeedAndDropStats();
            }
            Assert.That(StatsExist(_file), Is.False);

            using (var ctx = BuildContext(migrate: false))
            {
                // No explicit OpenConnection: see the test above.
                // Same index-using query: without it optimize has no
                // reason to analyze and the test would pass vacuously.
                _ = ctx.PlexEpisode.Where(e => e.GrandparentKey == "missing").CountAsync().GetAwaiter().GetResult();
                _ = ctx.PlexEpisode.CountAsync().GetAwaiter().GetResult();
            }

            Assert.That(StatsExist(_file), Is.True);
        }
    }
}
