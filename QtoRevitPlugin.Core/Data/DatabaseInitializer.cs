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

            // Registra versione iniziale
            using (var versionCmd = conn.CreateCommand())
            {
                versionCmd.Transaction = tx;
                versionCmd.CommandText = "INSERT INTO SchemaInfo (Version, Notes) VALUES ($v, $n);";
                versionCmd.Parameters.AddWithValue("$v", DatabaseSchema.CurrentVersion);
                versionCmd.Parameters.AddWithValue("$n", "Schema iniziale Sprint 1");
                versionCmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        private void MigrateIfNeeded(SqliteConnection conn)
        {
            int dbVersion = GetCurrentSchemaVersion(conn);
            if (dbVersion >= DatabaseSchema.CurrentVersion) return;

            // Migrazione v1 → v2 (Sprint 2): aggiunge PriceItems_FTS virtual table.
            // Tutti gli statement in InitialStatements usano IF NOT EXISTS, quindi idempotenti:
            // basta rieseguirli per portare un DB v1 a v2 senza perdita di dati.
            // Se futuri step richiederanno ALTER TABLE o backfill, aggiungere un dispatcher per dbVersion qui.
            using var tx = conn.BeginTransaction();
            foreach (var stmt in DatabaseSchema.InitialStatements)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = stmt;
                cmd.ExecuteNonQuery();
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
