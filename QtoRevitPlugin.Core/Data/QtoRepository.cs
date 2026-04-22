using Dapper;
using Microsoft.Data.Sqlite;
using QtoRevitPlugin.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace QtoRevitPlugin.Data
{
    /// <summary>
    /// Facciata unica per l'accesso al DB SQLite. Responsabile di aprire la connessione,
    /// gestire il ciclo di vita e esporre i metodi CRUD per ogni entità.
    /// La connessione è keep-alive per la durata della sessione (performance flush &lt; 5ms).
    /// </summary>
    public class QtoRepository : IDisposable
    {
        private readonly SqliteConnection _conn;
        private bool _disposed;

        public QtoRepository(string dbPath)
        {
            var init = new DatabaseInitializer(dbPath);
            _conn = init.OpenOrCreate();
        }

        public string DatabasePath => _conn.DataSource;

        // =====================================================================
        // Sessions
        // =====================================================================

        public int InsertSession(WorkSession session)
        {
            const string sql = @"
                INSERT INTO Sessions
                    (ProjectPath, ProjectName, SessionName, Status,
                     ActivePhaseId, ActivePhaseName, TotalElements, TaggedElements,
                     TotalAmount, LastEpCode, Notes, CreatedAt, LastSavedAt, ModelSnapshotDate)
                VALUES
                    (@ProjectPath, @ProjectName, @SessionName, @Status,
                     @ActivePhaseId, @ActivePhaseName, @TotalElements, @TaggedElements,
                     @TotalAmount, @LastEpCode, @Notes, @CreatedAt, @LastSavedAt, @ModelSnapshotDate);
                SELECT last_insert_rowid();";

            var id = _conn.ExecuteScalar<long>(sql, new
            {
                session.ProjectPath,
                session.ProjectName,
                session.SessionName,
                Status = session.Status.ToString(),
                session.ActivePhaseId,
                session.ActivePhaseName,
                session.TotalElements,
                session.TaggedElements,
                session.TotalAmount,
                session.LastEpCode,
                session.Notes,
                session.CreatedAt,
                session.LastSavedAt,
                session.ModelSnapshotDate
            });

            session.Id = (int)id;
            return session.Id;
        }

        public void UpdateSession(WorkSession session)
        {
            const string sql = @"
                UPDATE Sessions SET
                    SessionName = @SessionName,
                    Status = @Status,
                    ActivePhaseId = @ActivePhaseId,
                    ActivePhaseName = @ActivePhaseName,
                    TotalElements = @TotalElements,
                    TaggedElements = @TaggedElements,
                    TotalAmount = @TotalAmount,
                    LastEpCode = @LastEpCode,
                    Notes = @Notes,
                    LastSavedAt = @LastSavedAt,
                    ModelSnapshotDate = @ModelSnapshotDate
                WHERE Id = @Id;";

            _conn.Execute(sql, new
            {
                session.Id,
                session.SessionName,
                Status = session.Status.ToString(),
                session.ActivePhaseId,
                session.ActivePhaseName,
                session.TotalElements,
                session.TaggedElements,
                session.TotalAmount,
                session.LastEpCode,
                session.Notes,
                session.LastSavedAt,
                session.ModelSnapshotDate
            });
        }

        public WorkSession? GetSession(int id)
        {
            var row = _conn.QueryFirstOrDefault<SessionRow>(
                "SELECT * FROM Sessions WHERE Id = @id;", new { id });
            return row?.ToWorkSession();
        }

        public List<WorkSession> GetSessionsForProject(string projectPath)
        {
            const string sql = @"
                SELECT * FROM Sessions
                WHERE ProjectPath = @projectPath
                ORDER BY LastSavedAt DESC, CreatedAt DESC;";

            return _conn.Query<SessionRow>(sql, new { projectPath })
                        .Select(r => r.ToWorkSession())
                        .ToList();
        }

        /// <summary>Tutte le sessioni nel DB corrente, ordinate dalla più recente.
        /// Usato nel modello file-based (.cme): convenzione 1 file = 1 computo,
        /// se dovessero essercene più di una prendiamo la più recente.</summary>
        public List<WorkSession> GetAllSessions()
        {
            const string sql = @"
                SELECT * FROM Sessions
                ORDER BY LastSavedAt DESC, CreatedAt DESC;";

            return _conn.Query<SessionRow>(sql)
                        .Select(r => r.ToWorkSession())
                        .ToList();
        }

        public void TouchSession(int sessionId)
        {
            _conn.Execute(
                "UPDATE Sessions SET LastSavedAt = @ts WHERE Id = @id;",
                new { id = sessionId, ts = DateTime.UtcNow });
        }

        public int DeleteSession(int sessionId)
        {
            // Cascade cancella assegnazioni, NP, etc. grazie a ON DELETE CASCADE
            return _conn.Execute("DELETE FROM Sessions WHERE Id = @id;", new { id = sessionId });
        }

        // =====================================================================
        // Transazione esposta per operazioni multi-statement
        // =====================================================================

        public SqliteTransaction BeginTransaction() => _conn.BeginTransaction();

        // =====================================================================
        // Schema version (utility per diagnostica)
        // =====================================================================

        public int GetSchemaVersion()
        {
            return _conn.ExecuteScalar<int>("SELECT MAX(Version) FROM SchemaInfo;");
        }

        // =====================================================================
        // IDisposable
        // =====================================================================

        public void Dispose()
        {
            if (_disposed) return;
            _conn.Close();
            _conn.Dispose();
            _disposed = true;
        }

        // =====================================================================
        // Row mappers interni (mappa status Text→Enum)
        // =====================================================================

        private class SessionRow
        {
            public int Id { get; set; }
            public string ProjectPath { get; set; } = string.Empty;
            public string? ProjectName { get; set; }
            public string? SessionName { get; set; }
            public string Status { get; set; } = "InProgress";
            public int ActivePhaseId { get; set; }
            public string? ActivePhaseName { get; set; }
            public int TotalElements { get; set; }
            public int TaggedElements { get; set; }
            public double TotalAmount { get; set; }
            public string? LastEpCode { get; set; }
            public string? Notes { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime? LastSavedAt { get; set; }
            public DateTime? ModelSnapshotDate { get; set; }

            public WorkSession ToWorkSession() => new()
            {
                Id = Id,
                ProjectPath = ProjectPath,
                ProjectName = ProjectName ?? string.Empty,
                SessionName = SessionName ?? string.Empty,
                Status = Enum.TryParse<SessionStatus>(Status, out var s) ? s : SessionStatus.InProgress,
                ActivePhaseId = ActivePhaseId,
                ActivePhaseName = ActivePhaseName ?? string.Empty,
                TotalElements = TotalElements,
                TaggedElements = TaggedElements,
                TotalAmount = TotalAmount,
                LastEpCode = LastEpCode ?? string.Empty,
                Notes = Notes ?? string.Empty,
                CreatedAt = CreatedAt,
                LastSavedAt = LastSavedAt,
                ModelSnapshotDate = ModelSnapshotDate
            };
        }
    }
}
