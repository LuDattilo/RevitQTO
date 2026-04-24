using System;
using System.IO;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models.Computi;
using Xunit;

namespace QtoRevitPlugin.Tests.Computi
{
    /// <summary>
    /// Plan C-0: verifica che la migrazione schema v11→v12 crei correttamente le 7
    /// tabelle del modulo Computi + estenda PriceItems con 17 colonne XPWE-style.
    /// Usa pattern di SchemaV11MigrationTests: QtoRepository ctor = full init.
    /// </summary>
    public class SchemaV12MigrationTests
    {
        private static string UniquePath() =>
            Path.Combine(Path.GetTempPath(), $"qto_c0_test_{Guid.NewGuid():N}.db");

        private static void SafeDelete(string path)
        {
            SqliteConnection.ClearAllPools();
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
        }

        [Fact]
        public void FreshDb_CreatesAllComputiTables()
        {
            var dbPath = UniquePath();
            try
            {
                using var repo = new QtoRepository(dbPath);
                using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
                conn.Open();

                AssertTableExists(conn, "ComputoDocuments");
                AssertTableExists(conn, "ChapterNodes");
                AssertTableExists(conn, "CategoryNodes");
                AssertTableExists(conn, "WbsNodes");
                AssertTableExists(conn, "MeasurementRows");
                AssertTableExists(conn, "MeasurementSubRows");
                AssertTableExists(conn, "XpweExportJobs");
            }
            finally { SafeDelete(dbPath); }
        }

        [Fact]
        public void FreshDb_ExtendsPriceItemsWithXpweColumns()
        {
            var dbPath = UniquePath();
            try
            {
                using var repo = new QtoRepository(dbPath);
                using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
                conn.Open();

                AssertColumnExists(conn, "PriceItems", "Prezzo2");
                AssertColumnExists(conn, "PriceItems", "Prezzo3");
                AssertColumnExists(conn, "PriceItems", "Prezzo4");
                AssertColumnExists(conn, "PriceItems", "Prezzo5");
                AssertColumnExists(conn, "PriceItems", "SpCapId");
                AssertColumnExists(conn, "PriceItems", "CapId");
                AssertColumnExists(conn, "PriceItems", "SbCapId");
                AssertColumnExists(conn, "PriceItems", "WbsCapNodeId");
                AssertColumnExists(conn, "PriceItems", "IncMDO");
                AssertColumnExists(conn, "PriceItems", "IncMAT");
                AssertColumnExists(conn, "PriceItems", "IncSIC");
                AssertColumnExists(conn, "PriceItems", "Flags");
                AssertColumnExists(conn, "PriceItems", "CnfQt");
                AssertColumnExists(conn, "PriceItems", "AdrInternet");
                AssertColumnExists(conn, "PriceItems", "TipoRisorsa");
                AssertColumnExists(conn, "PriceItems", "Articolo");
                AssertColumnExists(conn, "PriceItems", "DataEP");
            }
            finally { SafeDelete(dbPath); }
        }

