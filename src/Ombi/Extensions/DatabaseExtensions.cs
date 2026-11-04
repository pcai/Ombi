using System;
using System.Data.Common;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
            SQLitePCL.raw.sqlite3_config(raw.SQLITE_CONFIG_MULTITHREAD);
            options.UseSqlite(config.ConnectionString);
            options.AddInterceptors(new SqlitePragmasInterceptor());
        }

        /// <summary>
        /// Applies the SQLite configuration the bare connection string cannot
        /// express: WAL journal mode, a busy timeout, and (only once WAL is
        /// confirmed) synchronous NORMAL. WAL and busy timeout are PRAGMAs,
        /// not connection-string keywords, so a user cannot set them through
        /// database.json, and until now Ombi set neither: the default
        /// DELETE journal mode plus busy_timeout 0 meant any overlap between
        /// two writers was an instant SQLITE_BUSY. WAL is sticky per file
        /// once set; busy_timeout is per connection, so both are applied on
        /// every connection open. This is deliberately best-effort: if the
        /// file cannot use WAL (read-only file, network filesystem), the
        /// connection still opens with today's behaviour rather than
        /// failing every database operation.
        /// </summary>
        private sealed class SqlitePragmasInterceptor : DbConnectionInterceptor
        {
            public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
            {
                ApplyPragmas(connection);
                base.ConnectionOpened(connection, eventData);
            }

            public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
            {
                ApplyPragmas(connection);
                await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
            }

            private static void ApplyPragmas(DbConnection connection)
            {
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
                }
                catch (DbException)
                {
                    // Best-effort: see the class comment. A pragma failure
                    // must not take down the connection that needed it.
                }
            }
        }
    }
}
