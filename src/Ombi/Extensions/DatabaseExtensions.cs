using System;
using System.Data.Common;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MySqlConnector;
using Newtonsoft.Json;
using Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal;
using Ombi.Core.Helpers;
using Ombi.Core.Models;
using Ombi.Helpers;
using Ombi.Store.Context;
using Ombi.Store.Context.MySql;
using Ombi.Store.Context.Sqlite;
using Ombi.Store.Context.Postgres;
using Polly;
using Pomelo.EntityFrameworkCore.MySql.Storage.Internal;
using SQLitePCL;

namespace Ombi.Extensions
{
    public static class DatabaseExtensions
    {

        public const string SqliteDatabase = "Sqlite";
        public const string MySqlDatabase = "MySQL";
        public const string PostgresDatabase = "Postgres";

        private static int _sqliteConfigured;

        public static void ConfigureDatabases(this IServiceCollection services, IHealthChecksBuilder hcBuilder)
        {
            var configuration = GetDatabaseConfiguration();

            // Ombi db
            switch (configuration.OmbiDatabase.Type)
            {
                case var type when type.Equals(SqliteDatabase, StringComparison.InvariantCultureIgnoreCase):
                    services.AddDbContext<OmbiContext, OmbiSqliteContext>(x => ConfigureSqlite(x, configuration.OmbiDatabase));
                    AddSqliteHealthCheck(hcBuilder, "Ombi Database", configuration.OmbiDatabase);
                    break;
                case var type when type.Equals(MySqlDatabase, StringComparison.InvariantCultureIgnoreCase):
                    services.AddDbContext<OmbiContext, OmbiMySqlContext>(x => DatabaseConfigurationSetup.ConfigureMySql(x, configuration.OmbiDatabase));
                    AddMySqlHealthCheck(hcBuilder, "Ombi Database", configuration.OmbiDatabase);
                    break;
                case var type when type.Equals(PostgresDatabase, StringComparison.InvariantCultureIgnoreCase):
                    services.AddDbContext<OmbiContext, OmbiPostgresContext>(x => DatabaseConfigurationSetup.ConfigurePostgres(x, configuration.OmbiDatabase));
                    AddPostgresHealthCheck(hcBuilder, "Ombi Database", configuration.OmbiDatabase);
                    break;
            }

            switch (configuration.ExternalDatabase.Type)
            {
                case var type when type.Equals(SqliteDatabase, StringComparison.InvariantCultureIgnoreCase):
                    services.AddDbContext<ExternalContext, ExternalSqliteContext>(x => ConfigureSqlite(x, configuration.ExternalDatabase));
                    AddSqliteHealthCheck(hcBuilder, "External Database", configuration.ExternalDatabase);
                    break;
                case var type when type.Equals(MySqlDatabase, StringComparison.InvariantCultureIgnoreCase):
                    services.AddDbContext<ExternalContext, ExternalMySqlContext>(x => DatabaseConfigurationSetup.ConfigureMySql(x, configuration.ExternalDatabase));
                    AddMySqlHealthCheck(hcBuilder, "External Database", configuration.ExternalDatabase);
                    break;
                case var type when type.Equals(PostgresDatabase, StringComparison.InvariantCultureIgnoreCase):
                    services.AddDbContext<ExternalContext, ExternalPostgresContext>(x => DatabaseConfigurationSetup.ConfigurePostgres(x, configuration.ExternalDatabase));
                    AddPostgresHealthCheck(hcBuilder, "External Database", configuration.ExternalDatabase);
                    break;
            }

            switch (configuration.SettingsDatabase.Type)
            {
                case var type when type.Equals(SqliteDatabase, StringComparison.InvariantCultureIgnoreCase):
                    services.AddDbContext<SettingsContext, SettingsSqliteContext>(x => ConfigureSqlite(x, configuration.SettingsDatabase));
                    AddSqliteHealthCheck(hcBuilder, "Settings Database", configuration.SettingsDatabase);
                    break;
                case var type when type.Equals(MySqlDatabase, StringComparison.InvariantCultureIgnoreCase):
                    services.AddDbContext<SettingsContext, SettingsMySqlContext>(x => DatabaseConfigurationSetup.ConfigureMySql(x, configuration.SettingsDatabase));
                    AddMySqlHealthCheck(hcBuilder, "Settings Database", configuration.SettingsDatabase);
                    break;
                case var type when type.Equals(PostgresDatabase, StringComparison.InvariantCultureIgnoreCase):
                    services.AddDbContext<SettingsContext, SettingsPostgresContext>(x => DatabaseConfigurationSetup.ConfigurePostgres(x, configuration.SettingsDatabase));
                    AddPostgresHealthCheck(hcBuilder, "Settings Database", configuration.SettingsDatabase);
                    break;
            }
        }

