using FluentAssertions;
using Microsoft.Data.Sqlite;
using QtoRevitPlugin.Data;
using System.IO;
using Xunit;

namespace QtoRevitPlugin.Tests.Sprint9
{
    public class SchemaV5MigrationTests
    {
        [Fact]
        public void NewDb_HasComputoChaptersTable()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"test_v5_{System.Guid.NewGuid()}.db");
            try
            {
                using var repo = new QtoRepository(dbPath);
                using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='ComputoChapters'";
                var result = cmd.ExecuteScalar();
                result.Should().Be("ComputoChapters");
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(dbPath)) File.Delete(dbPath);
            }
        }

        [Fact]
        public void NewDb_QtoAssignments_HasComputoChapterIdColumn()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"test_v5_col_{System.Guid.NewGuid()}.db");
            try
            {
                using var repo = new QtoRepository(dbPath);
                using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA table_info(QtoAssignments);";
                using var reader = cmd.ExecuteReader();
                bool found = false;
                while (reader.Read())
                {
                    if (reader.GetString(1) == "ComputoChapterId") { found = true; break; }
                }
                found.Should().BeTrue("QtoAssignments must have ComputoChapterId column after v5 migration");
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(dbPath)) File.Delete(dbPath);
            }
        }

        [Fact]
        public void ExistingV4Db_MigratesTo_V5_PreservingData()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"test_v4_to_v5_{System.Guid.NewGuid()}.db");
            try
            {
                // Simula un DB v4 creato manualmente (senza ComputoChapters)
                using (var conn = new SqliteConnection($"Data Source={dbPath}"))
                {
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        CREATE TABLE SchemaInfo (Version INTEGER PRIMARY KEY, AppliedAt DATETIME, Notes TEXT);
                        INSERT INTO SchemaInfo (Version, Notes) VALUES (4, 'v4 iniziale test');
                        CREATE TABLE Sessions (Id INTEGER PRIMARY KEY AUTOINCREMENT, ProjectPath TEXT NOT NULL, Status TEXT NOT NULL DEFAULT 'InProgress');
                        INSERT INTO Sessions (ProjectPath, Status) VALUES ('test.rvt', 'InProgress');";
                    cmd.ExecuteNonQuery();
                }
                SqliteConnection.ClearAllPools();

                // Apri con QtoRepository → deve migrare fino a v10 (v5 + v6 + v7 + v8 + v9 + v10 in sequenza)
                using (var repo = new QtoRepository(dbPath))
                {
                    repo.GetSchemaVersion().Should().Be(10);
                }
                SqliteConnection.ClearAllPools();

                // Dato originale preservato
                using var conn2 = new SqliteConnection($"Data Source={dbPath};Pooling=False");
                conn2.Open();
                using var cmd2 = conn2.CreateCommand();
                cmd2.CommandText = "SELECT COUNT(*) FROM Sessions WHERE ProjectPath='test.rvt'";
                ((long)cmd2.ExecuteScalar()!).Should().Be(1);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(dbPath)) File.Delete(dbPath);
            }
        }
    }
}
