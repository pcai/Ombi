using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Ombi.Store.Context.Sqlite;

namespace Ombi.Tests
{
    [TestFixture]
    public class MigrationModelTests
    {
        private string _dir;

        [SetUp]
        public void Setup()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ombi-migtest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
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

        private static HashSet<string> IndexNames(string file)
        {
            using var conn = new SqliteConnection($"Data Source={file}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index'";
            using var reader = cmd.ExecuteReader();
            var names = new HashSet<string>();
            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }
            return names;
        }

        private static List<string> PlanDetails(string file, string sql)
        {
            using var conn = new SqliteConnection($"Data Source={file}");
            conn.Open();
            // Production runs PRAGMA optimize on connection close, which
            // creates these statistics; reproduce that here so the plan
            // reflects a database that has been through normal use.
            using (var analyze = conn.CreateCommand())
            {
                analyze.CommandText = "ANALYZE;";
                analyze.ExecuteNonQuery();
            }
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "EXPLAIN QUERY PLAN " + sql;
            using var reader = cmd.ExecuteReader();
            var details = new List<string>();
            while (reader.Read())
            {
                details.Add(reader.GetString(3));
            }
            return details;
        }

        [Test]
        public void ExternalMigration_ModelAgreesWithSnapshotAndIndexesAreAsExpected()
        {
            var file = Path.Combine(_dir, "OmbiExternal.db");
            var options = new DbContextOptionsBuilder<ExternalSqliteContext>()
                .UseSqlite($"Data Source={file}").Options;
            using (var ctx = new ExternalSqliteContext(options))
            {
                // Constructing the context runs the full migration history.
                // This was true for the previous migration (the composite
                // index made the model want to drop the single-column
                // foreign-key index the snapshot still had).
                Assert.That(ctx.Database.HasPendingModelChanges(), Is.False);
            }

            var indexes = IndexNames(file);
            Assert.Multiple(() =>
            {
                foreach (var dropped in new[]
                {
                    "IX_PlexServerContent_Key", "IX_PlexServerContent_Type",
                    "IX_PlexEpisode_GrandparentKey_Key", "IX_EmbyContent_EmbyId",
                    "IX_JellyfinContent_JellyfinId",
                })
                {
                    Assert.That(indexes, Does.Not.Contain(dropped), $"dropped index still present: {dropped}");
                }
                foreach (var added in new[]
                {
                    "IX_PlexServerContent_Title_ReleaseYear_Type", "IX_JellyfinEpisode_JellyfinId",
                })
                {
                    Assert.That(indexes, Does.Contain(added), $"added index missing: {added}");
                }
                foreach (var kept in new[]
                {
                    "IX_PlexServerContent_ImdbId", "IX_PlexServerContent_TheMovieDbId",
                    "IX_PlexServerContent_TvDbId", "IX_PlexEpisode_GrandparentKey",
                })
                {
                    Assert.That(indexes, Does.Contain(kept), $"kept index missing: {kept}");
                }
            });

            var providerPlan = PlanDetails(file,
                "SELECT * FROM PlexServerContent WHERE ImdbId = 'tt1' AND Type = 0");
            Assert.That(string.Join(" ", providerPlan),
                Does.Contain("USING INDEX IX_PlexServerContent_ImdbId"),
                "GetByType must use the provider-id index, not a scan: " + string.Join(" | ", providerPlan));

            var titlePlan = PlanDetails(file,
                "SELECT * FROM PlexServerContent WHERE Title = 'Show' AND ReleaseYear = '2020' AND Type = 1");
            Assert.That(string.Join(" ", titlePlan),
                Does.Contain("USING INDEX IX_PlexServerContent_Title_ReleaseYear_Type"),
                "Title matching must use the title composite: " + string.Join(" | ", titlePlan));
        }

        [Test]
        public void OmbiMigration_ModelAgreesWithSnapshot()
        {
            var file = Path.Combine(_dir, "Ombi.db");
            var options = new DbContextOptionsBuilder<OmbiSqliteContext>()
                .UseSqlite($"Data Source={file}").Options;
            using (var ctx = new OmbiSqliteContext(options))
            {
                Assert.That(ctx.Database.HasPendingModelChanges(), Is.False);
            }
        }
    }
}
