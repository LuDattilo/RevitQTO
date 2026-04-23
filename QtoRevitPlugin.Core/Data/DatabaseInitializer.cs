using Microsoft.Data.Sqlite;
using System;
using System.IO;

namespace QtoRevitPlugin.Data
{
    /// <summary>
    /// Apre o crea il database SQLite per un dato progetto Revit.
    /// Convenzione: un file .db per ogni .rvt, in %AppData%\QtoPlugin\db\{NomeProgetto}.db
    /// </summary>
    public class DatabaseInitializer
    {
        private readonly string _dbPath;

        public DatabaseInitializer(string dbPath)
        {
            _dbPath = dbPath ?? throw new ArgumentNullException(nameof(dbPath));
        }

        /// <summary>
        /// Path di default per un progetto Revit. Il nome del file deriva dal nome .rvt
        /// (senza estensione). Crea la cartella se non esiste.
        /// </summary>
        public static string GetDefaultDbPath(string projectName)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var folder = Path.Combine(appData, "QtoPlugin", "db");
            Directory.CreateDirectory(folder);
            var safeName = MakeFileSystemSafe(projectName);
            return Path.Combine(folder, $"{safeName}.db");
        }

        /// <summary>Crea o apre il DB. Applica lo schema se nuovo; esegue eventuali migrazioni se esistente.</summary>
        public SqliteConnection OpenOrCreate()
        {
            var isNew = !File.Exists(_dbPath);
            // Pooling=False: una sola connessione per sessione, no pool → Dispose rilascia subito il file handle
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = _dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                ForeignKeys = true,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            }.ToString();

            var conn = new SqliteConnection(connectionString);
            conn.Open();

            if (isNew)
            {
                ApplyInitialSchema(conn);
            }
            else
            {
                MigrateIfNeeded(conn);
            }

