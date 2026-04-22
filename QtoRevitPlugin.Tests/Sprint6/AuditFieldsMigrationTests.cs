using Dapper;
using Microsoft.Data.Sqlite;
using QtoRevitPlugin.Data;
using System;
using System.IO;
using Xunit;

namespace QtoRevitPlugin.Tests.Sprint6
{
    public class AuditFieldsMigrationTests
    {
        private static void SafeDelete(string path)
        {
            SqliteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            File.Delete(path);
        }

        [Fact]
        public void NewDb_HasChangeLogTable()
        {
            var path = Path.GetTempFileName();
            try
            {
                object result;
                var init = new DatabaseInitializer(path);
                using (var conn = init.OpenOrCreate())
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='ChangeLog';";
                    result = cmd.ExecuteScalar();
                }
                Assert.Equal("ChangeLog", result);
            }
            finally { SafeDelete(path); }
        }

        [Fact]
        public void NewDb_QtoAssignments_HasCreatedByColumn()
        {
            var path = Path.GetTempFileName();
            try
            {
                bool found;
                var init = new DatabaseInitializer(path);
                using (var conn = init.OpenOrCreate())
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "PRAGMA table_info(QtoAssignments);";
                    using var reader = cmd.ExecuteReader();
                    found = false;
                    while (reader.Read())
                        if (reader.GetString(1) == "CreatedBy") { found = true; break; }
                }
                Assert.True(found);
            }
            finally { SafeDelete(path); }
        }

        [Fact]
        public void NewDb_HasElementSnapshotsTable()
        {
            var path = Path.GetTempFileName();
            try
            {
                object result;
                var init = new DatabaseInitializer(path);
                using (var conn = init.OpenOrCreate())
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='ElementSnapshots';";
                    result = cmd.ExecuteScalar();
                }
                Assert.Equal("ElementSnapshots", result);
            }
            finally { SafeDelete(path); }
        }

        [Fact]
        public void ExistingV3Db_MigratesTo_V4()
        {
            var path = Path.GetTempFileName();
            try
            {
                using (var conn = new SqliteConnection($"Data Source={path};Pooling=False"))
                {
                    conn.Open();
                    using var tx = conn.BeginTransaction();
                    conn.Execute("CREATE TABLE IF NOT EXISTS SchemaInfo (Version INTEGER PRIMARY KEY, AppliedAt DATETIME, Notes TEXT);", transaction: tx);
                    conn.Execute("CREATE TABLE IF NOT EXISTS Sessions (Id INTEGER PRIMARY KEY, ProjectPath TEXT NOT NULL DEFAULT '', Status TEXT NOT NULL DEFAULT 'InProgress');", transaction: tx);
                    conn.Execute("CREATE TABLE IF NOT EXISTS QtoAssignments (Id INTEGER PRIMARY KEY, SessionId INTEGER, UniqueId TEXT, EpCode TEXT);", transaction: tx);
                    conn.Execute("INSERT INTO SchemaInfo (Version, Notes) VALUES (3, 'test');", transaction: tx);
                    tx.Commit();
                }

                int version;
                var init = new DatabaseInitializer(path);
                using (var migratedConn = init.OpenOrCreate())
                {
                    using var versionCmd = migratedConn.CreateCommand();
                    versionCmd.CommandText = "SELECT MAX(Version) FROM SchemaInfo;";
                    version = Convert.ToInt32(versionCmd.ExecuteScalar());
                }
                Assert.Equal(4, version);
            }
            finally { SafeDelete(path); }
        }
    }
}
