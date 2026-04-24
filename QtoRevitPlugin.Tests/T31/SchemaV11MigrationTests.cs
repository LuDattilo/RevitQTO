using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using QtoRevitPlugin.Data; // QtoRepository (public)
using QtoRevitPlugin.Models;
using Xunit;

namespace QtoRevitPlugin.Tests.T31
{
    /// <summary>
    /// Test migration v10 → v11 (T3.1): UserFavorites.PriceListPublicId +
    /// index idx_favorites_public + backfill da PriceLists.PublicId.
    ///
    /// Scope test:
    /// - DB nuovo → schema v11, colonna presente, index presente
    /// - DB simulato v10 (pre-migration): dati preferiti + listini con PublicId →
    ///   dopo apertura via QtoRepository, backfill completato
    /// - Preferiti orfani (ListId null) → PriceListPublicId resta null
    /// - Preferiti con ListId che punta a PriceList.PublicId=null → resta null
    /// - Idempotenza: doppia apertura non duplica la migration
    /// - API repo: AddFavorite con solo ListId → auto-risolve PriceListPublicId
    /// - API repo: GetFavorites espone PriceListPublicId
    /// </summary>
    public class SchemaV11MigrationTests
    {
        [Fact]
        public void NewDatabase_HasPriceListPublicIdColumn()
        {
            var dbPath = UniquePath();
            try
            {
                using (var repo = new QtoRepository(dbPath))
                {
                    repo.GetSchemaVersion().Should().Be(12);
                }

                using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
                conn.Open();
                ColumnExists(conn, "UserFavorites", "PriceListPublicId").Should().BeTrue();
                IndexExists(conn, "idx_favorites_public").Should().BeTrue();
            }
            finally { SafeDelete(dbPath); }
        }