            return conn;
        }

        private void ApplyInitialSchema(SqliteConnection conn)
        {
            using var tx = conn.BeginTransaction();
            foreach (var stmt in DatabaseSchema.InitialStatements)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = stmt;
                cmd.ExecuteNonQuery();
            }

            // Sprint 9 v5: indice su QtoAssignments.ComputoChapterId — creato dopo ComputoChapters
            // (la colonna esiste già nel DDL QtoAssignments per nuovi DB)
            ExecuteStatement(conn, tx, DatabaseSchema.MigrateV4ToV5_AddIndexOnComputoChapterId);

            // Sprint 10 step 2 (v8): seed OG/OS per DB nuovi
            SeedSoaCategoriesIfEmpty(conn, tx);

            // Registra versione iniziale
            using (var versionCmd = conn.CreateCommand())
            {
                versionCmd.Transaction = tx;
                versionCmd.CommandText = "INSERT INTO SchemaInfo (Version, Notes) VALUES ($v, $n);";
                versionCmd.Parameters.AddWithValue("$v", DatabaseSchema.CurrentVersion);
                versionCmd.Parameters.AddWithValue("$n", "Schema iniziale Sprint 1-10");
                versionCmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        private void MigrateIfNeeded(SqliteConnection conn)
        {
            int dbVersion = GetCurrentSchemaVersion(conn);
            if (dbVersion >= DatabaseSchema.CurrentVersion) return;

            using var tx = conn.BeginTransaction();

            // --------------------------------------------------------------
            // Pre-migration: garantisce la compatibilità delle COLONNE a cui
            // i CREATE INDEX dentro InitialStatements fanno riferimento.
            //
            // Senza questo guard, uno scenario realistico rompe il riavvio:
            //   - DB creato sotto schema v7 (ComputoChapters senza SoaCategoryId)
            //   - App aggiornata a v8+ (DDL di ComputoChapters ora include
            //     CREATE INDEX IX_ComputoChapters_Soa ON ComputoChapters(SoaCategoryId))
            //   - Al riavvio: CREATE TABLE IF NOT EXISTS è no-op (tabella esiste)
            //     ma CREATE INDEX ... ON tab(colonna_mancante) FAIL con
            //     "no such column: SoaCategoryId" → plugin crash all'avvio.
            //
            // La fix è eseguire le ALTER TABLE "add column" PRIMA di rieseguire
            // gli InitialStatements, così le colonne esistono quando gli indici
            // vengono (ri)creati.
            // --------------------------------------------------------------
            if (TableExists(conn, tx, "ComputoChapters") && !ColumnExists(conn, tx, "ComputoChapters", "SoaCategoryId"))
            {
                ExecuteStatement(conn, tx, DatabaseSchema.MigrateV7ToV8_AddSoaCategoryIdToChapters);
            }

            // Migrazione v1 → v2 (Sprint 2): aggiunge PriceItems_FTS virtual table.
            // I CREATE TABLE/VIRTUAL TABLE sono idempotenti (IF NOT EXISTS), basta rieseguirli.
            foreach (var stmt in DatabaseSchema.InitialStatements)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = stmt;
                cmd.ExecuteNonQuery();
            }

            // Migrazione v2 → v3 (Sprint 4): aggiunge colonna PriceLists.PublicId per
            // riferimenti portabili nel DataStorage del .rvt. ALTER TABLE non supporta
            // IF NOT EXISTS → check preventivo via PRAGMA.
            if (dbVersion < 3 && !ColumnExists(conn, tx, "PriceLists", "PublicId"))
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.Transaction = tx;
                alterCmd.CommandText = DatabaseSchema.MigrateV2ToV3_AddPublicId;
                alterCmd.ExecuteNonQuery();
            }

            // Le colonne audit Sprint 6 (v4) vengono verificate e applicate in modo
            // DIFENSIVO (indipendentemente da dbVersion) — alcuni DB hanno SchemaInfo
            // incongruente con la struttura reale (es. v4 dichiarato ma tabella
            // ancora pre-v4). Senza questo guard, la migration v5→v6 fallisce perché
            // il SELECT/INSERT riferisce CreatedBy che non esiste nel backup.
            if (TableExists(conn, tx, "QtoAssignments"))
            {
                if (!ColumnExists(conn, tx, "QtoAssignments", "CreatedBy"))
                    ExecuteStatement(conn, tx, DatabaseSchema.MigrateV3ToV4_CreatedBy);
                if (!ColumnExists(conn, tx, "QtoAssignments", "CreatedAt"))
                    ExecuteStatement(conn, tx, DatabaseSchema.MigrateV3ToV4_CreatedAt);
                if (!ColumnExists(conn, tx, "QtoAssignments", "ModifiedBy"))
                    ExecuteStatement(conn, tx, DatabaseSchema.MigrateV3ToV4_ModifiedBy);
                if (!ColumnExists(conn, tx, "QtoAssignments", "Version"))
                    ExecuteStatement(conn, tx, DatabaseSchema.MigrateV3ToV4_Version);
                if (!ColumnExists(conn, tx, "QtoAssignments", "AuditStatus"))
                    ExecuteStatement(conn, tx, DatabaseSchema.MigrateV3ToV4_AuditStatus);
            }

            // Colonne v5 (Sprint 9): guard difensivo indipendente da dbVersion —
            // stesso motivo del blocco v4 sopra (SchemaInfo può essere incongruente).
            if (TableExists(conn, tx, "QtoAssignments") && !ColumnExists(conn, tx, "QtoAssignments", "ComputoChapterId"))
            {
                ExecuteStatement(conn, tx, DatabaseSchema.MigrateV4ToV5_AddComputoChapterIdToAssignments);
            }
            // Indice idempotente via IF NOT EXISTS — safe to run always
            if (TableExists(conn, tx, "QtoAssignments"))
            {
                ExecuteStatement(conn, tx, DatabaseSchema.MigrateV4ToV5_AddIndexOnComputoChapterId);
            }
            if (TableExists(conn, tx, "Sessions") && !ColumnExists(conn, tx, "Sessions", "LastUsedComputoChapterId"))
            {
                ExecuteStatement(conn, tx, DatabaseSchema.MigrateV4ToV5_AddLastUsedChapterToSessions);
            }

            if (dbVersion < 6)
            {
                // v6: aggiorna UNIQUE constraint su QtoAssignments da (SessionId, UniqueId, EpCode)
                //     a (SessionId, UniqueId, EpCode, Version) per supportare il pattern Supersede.
                // SQLite non supporta DROP CONSTRAINT → ricreazione tabella.
                // Guard: solo se la tabella esiste ed è la versione completa (ha ElementId).
                // DB minimali (test v3) non hanno ElementId → skip; la v4 migration avrà già creato
                // le colonne mancanti tramite ALTER TABLE prima di arrivare qui.
                if (TableExists(conn, tx, "QtoAssignments") && ColumnExists(conn, tx, "QtoAssignments", "ElementId"))
                {
                    ExecuteStatement(conn, tx, "PRAGMA foreign_keys = OFF;");
                    ExecuteStatement(conn, tx, DatabaseSchema.MigrateV5ToV6_RenameQtoAssignments);
                    ExecuteStatement(conn, tx, DatabaseSchema.MigrateV5ToV6_CreateQtoAssignments);
                    ExecuteStatement(conn, tx, DatabaseSchema.MigrateV5ToV6_CopyData);
                    ExecuteStatement(conn, tx, DatabaseSchema.MigrateV5ToV6_DropBackup);
                    ExecuteStatement(conn, tx, DatabaseSchema.MigrateV5ToV6_RecreateIndexes);
                    ExecuteStatement(conn, tx, "PRAGMA foreign_keys = ON;");
                }
                // Se QtoAssignments non esiste o è minimale (test), non serve ricrearla:
                // il vincolo corretto sarà applicato quando la tabella verrà creata tramite
                // InitialStatements (già eseguito nel loop v<5 sopra).
            }

            // Colonna SoaCategoryId (v8) — guard difensivo: applicabile solo se la
            // tabella ComputoChapters esiste e non ha già la colonna.
            if (TableExists(conn, tx, "ComputoChapters") && !ColumnExists(conn, tx, "ComputoChapters", "SoaCategoryId"))
            {
                ExecuteStatement(conn, tx, DatabaseSchema.MigrateV7ToV8_AddSoaCategoryIdToChapters);
            }
            if (TableExists(conn, tx, "ComputoChapters"))
            {
                ExecuteStatement(conn, tx, DatabaseSchema.MigrateV7ToV8_AddIndexOnSoaCategoryId);
            }

            // Seed SoaCategories se la tabella è vuota (prima volta a v8)
            SeedSoaCategoriesIfEmpty(conn, tx);

            if (dbVersion < 9)
            {
                // v8→v9: comuni_italiani (popolata solo in UserLibrary) + RevitParamMapping (solo .cme)
                ExecuteStatement(conn, tx, DatabaseSchema.MigrateV8ToV9_CreateComuniItaliani);
                ExecuteStatement(conn, tx, DatabaseSchema.MigrateV8ToV9_IndexComuniProv);
                ExecuteStatement(conn, tx, DatabaseSchema.MigrateV8ToV9_IndexComuniNome);
                ExecuteStatement(conn, tx, DatabaseSchema.MigrateV8ToV9_CreateRevitParamMapping);
            }

            if (dbVersion < 10)
            {
                // v9→v10: UserFavorites (popolata solo in UserLibrary).
                ExecuteStatement(conn, tx, DatabaseSchema.MigrateV9ToV10_CreateUserFavorites);
                ExecuteStatement(conn, tx, DatabaseSchema.MigrateV9ToV10_IndexFavoritesCode);
            }

            using (var insert = conn.CreateCommand())
            {
                insert.Transaction = tx;
                insert.CommandText = "INSERT INTO SchemaInfo (Version, Notes) VALUES ($v, $n);";
                insert.Parameters.AddWithValue("$v", DatabaseSchema.CurrentVersion);
                insert.Parameters.AddWithValue("$n", $"Migrato da v{dbVersion}");
                insert.ExecuteNonQuery();
            }

            tx.Commit();
        }

        /// <summary>
        /// Seed one-shot della tabella SoaCategories con i codici normativi OG/OS
        /// D.Lgs. 36/2023 All. II.12. Idempotente: se la tabella è già popolata
        /// (es. upgrade già avvenuto in altro avvio), no-op.
        /// </summary>
        private static void SeedSoaCategoriesIfEmpty(SqliteConnection conn, SqliteTransaction tx)
        {
            if (!TableExists(conn, tx, "SoaCategories")) return;

            using (var countCmd = conn.CreateCommand())
            {
                countCmd.Transaction = tx;
                countCmd.CommandText = "SELECT COUNT(*) FROM SoaCategories;";
                var count = Convert.ToInt64(countCmd.ExecuteScalar()!);
                if (count > 0) return;
            }

            foreach (var soa in QtoRevitPlugin.Models.SoaCategorySeed.All)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO SoaCategories (Code, Description, Type, SortOrder) VALUES ($c, $d, $t, $s);";
                cmd.Parameters.AddWithValue("$c", soa.Code);
                cmd.Parameters.AddWithValue("$d", soa.Description);
                cmd.Parameters.AddWithValue("$t", soa.Type);
                cmd.Parameters.AddWithValue("$s", soa.SortOrder);
                cmd.ExecuteNonQuery();
            }
        }

        private static void ExecuteStatement(SqliteConnection conn, SqliteTransaction tx, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private static bool TableExists(SqliteConnection conn, SqliteTransaction tx, string table)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$t;";
            cmd.Parameters.AddWithValue("$t", table);
            return Convert.ToInt64(cmd.ExecuteScalar()!) > 0;
        }

        /// <summary>
        /// Check idempotente tramite PRAGMA table_info: ritorna true se la colonna esiste.
        /// Usato per gating di ALTER TABLE ADD COLUMN (che altrimenti fallirebbe su retry).
        /// </summary>
        private static bool ColumnExists(SqliteConnection conn, SqliteTransaction tx, string table, string column)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"PRAGMA table_info({table});";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                // Campo "name" è l'indice 1 nel PRAGMA table_info
                var name = reader.GetString(1);
                if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static int GetCurrentSchemaVersion(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT MAX(Version) FROM SchemaInfo;";
            try
            {
                var result = cmd.ExecuteScalar();
                return result is null or DBNull ? 0 : Convert.ToInt32(result);
            }
            catch (SqliteException)
            {
                // Tabella non esiste → DB pre-schema
                return 0;
            }
        }

        private static string MakeFileSystemSafe(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "default";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = new char[name.Length];
            for (int i = 0; i < name.Length; i++)
            {
                chars[i] = Array.IndexOf(invalid, name[i]) >= 0 ? '_' : name[i];
            }
            return new string(chars).Trim();
        }
    }
}