        public static DatabaseConfiguration GetDatabaseConfiguration()
        {
            var i = StartupSingleton.Instance;
            if (string.IsNullOrEmpty(i.StoragePath))
            {
                i.StoragePath = string.Empty;
            }

            var databaseFileLocation = Path.Combine(i.StoragePath, "database.json");

            var configuration = new DatabaseConfiguration(i.StoragePath);
            if (File.Exists(databaseFileLocation))
            {
                var databaseJson = File.ReadAllText(databaseFileLocation);
                if (string.IsNullOrEmpty(databaseJson))
                {
                    throw new Exception("Found 'database.json' but it was empty!");
                }

                configuration = JsonConvert.DeserializeObject<DatabaseConfiguration>(databaseJson);
            }

            return configuration;
        }


        private static void AddSqliteHealthCheck(IHealthChecksBuilder builder, string dbName, PerDatabaseConfiguration config)
        {
            if (builder != null)
            {
                builder.AddSqlite(
                    sqliteConnectionString: config.ConnectionString,
                    name: dbName,
                    failureStatus: HealthStatus.Unhealthy,
                    tags: new string[] { "db" });
            }
        }

        private static void AddMySqlHealthCheck(IHealthChecksBuilder builder, string dbName, PerDatabaseConfiguration config)
        {
            if (builder != null)
            {
                builder.AddMySql(
                    connectionString: config.ConnectionString,
                    name: dbName,
                    failureStatus: HealthStatus.Unhealthy,
                    tags: new string[] { "db" }
                    );
            }
        }

        private static void AddPostgresHealthCheck(IHealthChecksBuilder builder, string dbName, PerDatabaseConfiguration config)
        {
            if (builder != null)
            {
                builder.AddNpgSql(
                    npgsqlConnectionString: config.ConnectionString,
                    name: dbName,
                    failureStatus: HealthStatus.Unhealthy,
                    tags: new string[] { "db" }
                    );
            }
        }

        public static void ConfigureSqlite(DbContextOptionsBuilder options, PerDatabaseConfiguration config)
        {
            SQLitePCL.Batteries.Init();
            // sqlite3_config only takes effect before SQLite initializes and
            // is a no-op error afterwards, so call it once instead of on
            // every options build; the result is not meaningful to check.
            if (Interlocked.CompareExchange(ref _sqliteConfigured, 1, 0) == 0)
            {
                SQLitePCL.raw.sqlite3_config(raw.SQLITE_CONFIG_MULTITHREAD);
            }
            options.UseSqlite(config.ConnectionString);
            options.AddInterceptors(new SqlitePragmasInterceptor());
        }

        /// <summary>
        /// Applies the SQLite configuration the bare connection string cannot
        /// express: WAL journal mode, a busy timeout, and (only once WAL is
        /// confirmed) synchronous NORMAL. WAL and busy timeout are PRAGMAs,
        /// not connection-string keywords, so a user cannot set them through
        /// database.json, and until now Ombi set neither: the defaults were
        /// DELETE journal mode and busy_timeout 0. Lock acquisition does
        /// retry up to the busy timeout, so writers do not all fail
        /// instantly; what contention runs actually showed was single-row
        /// inserts failing to complete outside the driver's retry loop,
        /// losing writes under DELETE journal. WAL is sticky per file
        /// once set; busy_timeout is per connection, so both are applied on
        /// every connection open. This is deliberately best-effort and
        /// never fails the connection, but failures are logged, not
        /// silent: WAL is unsupported on network filesystems, so if the
        /// file cannot use WAL the connection keeps whatever journal mode
        /// the file supports and synchronous stays at its default.
        /// </summary>
        private sealed class SqlitePragmasInterceptor : DbConnectionInterceptor
        {
            public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
            {
                ApplyPragmas(connection, eventData.Context);
                base.ConnectionOpened(connection, eventData);
            }

