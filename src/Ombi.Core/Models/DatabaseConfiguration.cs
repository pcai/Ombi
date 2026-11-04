using System.IO;

namespace Ombi.Core.Models;

public class DatabaseConfiguration
{
    public const string SqliteDatabase = "Sqlite";

    public DatabaseConfiguration()
    {

    }

    public DatabaseConfiguration(string defaultSqlitePath)
    {
        // Default Timeout matches the 60-second command timeout the contexts
        // set: EF's command timeout does not cover BeginTransaction, which
        // uses DefaultTimeout (30 seconds by default) instead, so without
        // this a contended multi-row save could time out at ~35 seconds
        // despite the 60-second command timeout.
        OmbiDatabase = new PerDatabaseConfiguration(SqliteDatabase, $"Data Source={Path.Combine(defaultSqlitePath, "Ombi.db")};Default Timeout=60");
        SettingsDatabase = new PerDatabaseConfiguration(SqliteDatabase, $"Data Source={Path.Combine(defaultSqlitePath, "OmbiSettings.db")};Default Timeout=60");
        ExternalDatabase = new PerDatabaseConfiguration(SqliteDatabase, $"Data Source={Path.Combine(defaultSqlitePath, "OmbiExternal.db")};Default Timeout=60");
    }
    public PerDatabaseConfiguration OmbiDatabase { get; set; }
    public PerDatabaseConfiguration SettingsDatabase { get; set; }
    public PerDatabaseConfiguration ExternalDatabase { get; set; }
}

public class PerDatabaseConfiguration
{
    public PerDatabaseConfiguration(string type, string connectionString)
    {
        Type = type;
        ConnectionString = connectionString;
    }

    // Used in Deserialization
    public PerDatabaseConfiguration()
    {

    }
    public string Type { get; set; }
    public string ConnectionString { get; set; }
}