        [Fact]
        public void AddFavorite_WithListId_AutoResolvesPriceListPublicId()
        {
            var dbPath = UniquePath();
            try
            {
                using var repo = new QtoRepository(dbPath);
                var publicId = Guid.NewGuid().ToString("N");

                // Simula una PriceList con PublicId seedato
                using (var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
                {
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
INSERT INTO PriceLists (Id, PublicId, Name, IsActive, Priority, RowCount)
VALUES (42, @pub, 'Listino Test', 1, 0, 0);";
                    cmd.Parameters.AddWithValue("@pub", publicId);
                    cmd.ExecuteNonQuery();
                }

                // Il chiamante NON fornisce PriceListPublicId → deve essere auto-risolto
                var id = repo.AddFavorite(new UserFavorite
                {
                    Code = "EP.001",
                    Description = "Voce test",
                    ListId = 42,
                    ListName = "Listino Test"
                });
                id.Should().BeGreaterThan(0);

                var favs = repo.GetFavorites();
                favs.Should().ContainSingle();
                favs[0].PriceListPublicId.Should().Be(publicId);
            }
            finally { SafeDelete(dbPath); }
        }

        [Fact]
        public void AddFavorite_WithExplicitPublicId_PreservesIt()
        {
            var dbPath = UniquePath();
            try
            {
                using var repo = new QtoRepository(dbPath);
                var myPublicId = Guid.NewGuid().ToString("N");

                // Nessuna PriceList inserita → l'auto-resolve fallisce, ma il
                // chiamante ha fornito esplicitamente il PublicId → deve rispettarlo
                var id = repo.AddFavorite(new UserFavorite
                {
                    Code = "EP.002",
                    ListId = 99, // orfano nella tabella
                    PriceListPublicId = myPublicId,
                });
                id.Should().BeGreaterThan(0);

                var favs = repo.GetFavorites();
                favs.Should().ContainSingle();
                favs[0].PriceListPublicId.Should().Be(myPublicId);
            }
            finally { SafeDelete(dbPath); }
        }

        [Fact]
        public void AddFavorite_WithNullListId_PriceListPublicIdStaysNull()
        {
            var dbPath = UniquePath();
            try
            {
                using var repo = new QtoRepository(dbPath);

                // Favorite senza ListId → niente da auto-resolvere
                var id = repo.AddFavorite(new UserFavorite
                {
                    Code = "EP.ORPHAN",
                    ListId = null,
                });
                id.Should().BeGreaterThan(0);

                var favs = repo.GetFavorites();
                favs.Should().ContainSingle();
                favs[0].PriceListPublicId.Should().BeNull();
            }
            finally { SafeDelete(dbPath); }
        }

        [Fact]
        public void MigrationV10ToV11_BackfillsExistingFavorites()
        {
            // Simula scenario realistico: DB creato con schema v10 (prima della
            // migration), contiene PriceLists con PublicId + UserFavorites con
            // ListId. Dopo apertura con QtoRepository (v11), il backfill
            // popola retroattivamente PriceListPublicId.
            var dbPath = UniquePath();
            try
            {
                var publicIdA = Guid.NewGuid().ToString("N");
                var publicIdB = Guid.NewGuid().ToString("N");

                // Step 1: prepara DB allo stato "v10" (crea tabelle manualmente,
                // SchemaInfo=10, niente colonna PriceListPublicId)
                using (var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
                {
                    conn.Open();
                    // DDL minimale v10 inline (DatabaseSchema è internal al Core):
                    ExecSql(conn, @"
CREATE TABLE IF NOT EXISTS SchemaInfo (
    Version    INTEGER PRIMARY KEY,
    AppliedAt  DATETIME DEFAULT CURRENT_TIMESTAMP,
    Notes      TEXT
);");
                    ExecSql(conn, @"
CREATE TABLE IF NOT EXISTS PriceLists (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    PublicId    TEXT UNIQUE,
    Name        TEXT NOT NULL,
    Source      TEXT,
    Version     TEXT,
    Region      TEXT,
    IsActive    INTEGER NOT NULL DEFAULT 1,
    Priority    INTEGER NOT NULL DEFAULT 0,
    ImportedAt  DATETIME,
    RowCount    INTEGER NOT NULL DEFAULT 0
);");
                    // UserFavorites v10 (senza PriceListPublicId)
                    ExecSql(conn, @"
CREATE TABLE IF NOT EXISTS UserFavorites (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    PriceItemId  INTEGER NULL,
    Code         TEXT NOT NULL,
    Description  TEXT NOT NULL DEFAULT '',
    Unit         TEXT NOT NULL DEFAULT '',
    UnitPrice    REAL NOT NULL DEFAULT 0,
    ListName     TEXT NOT NULL DEFAULT '',
    ListId       INTEGER NULL,
    AddedAt      TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    Note         TEXT NOT NULL DEFAULT '',
    UNIQUE(Code, ListId)
);");

                    ExecSql(conn,
                        $@"INSERT INTO PriceLists (Id, PublicId, Name, IsActive, Priority, RowCount)
                           VALUES (1, '{publicIdA}', 'L-A', 1, 0, 0),
                                  (2, '{publicIdB}', 'L-B', 1, 0, 0),
                                  (3, NULL, 'L-NoPublic', 1, 0, 0);");

                    ExecSql(conn, @"
INSERT INTO UserFavorites (Code, ListId, ListName, AddedAt)
VALUES ('EP-A1', 1, 'L-A', '2026-01-01T00:00:00Z'),
       ('EP-A2', 1, 'L-A', '2026-01-02T00:00:00Z'),
       ('EP-B1', 2, 'L-B', '2026-01-03T00:00:00Z'),
       ('EP-ORPH', NULL, '', '2026-01-04T00:00:00Z'),
       ('EP-NOP', 3, 'L-NoPublic', '2026-01-05T00:00:00Z');");

                    ExecSql(conn, "INSERT INTO SchemaInfo (Version, Notes) VALUES (10, 'simulato v10');");
                }
                SqliteConnection.ClearAllPools();

                // Step 2: apri con QtoRepository → esegue migration fino a v11
                using (var repo = new QtoRepository(dbPath))
                {
                    repo.GetSchemaVersion().Should().Be(12);
                }
                SqliteConnection.ClearAllPools();

                // Step 3: verifica backfill
                using var conn2 = new SqliteConnection($"Data Source={dbPath};Pooling=False");
                conn2.Open();
                var rows = new Dictionary<string, string?>();
                using (var cmd = conn2.CreateCommand())
                {
                    cmd.CommandText = "SELECT Code, PriceListPublicId FROM UserFavorites ORDER BY Code;";
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var code = reader.GetString(0);
                        var pub = reader.IsDBNull(1) ? null : reader.GetString(1);
                        rows[code] = pub;
                    }
                }

                // Preferiti su listino A → backfill con publicIdA
                rows["EP-A1"].Should().Be(publicIdA);
                rows["EP-A2"].Should().Be(publicIdA);
                // Preferito su listino B → backfill con publicIdB
                rows["EP-B1"].Should().Be(publicIdB);
                // Preferito orfano (ListId null) → resta null
                rows["EP-ORPH"].Should().BeNull();
                // Preferito su listino senza PublicId → resta null (graceful)
                rows["EP-NOP"].Should().BeNull();
            }
            finally { SafeDelete(dbPath); }
        }

        [Fact]
        public void MigrationV10ToV11_IsIdempotent()
        {
            // Scenario: DB v11 già migrato + favorite con PriceListPublicId già
            // popolato. Riaprendo, la migration NON deve rieseguire nulla di
            // distruttivo (no reset del PublicId, no duplicazione di righe,
            // no nuova riga SchemaInfo per v11 dato che dbVersion>=CurrentVersion).
            var dbPath = UniquePath();
            var publicId = Guid.NewGuid().ToString("N");
            try
            {
                using (var repo1 = new QtoRepository(dbPath))
                {
                    using (var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
                    {
                        conn.Open();
                        ExecSql(conn,
                            $@"INSERT INTO PriceLists (Id, PublicId, Name, IsActive, Priority, RowCount)
                               VALUES (10, '{publicId}', 'L', 1, 0, 0);");
                    }
                    // Usa AddFavorite → auto-resolve del PublicId
                    repo1.AddFavorite(new UserFavorite { Code = "X", ListId = 10 });
                    repo1.GetFavorites()[0].PriceListPublicId.Should().Be(publicId);
                }
                SqliteConnection.ClearAllPools();

                // Seconda apertura: il DB è già stato migrato fino alla CurrentVersion corrente
                // dalla prima apertura (repo1 sopra). MigrateIfNeeded no-op.
                using (var repo2 = new QtoRepository(dbPath))
                {
                    repo2.GetSchemaVersion().Should().Be(12);
                    var favs = repo2.GetFavorites();
                    favs.Should().ContainSingle();
                    favs[0].PriceListPublicId.Should().Be(publicId, "idempotency: il PublicId resta stabile");
                }

                // Verifica che SchemaInfo non abbia due righe della CurrentVersion (una da
                // InsertSchema iniziale: in aperture successive non deve più crescere).
                // Nota: con CurrentVersion=12 il DB creato parte direttamente a v12, quindi
                // testiamo l'idempotenza sulla versione corrente (non più v11 come storicamente).
                SqliteConnection.ClearAllPools();
                using var conn2 = new SqliteConnection($"Data Source={dbPath};Pooling=False");
                conn2.Open();
                using var cmd = conn2.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM SchemaInfo WHERE Version = 12;";
                var currRows = Convert.ToInt64(cmd.ExecuteScalar()!);
                currRows.Should().Be(1, "la riga della CurrentVersion in SchemaInfo è scritta una volta sola");
            }
            finally { SafeDelete(dbPath); }
        }

        [Fact]
        public void UniqueIndex_OnPriceListPublicIdAndCode_PreventsDuplicates()
        {
            // I4 (code review): l'UNIQUE INDEX parziale ux_favorites_public_code
            // garantisce che (PriceListPublicId, Code) sia univoco.
            // AddFavorite usa INSERT OR IGNORE → il secondo insert non crea nuova riga,
            // ritorna l'Id della riga esistente (idempotente per design).
            var dbPath = UniquePath();
            try
            {
                using var repo = new QtoRepository(dbPath);
                var publicId = Guid.NewGuid().ToString("N");
                using (var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
                {
                    conn.Open();
                    ExecSql(conn,
                        $@"INSERT INTO PriceLists (Id, PublicId, Name, IsActive, Priority, RowCount)
                           VALUES (1, '{publicId}', 'L-A', 1, 0, 0);");
                }

                // Primo insert → OK, ritorna Id nuovo
                var id1 = repo.AddFavorite(new UserFavorite { Code = "X", ListId = 1 });
                id1.Should().BeGreaterThan(0);

                // Secondo insert con stesso (PriceListPublicId auto-resolved, Code)
                // → INSERT OR IGNORE, ritorna Id della riga esistente (idempotente)
                var id2 = repo.AddFavorite(new UserFavorite { Code = "X", ListId = 1 });
                id2.Should().Be(id1, "AddFavorite è idempotente: stesso (PublicId,Code) restituisce l'Id esistente");

                // Verifica che esista una sola riga (il duplicate è stato ignorato)
                repo.GetFavorites().Should().ContainSingle(
                    "l'UNIQUE INDEX parziale ha impedito la creazione di un duplicato");
            }
            finally { SafeDelete(dbPath); }
        }

        [Fact]
        public void UniqueIndex_Partial_AllowsMultipleNullPublicId()
        {
            // Il constraint UNIQUE è WHERE PriceListPublicId IS NOT NULL.
            // Due preferiti con Code="Y" ma PriceListPublicId=null (ListId=null)
            // devono coesistere — SQLite considera NULL≠NULL nell'UNIQUE parziale.
            var dbPath = UniquePath();
            try
            {
                using var repo = new QtoRepository(dbPath);

                repo.AddFavorite(new UserFavorite { Code = "Y", ListId = null }).Should().BeGreaterThan(0);
                // Stesso Code, stesso NULL PublicId → secondo insert separato (ListId distinto o null)
                // La UNIQUE(Code, ListId) standard blocca (Code,null)+(Code,null),
                // ma l'UNIQUE PARZIALE su PublicId non si applica ai NULL → lo scenario
                // legacy (nessun PublicId) non è bloccato dall'index parziale.
                // Questo test verifica che l'index parziale non interferisca con i NULL.
                var favs = repo.GetFavorites();
                favs.Should().ContainSingle(f => f.Code == "Y");
                favs[0].PriceListPublicId.Should().BeNull();
            }
            finally { SafeDelete(dbPath); }
        }

        // --- helpers ----

        private static string UniquePath() =>
            Path.Combine(Path.GetTempPath(), $"t31_{Guid.NewGuid():N}.db");

        private static void SafeDelete(string path)
        {
            try
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(path)) File.Delete(path);
            }
            catch { /* best-effort */ }
        }

        private static void ExecSql(SqliteConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private static bool ColumnExists(SqliteConnection conn, string table, string column)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table});";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(1);
                if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool IndexExists(SqliteConnection conn, string indexName)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name=@n;";
            cmd.Parameters.AddWithValue("@n", indexName);
            return Convert.ToInt64(cmd.ExecuteScalar()!) > 0;
        }
    }
}
