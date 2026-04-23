using FluentAssertions;
using Microsoft.Data.Sqlite;
using QtoRevitPlugin.Data;
using System.IO;
using Xunit;

namespace QtoRevitPlugin.Tests.Data
{
    /// <summary>
    /// Regressione 2026-04-23: DB UserLibrary.db creato sotto schema v7
    /// (ComputoChapters senza colonna SoaCategoryId) falliva l'avvio con
    /// <c>SqliteException: no such column: SoaCategoryId</c> perché il DDL v8+
    /// di ComputoChapters include
    /// <c>CREATE INDEX IX_ComputoChapters_Soa ON ComputoChapters(SoaCategoryId)</c>.
    /// L'indice veniva eseguito DENTRO il loop InitialStatements PRIMA che l'ALTER
    /// TABLE che aggiunge la colonna fosse eseguita.
    ///
    /// Fix: il guard difensivo che aggiunge SoaCategoryId via ALTER TABLE è stato
    /// spostato PRIMA del loop InitialStatements nel MigrateIfNeeded.
    /// </summary>
    public class SchemaV7ToV10RegressionTests
    {
        [Fact]
        public void DbWithV7Schema_MigratesToCurrent_WithoutError()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"regr_v7_{System.Guid.NewGuid():N}.db");
            try
            {
                // 1) Creiamo un DB che emula la struttura v7: ComputoChapters senza SoaCategoryId,
                //    nessuna tabella SoaCategories / ComuniItaliani / RevitParamMapping / UserFavorites,
                //    SchemaInfo.Version = 7
                CreateV7Db(dbPath);

                // 2) Aprirlo con la libreria attuale (CurrentVersion = 10) NON deve throw.
                //    Prima del fix, throw: "no such column: SoaCategoryId"
                using var repo = new QtoRepository(dbPath);

                // 3) Verifica: colonna SoaCategoryId è stata aggiunta
                using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA table_info(ComputoChapters);";
                using var reader = cmd.ExecuteReader();
                bool hasSoaCategoryId = false;
                while (reader.Read())
                {
                    if (reader.GetString(1) == "SoaCategoryId") { hasSoaCategoryId = true; break; }
                }
                hasSoaCategoryId.Should().BeTrue(
                    "la migrazione deve aggiungere la colonna SoaCategoryId a ComputoChapters esistente");
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(dbPath)) File.Delete(dbPath);
            }
        }

        [Fact]
        public void DbWithV7Schema_MigratesToCurrent_CreatesSoaCategoriesTable()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"regr_v7_soa_{System.Guid.NewGuid():N}.db");
            try
            {
                CreateV7Db(dbPath);

                using var repo = new QtoRepository(dbPath);

                using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='SoaCategories';";
                var count = (long)cmd.ExecuteScalar()!;
                count.Should().Be(1, "la migrazione deve creare la tabella SoaCategories");
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(dbPath)) File.Delete(dbPath);
            }
        }

        /// <summary>
        /// Crea un DB che emula la struttura schema v7: ComputoChapters senza SoaCategoryId,
        /// SchemaInfo.Version=7, senza SoaCategories/ComuniItaliani/RevitParamMapping/UserFavorites.
        /// Solo le tabelle minime necessarie per non fare crashare la migrazione.
        /// </summary>
        private static void CreateV7Db(string dbPath)
        {
            using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE SchemaInfo (Version INTEGER PRIMARY KEY, AppliedAt DATETIME DEFAULT CURRENT_TIMESTAMP, Notes TEXT);
INSERT INTO SchemaInfo (Version, Notes) VALUES (7, 'Test v7 baseline');

CREATE TABLE Sessions (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ProjectPath TEXT NOT NULL,
    ProjectName TEXT,
    SessionName TEXT,
    Status TEXT NOT NULL DEFAULT 'InProgress',
    ActivePhaseId INTEGER,
    ActivePhaseName TEXT,
    TotalElements INTEGER NOT NULL DEFAULT 0,
    TaggedElements INTEGER NOT NULL DEFAULT 0,
    TotalAmount REAL NOT NULL DEFAULT 0,
    LastEpCode TEXT,
    Notes TEXT,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    LastSavedAt DATETIME,
    ModelSnapshotDate DATETIME,
    LastUsedComputoChapterId INTEGER NULL
);

-- ComputoChapters v7: SENZA SoaCategoryId (è il trigger della regressione)
CREATE TABLE ComputoChapters (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId INTEGER NOT NULL REFERENCES Sessions(Id) ON DELETE CASCADE,
    ParentChapterId INTEGER NULL REFERENCES ComputoChapters(Id) ON DELETE CASCADE,
    Code TEXT NOT NULL,
    Name TEXT NOT NULL,
    Level INTEGER NOT NULL,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    UNIQUE(SessionId, Code)
);
";
            cmd.ExecuteNonQuery();
        }
    }
}