        [Fact]
        public void ComputoDocument_Roundtrip()
        {
            var dbPath = UniquePath();
            try
            {
                using var repo = new QtoRepository(dbPath);
                var sessId = repo.InsertSession(new QtoRevitPlugin.Models.WorkSession
                {
                    ProjectPath = "test.rvt",
                    SessionName = "Test CME",
                    CreatedAt = DateTime.UtcNow,
                    LastSavedAt = DateTime.UtcNow
                });

                var doc = new ComputoDocument
                {
                    WorkSessionId = sessId,
                    TipoDocumento = 1,
                    Oggetto = "Progetto test",
                    Comune = "Firenze",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                var id = repo.InsertComputoDocument(doc);
                id.Should().BeGreaterThan(0);

                var loaded = repo.GetComputoDocumentBySession(sessId);
                loaded.Should().NotBeNull();
                loaded!.Oggetto.Should().Be("Progetto test");
                loaded.Comune.Should().Be("Firenze");
                loaded.TipoDocumento.Should().Be(1);
                loaded.Versione.Should().Be("5.04");  // default
                loaded.Currency.Should().Be("EUR");
            }
            finally { SafeDelete(dbPath); }
        }

        [Fact]
        public void ChapterNode_CrudWithHierarchy()
        {
            var dbPath = UniquePath();
            try
            {
                using var repo = new QtoRepository(dbPath);
                var sessId = repo.InsertSession(new QtoRevitPlugin.Models.WorkSession
                {
                    ProjectPath = "t.rvt", SessionName = "T",
                    CreatedAt = DateTime.UtcNow, LastSavedAt = DateTime.UtcNow
                });
                var docId = repo.InsertComputoDocument(new ComputoDocument
                {
                    WorkSessionId = sessId, TipoDocumento = 1,
                    CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
                });

                var sp = new ChapterNode
                {
                    DocumentId = docId, Level = "SpCap",
                    Codice = "01", DesSintetica = "Demolizioni", SortOrder = 1
                };
                var spId = repo.InsertChapterNode(sp);
                var cap = new ChapterNode
                {
                    DocumentId = docId, Level = "Cap",
                    Codice = "01.01", DesSintetica = "Murature",
                    ParentId = spId, SortOrder = 1
                };
                repo.InsertChapterNode(cap);

                var all = repo.GetChapterNodes(docId);
                all.Should().HaveCount(2);
                all[0].Codice.Should().Be("01");
                all[1].ParentId.Should().Be(spId);
            }
            finally { SafeDelete(dbPath); }
        }

        [Fact]
        public void MeasurementRow_RecalcQuantitaFromSubRows()
        {
            var dbPath = UniquePath();
            try
            {
                using var repo = new QtoRepository(dbPath);
                var sessId = repo.InsertSession(new QtoRevitPlugin.Models.WorkSession
                {
                    ProjectPath = "t.rvt", SessionName = "T",
                    CreatedAt = DateTime.UtcNow, LastSavedAt = DateTime.UtcNow
                });
                var docId = repo.InsertComputoDocument(new ComputoDocument
                {
                    WorkSessionId = sessId, TipoDocumento = 1,
                    CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
                });

                // PriceList + PriceItem minimi (PriceListId è NOT NULL).
                using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO PriceLists (Name, IsActive, Priority, RowCount)
                                        VALUES ('TestList', 1, 0, 0);";
                    cmd.ExecuteNonQuery();
                }
                int listId;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT last_insert_rowid();";
                    listId = Convert.ToInt32(cmd.ExecuteScalar());
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO PriceItems (PriceListId, Code, Description, Unit, UnitPrice)
                                        VALUES (@lid, 'TEST-01', 'test', 'mc', 100.0);";
                    cmd.Parameters.AddWithValue("@lid", listId);
                    cmd.ExecuteNonQuery();
                }
                int piId;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT last_insert_rowid();";
                    piId = Convert.ToInt32(cmd.ExecuteScalar());
                }
                SqliteConnection.ClearAllPools();

                var rowId = repo.InsertMeasurementRow(new MeasurementRow
                {
                    DocumentId = docId, PriceItemId = piId, SortOrder = 1
                });
                repo.InsertMeasurementSubRow(new MeasurementSubRow
                {
                    MeasurementRowId = rowId, IDVV = 1,
                    PartiUguali = 2, Lunghezza = 3, Larghezza = 4,
                    Quantita = 24.0, SortOrder = 1
                });
                repo.InsertMeasurementSubRow(new MeasurementSubRow
                {
                    MeasurementRowId = rowId, IDVV = 2,
                    PartiUguali = 1, Lunghezza = 5,
                    Quantita = 5.0, SortOrder = 2
                });

                repo.RecalcMeasurementRowQuantita(rowId);
                var rows = repo.GetMeasurementRows(docId);
                rows.Should().ContainSingle();
                rows[0].Quantita.Should().BeApproximately(29.0, 0.001);
            }
            finally { SafeDelete(dbPath); }
        }

        private static void AssertTableExists(SqliteConnection conn, string table)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT name FROM sqlite_master WHERE type='table' AND name='{table}';";
            var result = cmd.ExecuteScalar();
            Assert.True(result != null, $"Tabella '{table}' non trovata");
            Assert.Equal(table, result);
        }

        private static void AssertColumnExists(SqliteConnection conn, string table, string col)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table});";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (reader.GetString(1) == col) return;
            }
            Assert.Fail($"Colonna {table}.{col} non trovata");
        }
    }
}
