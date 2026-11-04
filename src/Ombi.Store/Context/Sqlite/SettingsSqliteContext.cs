using System;
using System.Threading;
using Microsoft.EntityFrameworkCore;

namespace Ombi.Store.Context.Sqlite
{
    public sealed class SettingsSqliteContext : SettingsContext
    {
        private static int _migrated;
        public SettingsSqliteContext(DbContextOptions<SettingsSqliteContext> options) : base(options)
        {
            // Command timeout is per-context-instance state, so it must be
            // set on every construction. The old code set it only on the
            // first constructed context (guarded by a static flag shared
            // with migration), leaving every later context on the provider
            // default. Migration itself still runs only once, now guarded
            // atomically: contexts are constructed on several threads
            // (HTTP + Quartz pool) and a plain bool check-then-set races.
            Database.SetCommandTimeout(60);
            if (Interlocked.CompareExchange(ref _migrated, 1, 0) == 0)
            {
                Upgrade();
                Database.Migrate();
            }

        }

        private void Upgrade()
        {
            try
            {
                Database.ExecuteSqlRaw(@"INSERT OR IGNORE INTO __EFMigrationsHistory (MigrationId,ProductVersion)
                VALUES('20191103205204_Inital', '2.2.6-servicing-10079'); ");
            }
#pragma warning disable RCS1075 // Avoid empty catch clause that catches System.Exception.
            catch (Exception)
#pragma warning restore RCS1075 // Avoid empty catch clause that catches System.Exception.
            {
                // ignored
            }
        }
    }
}