            public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
            {
                ApplyPragmas(connection, eventData.Context);
                await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
            }

            public override InterceptionResult ConnectionClosing(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
            {
                Optimize(connection, LoggerFor(eventData.Context));
                return base.ConnectionClosing(connection, eventData, result);
            }

            public override async ValueTask<InterceptionResult> ConnectionClosingAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
            {
                Optimize(connection, LoggerFor(eventData.Context));
                return await base.ConnectionClosingAsync(connection, eventData, result);
            }

            private static ILogger LoggerFor(DbContext context)
            {
                try
                {
                    // Logging must never break the connection, so any
                    // failure to resolve a logger degrades to silent.
                    return context == null
                        ? null
                        : ((IInfrastructure<IServiceProvider>)context).Instance.GetService<ILoggerFactory>()
                            ?.CreateLogger<SqlitePragmasInterceptor>();
                }
                catch
                {
                    return null;
                }
            }

            private static void ApplyPragmas(DbConnection connection, DbContext context)
            {
                var logger = LoggerFor(context);
                try
                {
                    using var command = connection.CreateCommand();
                    // busy_timeout first: if a writer already holds the
                    // file, the journal_mode change itself may need to wait.
                    command.CommandText = "PRAGMA busy_timeout=5000;";
                    command.ExecuteNonQuery();
                    command.CommandText = "PRAGMA journal_mode=WAL;";
                    var mode = command.ExecuteScalar()?.ToString();
                    if (string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase))
                    {
                        // synchronous NORMAL is only safe with WAL (a
                        // rollback after a crash is still guaranteed by the
                        // WAL file); with DELETE mode it risks corruption,
                        // so it is applied only when WAL actually took.
                        command.CommandText = "PRAGMA synchronous=NORMAL;";
                        command.ExecuteNonQuery();
                    }
                    else
                    {
                        logger?.LogWarning(
                            "SQLite journal mode is '{Mode}' instead of WAL for {DataSource}; "
                            + "WAL is unsupported on network filesystems, so this database keeps its existing mode",
                            mode, connection.DataSource);
                    }
                }
                catch (DbException e)
                {
                    // Best-effort: see the class comment. A pragma failure
                    // must not take down the connection that needed it.
                    logger?.LogWarning(e, "SQLite pragmas could not be applied to {DataSource}", connection.DataSource);
                }
            }

            private static void Optimize(DbConnection connection, ILogger logger)
            {
                // Keep planner statistics fresh so index choices do not go
                // stale as tables grow; PRAGMA optimize is designed to run
                // on close and does real work only when statistics are
                // missing or outdated. Best-effort like ApplyPragmas, but
                // with a hard bound on how long it may wait: when a table
                // lacks statistics, optimize runs ANALYZE for it, and
                // ANALYZE needs the write lock. If another connection
                // holds that lock, the wait runs to the ADO.NET command
                // timeout (60 seconds with Ombi's Default Timeout=60),
                // not the busy timeout — measured 30 seconds with
                // busy_timeout=5000 and with 0 alike, failing with
                // "database is locked" — so a read stalls for a full
                // minute behind a writer, and EF Core closes the
                // connection after every operation, so a sync doing two
                // queries can stall twice (120 seconds measured). The
                // stall is intermittent by design: optimize only
                // considers a table the connection has queried by index
                // (verified: an index-using read flips optimize(-1) from
                // empty to ANALYZE, and the close then blocks), so a
                // connection that only scanned closes instantly while one
                // that used an index stalls. The command therefore
                // carries its own 2 second timeout: the ANALYZE work
                // itself is 0.03 seconds at 100k rows, so 2 seconds
                // leaves ample headroom for realistic libraries, and a
                // timed-out attempt just leaves the statistics as they
                // were — the next close retries.
                try
                {
                    using var command = connection.CreateCommand();
                    command.CommandTimeout = 2;
                    command.CommandText = "PRAGMA optimize;";
                    command.ExecuteNonQuery();
                }
                catch (DbException e)
                {
                    // A skipped optimize is expected under contention,
                    // not a problem: log it at Debug, never fail the close.
                    logger?.LogDebug(e, "SQLite PRAGMA optimize skipped for {DataSource}", connection.DataSource);
                }
            }
        }
    }
}
