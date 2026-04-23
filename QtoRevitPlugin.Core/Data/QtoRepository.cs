using Dapper;
using Microsoft.Data.Sqlite;
using QtoRevitPlugin.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace QtoRevitPlugin.Data
{
    /// <summary>
    /// Facciata unica per l'accesso al DB SQLite. Responsabile di aprire la connessione,
    /// gestire il ciclo di vita e esporre i metodi CRUD per ogni entità.
    /// La connessione è keep-alive per la durata della sessione (performance flush &lt; 5ms).
    /// </summary>
    public class QtoRepository : IQtoRepository, IDisposable
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
                     TotalAmount, LastEpCode, Notes, CreatedAt, LastSavedAt, ModelSnapshotDate,
                     LastUsedComputoChapterId)
                VALUES
                    (@ProjectPath, @ProjectName, @SessionName, @Status,
                     @ActivePhaseId, @ActivePhaseName, @TotalElements, @TaggedElements,
                     @TotalAmount, @LastEpCode, @Notes, @CreatedAt, @LastSavedAt, @ModelSnapshotDate,
                     @LastUsedComputoChapterId);
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
                session.ModelSnapshotDate,
                session.LastUsedComputoChapterId
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
                    ModelSnapshotDate = @ModelSnapshotDate,
                    LastUsedComputoChapterId = @LastUsedComputoChapterId
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
                session.ModelSnapshotDate,
                session.LastUsedComputoChapterId
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
        // Listini (PriceLists + PriceItems + FTS5)
        // =====================================================================

        /// <summary>
        /// Inserisce un nuovo listino (PriceLists), ritorna l'Id auto-increment generato.
        /// Se <see cref="PriceList.PublicId"/> è vuoto, viene generato un GUID stabile (usato
        /// dal ProjectPriceListSnapshot nel .rvt per riferimenti portabili cross-PC).
        /// </summary>
        public int InsertPriceList(PriceList list)
        {
            if (string.IsNullOrWhiteSpace(list.PublicId))
                list.PublicId = Guid.NewGuid().ToString("D");

            const string sql = @"
                INSERT INTO PriceLists
                    (PublicId, Name, Source, Version, Region, IsActive, Priority, ImportedAt, RowCount)
                VALUES
                    (@PublicId, @Name, @Source, @Version, @Region, @IsActive, @Priority, @ImportedAt, @RowCount);
                SELECT last_insert_rowid();";

            var id = _conn.ExecuteScalar<long>(sql, new
            {
                list.PublicId,
                list.Name,
                list.Source,
                list.Version,
                list.Region,
                IsActive = list.IsActive ? 1 : 0,
                list.Priority,
                ImportedAt = list.ImportedAt == default ? (DateTime?)null : list.ImportedAt,
                list.RowCount
            });

            list.Id = (int)id;
            return list.Id;
        }

        /// <summary>
        /// Inserimento batch di voci in transazione. Al completamento dell'ultimo batch esegue RebuildPriceItemsFts.
        /// Ritorna il numero totale di voci inserite. Usa INSERT OR IGNORE per duplicati (PriceListId, Code).
        /// </summary>
        public int InsertPriceItemsBatch(int priceListId, IEnumerable<PriceItem> items, int batchSize = 500)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (batchSize <= 0) batchSize = 500;

            const string sql = @"
                INSERT OR IGNORE INTO PriceItems
                    (PriceListId, Code, SuperChapter, Chapter, SubChapter,
                     Description, ShortDesc, Unit, UnitPrice, Notes, IsNP)
                VALUES
                    (@PriceListId, @Code, @SuperChapter, @Chapter, @SubChapter,
                     @Description, @ShortDesc, @Unit, @UnitPrice, @Notes, @IsNP);";

            int totalInserted = 0;
            using var tx = _conn.BeginTransaction();

            var buffer = new List<object>(batchSize);
            foreach (var it in items)
            {
                buffer.Add(new
                {
                    PriceListId = priceListId,
                    it.Code,
                    it.SuperChapter,
                    it.Chapter,
                    it.SubChapter,
                    it.Description,
                    it.ShortDesc,
                    it.Unit,
                    it.UnitPrice,
                    it.Notes,
                    IsNP = it.IsNP ? 1 : 0
                });

                if (buffer.Count >= batchSize)
                {
                    totalInserted += _conn.Execute(sql, buffer, tx);
                    buffer.Clear();
                }
            }

            // Flush ultimo chunk
            if (buffer.Count > 0)
            {
                totalInserted += _conn.Execute(sql, buffer, tx);
                buffer.Clear();
            }

            // Aggiorna metadati listino (RowCount = somma di quanto presente, non solo inserito ora)
            const string updateListSql = @"
                UPDATE PriceLists
                SET RowCount = (SELECT COUNT(*) FROM PriceItems WHERE PriceListId = @pid),
                    ImportedAt = @ts
                WHERE Id = @pid;";
            _conn.Execute(updateListSql, new { pid = priceListId, ts = DateTime.UtcNow }, tx);

            tx.Commit();

            // Rebuild FTS fuori dalla transazione principale: 'rebuild' è un comando meta su virtual table
            RebuildPriceItemsFts();

            return totalInserted;
        }

        /// <summary>
        /// Rebuild esplicito dell'indice FTS5 su PriceItems_FTS.
        /// Chiamata automaticamente da InsertPriceItemsBatch a fine import.
        /// Può essere chiamata anche manualmente (es. dopo restore DB).
        /// </summary>
        public void RebuildPriceItemsFts()
        {
            _conn.Execute("INSERT INTO PriceItems_FTS(PriceItems_FTS) VALUES('rebuild');");
        }

        /// <summary>
        /// Elimina definitivamente un listino e tutte le sue voci (ON DELETE CASCADE su PriceItems).
        /// Rebuild FTS necessario dopo (invocato automaticamente).
        /// </summary>
        public void DeletePriceList(int priceListId)
        {
            _conn.Execute("DELETE FROM PriceLists WHERE Id = @id;", new { id = priceListId });
            RebuildPriceItemsFts();
        }

        /// <summary>Aggiorna IsActive/Priority di un listino (soft-toggle senza rimuovere dati).</summary>
        public void UpdatePriceListFlags(int priceListId, bool isActive, int priority)
        {
            _conn.Execute(@"
                UPDATE PriceLists
                SET IsActive = @isActive, Priority = @priority
                WHERE Id = @id;",
                new { id = priceListId, isActive = isActive ? 1 : 0, priority });
        }

        /// <summary>Ritorna tutti i listini (attivi e non), ordinati per Priority ascendente.</summary>
        public IReadOnlyList<PriceList> GetPriceLists()
        {
            const string sql = @"
                SELECT Id, PublicId, Name, Source, Version, Region,
                       IsActive, Priority, ImportedAt, RowCount
                FROM PriceLists
                ORDER BY Priority ASC, Name ASC;";

            return _conn.Query<PriceListRow>(sql)
                        .Select(r => r.ToPriceList())
                        .ToList();
        }

        /// <summary>
        /// Livello 1 ricerca: match esatto (case-insensitive) per Code nei listini attivi.
        /// </summary>
        public IReadOnlyList<PriceItem> FindByCodeExact(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return Array.Empty<PriceItem>();

            const string sql = @"
                SELECT p.*, pl.Name AS ListName
                FROM PriceItems p
                JOIN PriceLists pl ON pl.Id = p.PriceListId
                WHERE LOWER(p.Code) = LOWER(@code) AND pl.IsActive = 1
                ORDER BY pl.Priority ASC;";

            return _conn.Query<PriceItemRow>(sql, new { code })
                        .Select(r => r.ToPriceItem())
                        .ToList();
        }

        /// <summary>
        /// Livello 2 ricerca: FTS5 MATCH su Description + ShortDesc + Chapter.
        /// Query sanitizzata per evitare errori FTS5 syntax (rimuovi caratteri speciali).
        /// Limit di default 50 risultati.
        /// </summary>
        public IReadOnlyList<PriceItem> SearchFts(string query, int limit = 50)
        {
            var ftsQuery = BuildFtsQuery(query);
            if (string.IsNullOrEmpty(ftsQuery))
                return Array.Empty<PriceItem>();

            // FTS5 richiede il nome letterale della virtual table nell'operator MATCH
            // (non è ammesso l'alias). L'alias funziona invece per rowid/rank.
            const string sql = @"
                SELECT p.*, pl.Name AS ListName
                FROM PriceItems_FTS
                JOIN PriceItems  p  ON p.Id = PriceItems_FTS.rowid
                JOIN PriceLists  pl ON pl.Id = p.PriceListId
                WHERE PriceItems_FTS MATCH @query AND pl.IsActive = 1
                ORDER BY rank
                LIMIT @limit;";

            return _conn.Query<PriceItemRow>(sql, new { query = ftsQuery, limit })
                        .Select(r => r.ToPriceItem())
                        .ToList();
        }

        /// <summary>
        /// Carica tutte le voci di UN listino specifico, ordinate per gerarchia
        /// (SuperChapter → Chapter → SubChapter → Code). Usato dal CatalogBrowserWindow
        /// per costruire il TreeView di anteprima.
        /// </summary>
        public IReadOnlyList<PriceItem> GetPriceItemsByList(int priceListId)
        {
            const string sql = @"
                SELECT p.*, pl.Name AS ListName
                FROM PriceItems p
                JOIN PriceLists pl ON pl.Id = p.PriceListId
                WHERE p.PriceListId = @id
                ORDER BY p.SuperChapter, p.Chapter, p.SubChapter, p.Code;";

            return _conn.Query<PriceItemRow>(sql, new { id = priceListId })
                        .Select(r => r.ToPriceItem())
                        .ToList();
        }

        /// <summary>
        /// Carica tutte le voci appartenenti a listini attivi. Usato dal
        /// <c>PriceItemSearchService</c> per la ricerca fuzzy (livello 3 Levenshtein) come cache one-shot.
        /// Per listini standard (&lt; 30k voci) è un'operazione &lt; 50ms.
        /// </summary>
        public IReadOnlyList<PriceItem> GetAllActivePriceItems()
        {
            const string sql = @"
                SELECT p.*, pl.Name AS ListName
                FROM PriceItems p
                JOIN PriceLists pl ON pl.Id = p.PriceListId
                WHERE pl.IsActive = 1
                ORDER BY pl.Priority ASC, p.Code ASC;";

            return _conn.Query<PriceItemRow>(sql)
                        .Select(r => r.ToPriceItem())
                        .ToList();
        }

        /// <summary>
        /// Sanitizza la query utente e la converte in sintassi FTS5 prefix-match per ogni token.
        /// Rimuove caratteri problematici ("*()^-) e produce 'word1* word2*' (AND implicito).
        /// Ritorna stringa vuota se non restano token validi.
        /// </summary>
        private static string BuildFtsQuery(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            // A questo punto raw è non-null (IsNullOrWhiteSpace ha già filtrato); netstandard2.0
            // non ha [NotNullWhen] su quell'overload, quindi disambiguo esplicitamente.
            var src = raw!;

            // Stripper caratteri FTS5 problematici: virgolette, star, parentesi, caret, trattino, colon
            var cleaned = new StringBuilder(src.Length);
            foreach (var ch in src)
            {
                if (ch == '"' || ch == '*' || ch == '(' || ch == ')' ||
                    ch == '^' || ch == '-' || ch == ':')
                {
                    cleaned.Append(' ');
                }
                else
                {
                    cleaned.Append(ch);
                }
            }

            var tokens = cleaned.ToString()
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length > 0)
                .ToArray();

            if (tokens.Length == 0) return string.Empty;

            // Ogni token → prefix match; AND implicito fra token in FTS5
            return string.Join(" ", tokens.Select(t => t + "*"));
        }

        // =====================================================================
        // QtoAssignments
        // =====================================================================

        public int InsertAssignment(QtoAssignment assignment) => InsertAssignment(assignment, null);

        public int InsertAssignment(QtoAssignment assignment, IDbTransaction? tx)
        {
            const string sql = @"
                INSERT INTO QtoAssignments
                    (SessionId, ElementId, UniqueId, Category, FamilyName, PhaseCreated, PhaseDemolished,
                     EpCode, EpDescription, Quantity, QuantityGross, QuantityDeducted, Unit, UnitPrice,
                     RuleApplied, Source, AssignedAt, ModifiedAt, IsDeleted, IsExcluded, ExclusionReason,
                     CreatedBy, CreatedAt, ModifiedBy, Version, AuditStatus, ComputoChapterId)
                VALUES
                    (@SessionId, @ElementId, @UniqueId, @Category, @FamilyName, @PhaseCreated, @PhaseDemolished,
                     @EpCode, @EpDescription, @Quantity, @QuantityGross, @QuantityDeducted, @Unit, @UnitPrice,
                     @RuleApplied, @Source, @AssignedAt, @ModifiedAt, @IsDeleted, @IsExcluded, @ExclusionReason,
                     @CreatedBy, @CreatedAt, @ModifiedBy, @Version, @AuditStatus, @ComputoChapterId);
                SELECT last_insert_rowid();";

            var id = _conn.ExecuteScalar<long>(sql, new
            {
                assignment.SessionId,
                assignment.ElementId,
                assignment.UniqueId,
                assignment.Category,
                assignment.FamilyName,
                assignment.PhaseCreated,
                assignment.PhaseDemolished,
                assignment.EpCode,
                assignment.EpDescription,
                assignment.Quantity,
                assignment.QuantityGross,
                assignment.QuantityDeducted,
                assignment.Unit,
                assignment.UnitPrice,
                RuleApplied = assignment.RuleApplied,
                Source = assignment.Source.ToString(),
                assignment.AssignedAt,
                assignment.ModifiedAt,
                IsDeleted = assignment.IsDeleted ? 1 : 0,
                IsExcluded = assignment.IsExcluded ? 1 : 0,
                assignment.ExclusionReason,
                assignment.CreatedBy,
                assignment.CreatedAt,
                assignment.ModifiedBy,
                assignment.Version,
                AuditStatus = assignment.AuditStatus.ToString(),
                assignment.ComputoChapterId
            }, tx);

            assignment.Id = (int)id;
            return assignment.Id;
        }

        public void UpdateAssignment(QtoAssignment assignment)
        {
            const string sql = @"
                UPDATE QtoAssignments SET
                    EpCode = @EpCode,
                    EpDescription = @EpDescription,
                    Quantity = @Quantity,
                    QuantityGross = @QuantityGross,
                    QuantityDeducted = @QuantityDeducted,
                    Unit = @Unit,
                    UnitPrice = @UnitPrice,
                    RuleApplied = @RuleApplied,
                    ModifiedAt = @ModifiedAt,
                    IsDeleted = @IsDeleted,
                    IsExcluded = @IsExcluded,
                    ExclusionReason = @ExclusionReason,
                    ModifiedBy = @ModifiedBy,
                    Version = @Version,
                    AuditStatus = @AuditStatus
                WHERE Id = @Id;";

            _conn.Execute(sql, new
            {
                assignment.Id,
                assignment.EpCode,
                assignment.EpDescription,
                assignment.Quantity,
                assignment.QuantityGross,
                assignment.QuantityDeducted,
                assignment.Unit,
                assignment.UnitPrice,
                assignment.RuleApplied,
                assignment.ModifiedAt,
                IsDeleted = assignment.IsDeleted ? 1 : 0,
                IsExcluded = assignment.IsExcluded ? 1 : 0,
                assignment.ExclusionReason,
                assignment.ModifiedBy,
                assignment.Version,
                AuditStatus = assignment.AuditStatus.ToString()
            });
        }

        public IReadOnlyList<QtoAssignment> GetAssignments(int sessionId)
        {
            const string sql = "SELECT * FROM QtoAssignments WHERE SessionId = @sessionId AND IsDeleted = 0;";
            var rows = _conn.Query<dynamic>(sql, new { sessionId });
            var result = new List<QtoAssignment>();
            foreach (var row in rows)
            {
                result.Add(new QtoAssignment
                {
                    Id = (int)row.Id,
                    SessionId = (int)row.SessionId,
                    ElementId = (int)row.ElementId,
                    UniqueId = row.UniqueId ?? "",
                    Category = row.Category ?? "",
                    FamilyName = row.FamilyName ?? "",
                    PhaseCreated = row.PhaseCreated ?? "",
                    PhaseDemolished = row.PhaseDemolished ?? "",
                    EpCode = row.EpCode ?? "",
                    EpDescription = row.EpDescription ?? "",
                    Quantity = (double)row.Quantity,
                    QuantityGross = (double)(row.QuantityGross ?? 0.0),
                    QuantityDeducted = (double)(row.QuantityDeducted ?? 0.0),
                    Unit = row.Unit ?? "",
                    UnitPrice = (double)row.UnitPrice,
                    RuleApplied = row.RuleApplied ?? "",
                    Source = Enum.TryParse<QtoSource>((string?)row.Source, out var src) ? src : QtoSource.RevitElement,
                    AssignedAt = row.AssignedAt is string ats ? DateTime.Parse(ats) : DateTime.UtcNow,
                    ModifiedAt = row.ModifiedAt is string mts ? (DateTime?)DateTime.Parse(mts) : null,
                    IsDeleted = ((int?)row.IsDeleted ?? 0) != 0,
                    IsExcluded = ((int?)row.IsExcluded ?? 0) != 0,
                    ExclusionReason = row.ExclusionReason ?? "",
                    CreatedBy = row.CreatedBy ?? "",
                    CreatedAt = row.CreatedAt is string cats ? DateTime.Parse(cats) : DateTime.UtcNow,
                    ModifiedBy = row.ModifiedBy,
                    Version = (int)(row.Version ?? 1),
                    AuditStatus = Enum.TryParse<AssignmentStatus>((string?)row.AuditStatus, out var ast) ? ast : AssignmentStatus.Active,
                    ComputoChapterId = row.ComputoChapterId == null ? (int?)null : (int)(long)row.ComputoChapterId
                });
            }
            return result;
        }

        // =====================================================================
        // ChangeLog
        // =====================================================================

        public void AppendChangeLog(ChangeLogEntry entry) => AppendChangeLog(entry, null);

        public void AppendChangeLog(ChangeLogEntry entry, IDbTransaction? tx)
        {
            const string sql = @"
                INSERT INTO ChangeLog
                    (SessionId, ElementUniqueId, PriceItemCode, ChangeType, OldValueJson, NewValueJson, UserId, Timestamp)
                VALUES
                    (@SessionId, @ElementUniqueId, @PriceItemCode, @ChangeType, @OldValueJson, @NewValueJson, @UserId, @Timestamp);";

            _conn.Execute(sql, new
            {
                entry.SessionId,
                entry.ElementUniqueId,
                entry.PriceItemCode,
                entry.ChangeType,
                entry.OldValueJson,
                entry.NewValueJson,
                entry.UserId,
                Timestamp = entry.Timestamp.ToString("o")
            }, tx);
        }

        public IReadOnlyList<ChangeLogEntry> GetChangeLog(int sessionId)
        {
            const string sql = "SELECT * FROM ChangeLog WHERE SessionId = @sessionId ORDER BY ChangeId;";
            var rows = _conn.Query<dynamic>(sql, new { sessionId });
            var result = new List<ChangeLogEntry>();
            foreach (var row in rows)
            {
                result.Add(new ChangeLogEntry
                {
                    ChangeId = (int)row.ChangeId,
                    SessionId = (int)row.SessionId,
                    ElementUniqueId = row.ElementUniqueId ?? "",
                    PriceItemCode = row.PriceItemCode ?? "",
                    ChangeType = row.ChangeType ?? "",
                    OldValueJson = row.OldValueJson,
                    NewValueJson = row.NewValueJson,
                    UserId = row.UserId ?? "",
                    Timestamp = DateTime.Parse(row.Timestamp ?? DateTime.UtcNow.ToString("o"))
                });
            }
            return result;
        }

        // =====================================================================
        // ElementSnapshots
        // =====================================================================

        public void UpsertSnapshot(ElementSnapshot snapshot) => UpsertSnapshot(snapshot, null);

        public void UpsertSnapshot(ElementSnapshot snapshot, IDbTransaction? tx)
        {
            const string sql = @"
                INSERT INTO ElementSnapshots
                    (SessionId, ElementId, UniqueId, SnapshotHash, SnapshotQty, AssignedEPJson, LastUpdated)
                VALUES
                    (@SessionId, @ElementId, @UniqueId, @SnapshotHash, @SnapshotQty, @AssignedEPJson, @LastUpdated)
                ON CONFLICT(SessionId, UniqueId) DO UPDATE SET
                    SnapshotHash   = excluded.SnapshotHash,
                    SnapshotQty    = excluded.SnapshotQty,
                    AssignedEPJson = excluded.AssignedEPJson,
                    LastUpdated    = excluded.LastUpdated;";

            _conn.Execute(sql, new
            {
                snapshot.SessionId,
                snapshot.ElementId,
                snapshot.UniqueId,
                snapshot.SnapshotHash,
                snapshot.SnapshotQty,
                AssignedEPJson = JsonSerializer.Serialize(snapshot.AssignedEP),
                LastUpdated = snapshot.LastUpdated.ToString("o")
            }, tx);
        }

        public IReadOnlyList<ElementSnapshot> GetSnapshots(int sessionId)
        {
            const string sql = "SELECT * FROM ElementSnapshots WHERE SessionId = @sessionId;";
            var rows = _conn.Query<dynamic>(sql, new { sessionId });
            var result = new List<ElementSnapshot>();
            foreach (var row in rows)
            {
                var epJson = (string?)row.AssignedEPJson ?? "[]";
                result.Add(new ElementSnapshot
                {
                    Id = (int)row.Id,
                    SessionId = (int)row.SessionId,
                    ElementId = (int)row.ElementId,
                    UniqueId = row.UniqueId ?? "",
                    SnapshotHash = row.SnapshotHash ?? "",
                    SnapshotQty = (double)row.SnapshotQty,
                    AssignedEP = JsonSerializer.Deserialize<List<string>>(epJson) ?? new List<string>(),
                    LastUpdated = DateTime.Parse(row.LastUpdated ?? DateTime.UtcNow.ToString("o"))
                });
            }
            return result;
        }

        // =====================================================================
        // ComputoChapters (Sprint 9)
        // =====================================================================

        public int InsertComputoChapter(ComputoChapter ch)
        {
            const string sql = @"
INSERT INTO ComputoChapters (SessionId, ParentChapterId, Code, Name, Level, SortOrder, CreatedAt)
VALUES (@SessionId, @ParentChapterId, @Code, @Name, @Level, @SortOrder, @CreatedAt);
SELECT last_insert_rowid();";
            var id = _conn.ExecuteScalar<int>(sql, new
            {
                ch.SessionId, ch.ParentChapterId, ch.Code, ch.Name, ch.Level, ch.SortOrder,
                CreatedAt = ch.CreatedAt.ToString("o", System.Globalization.CultureInfo.InvariantCulture)
            });
            ch.Id = id;
            return id;
        }

        public void UpdateComputoChapter(ComputoChapter ch)
        {
            const string sql = @"
UPDATE ComputoChapters
SET ParentChapterId = @ParentChapterId, Code = @Code, Name = @Name,
    Level = @Level, SortOrder = @SortOrder
WHERE Id = @Id;";
            _conn.Execute(sql, new { ch.Id, ch.ParentChapterId, ch.Code, ch.Name, ch.Level, ch.SortOrder });
        }

        public void DeleteComputoChapter(int chapterId)
        {
            // Foreign keys are OFF by default in SQLite — manually NULL out assignments first
            _conn.Execute(
                "UPDATE QtoAssignments SET ComputoChapterId = NULL WHERE ComputoChapterId = @Id;",
                new { Id = chapterId });
            _conn.Execute("DELETE FROM ComputoChapters WHERE Id = @Id;", new { Id = chapterId });
        }

        public IReadOnlyList<ComputoChapter> GetComputoChapters(int sessionId)
        {
            const string sql = @"
SELECT Id, SessionId, ParentChapterId, Code, Name, Level, SortOrder, CreatedAt
FROM ComputoChapters
WHERE SessionId = @SessionId
ORDER BY Level, SortOrder, Code;";
            return _conn.Query<dynamic>(sql, new { SessionId = sessionId })
                .Select(r => new ComputoChapter
                {
                    Id = (int)(long)r.Id,
                    SessionId = (int)(long)r.SessionId,
                    ParentChapterId = r.ParentChapterId == null ? (int?)null : (int)(long)r.ParentChapterId,
                    Code = (string)r.Code,
                    Name = (string)r.Name,
                    Level = (int)(long)r.Level,
                    SortOrder = (int)(long)r.SortOrder,
                    CreatedAt = System.DateTime.Parse((string)r.CreatedAt, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind)
                })
                .ToList();
        }

        public void AcceptDiffBatch(IReadOnlyList<SupersedeOp> ops)
        {
            if (ops == null || ops.Count == 0) return;

            using var tx = _conn.BeginTransaction();
            try
            {
                foreach (var op in ops)
                {
                    if (op.Kind == SupersedeKind.Modified)
                    {
                        _conn.Execute(
                            "UPDATE QtoAssignments SET AuditStatus = 'Superseded', ModifiedAt = @Now WHERE Id = @Id;",
                            new { Id = op.OldAssignmentId, Now = DateTime.UtcNow }, tx);

                        InsertAssignment(op.NewVersion, tx);

                        UpsertSnapshot(op.NewSnapshot, tx);
                    }
                    else if (op.Kind == SupersedeKind.Deleted)
                    {
                        _conn.Execute(
                            "UPDATE QtoAssignments SET AuditStatus = 'Deleted', ModifiedAt = @Now WHERE Id = @Id;",
                            new { Id = op.OldAssignmentId, Now = DateTime.UtcNow }, tx);
                    }

                    AppendChangeLog(op.Log, tx);
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        // =====================================================================
        // Transazione esposta per operazioni multi-statement
        // =====================================================================

        public SqliteTransaction BeginTransaction() => _conn.BeginTransaction();

        // =====================================================================
        // ProjectInfo (Sprint 10 · schema v7)
        // =====================================================================

        public ProjectInfo? GetProjectInfo(int sessionId)
        {
            const string sql = @"
SELECT Id, SessionId, DenominazioneOpera, Committente, Impresa, RUP, DirettoreLavori,
       Luogo, Comune, Provincia, DataComputo, DataPrezzi, RiferimentoPrezzario,
       CIG, CUP, RibassoPercentuale, LogoPath, UpdatedAt
FROM ProjectInfo
WHERE SessionId = @SessionId
LIMIT 1;";
            return _conn.Query<dynamic>(sql, new { SessionId = sessionId })
                .Select(r => new ProjectInfo
                {
                    Id = (int)(long)r.Id,
                    SessionId = (int)(long)r.SessionId,
                    DenominazioneOpera = (string)r.DenominazioneOpera,
                    Committente = (string)r.Committente,
                    Impresa = (string)r.Impresa,
                    RUP = (string)r.RUP,
                    DirettoreLavori = (string)r.DirettoreLavori,
                    Luogo = (string)r.Luogo,
                    Comune = (string)r.Comune,
                    Provincia = (string)r.Provincia,
                    DataComputo = r.DataComputo == null ? (DateTime?)null : DateTime.Parse((string)r.DataComputo, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
                    DataPrezzi = r.DataPrezzi == null ? (DateTime?)null : DateTime.Parse((string)r.DataPrezzi, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
                    RiferimentoPrezzario = (string)r.RiferimentoPrezzario,
                    CIG = (string)r.CIG,
                    CUP = (string)r.CUP,
                    RibassoPercentuale = Convert.ToDecimal(r.RibassoPercentuale),
                    LogoPath = (string)r.LogoPath,
                    UpdatedAt = DateTime.Parse((string)r.UpdatedAt, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind)
                })
                .FirstOrDefault();
        }

        public void UpsertProjectInfo(ProjectInfo info)
        {
            info.UpdatedAt = DateTime.UtcNow;
            const string sql = @"
INSERT INTO ProjectInfo
    (SessionId, DenominazioneOpera, Committente, Impresa, RUP, DirettoreLavori,
     Luogo, Comune, Provincia, DataComputo, DataPrezzi, RiferimentoPrezzario,
     CIG, CUP, RibassoPercentuale, LogoPath, UpdatedAt)
VALUES
    (@SessionId, @DenominazioneOpera, @Committente, @Impresa, @RUP, @DirettoreLavori,
     @Luogo, @Comune, @Provincia, @DataComputo, @DataPrezzi, @RiferimentoPrezzario,
     @CIG, @CUP, @RibassoPercentuale, @LogoPath, @UpdatedAt)
ON CONFLICT(SessionId) DO UPDATE SET
    DenominazioneOpera = excluded.DenominazioneOpera,
    Committente = excluded.Committente,
    Impresa = excluded.Impresa,
    RUP = excluded.RUP,
    DirettoreLavori = excluded.DirettoreLavori,
    Luogo = excluded.Luogo,
    Comune = excluded.Comune,
    Provincia = excluded.Provincia,
    DataComputo = excluded.DataComputo,
    DataPrezzi = excluded.DataPrezzi,
    RiferimentoPrezzario = excluded.RiferimentoPrezzario,
    CIG = excluded.CIG,
    CUP = excluded.CUP,
    RibassoPercentuale = excluded.RibassoPercentuale,
    LogoPath = excluded.LogoPath,
    UpdatedAt = excluded.UpdatedAt;";
            _conn.Execute(sql, new
            {
                info.SessionId,
                info.DenominazioneOpera,
                info.Committente,
                info.Impresa,
                info.RUP,
                info.DirettoreLavori,
                info.Luogo,
                info.Comune,
                info.Provincia,
                DataComputo = info.DataComputo?.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                DataPrezzi = info.DataPrezzi?.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                info.RiferimentoPrezzario,
                info.CIG,
                info.CUP,
                info.RibassoPercentuale,
                info.LogoPath,
                UpdatedAt = info.UpdatedAt.ToString("o", System.Globalization.CultureInfo.InvariantCulture)
            });
        }

        // =====================================================================
        // Conteggi (per RecoveryService · CRIT-2)
        // =====================================================================

        /// <summary>
        /// Conta le QtoAssignments attive (AuditStatus=Active, IsDeleted=0) per un progetto
        /// dato il path .rvt. Usato da RecoveryService per confrontare il conteggio DB
        /// con quello atteso dal modello (count Extensible Storage), decidendo se il sync
        /// può essere silenzioso o richiede conferma utente.
        /// </summary>
        public int CountActiveAssignmentsForProject(string projectPath)
        {
            const string sql = @"
SELECT COUNT(*)
FROM QtoAssignments a
INNER JOIN Sessions s ON s.Id = a.SessionId
WHERE s.ProjectPath = @ProjectPath
  AND a.AuditStatus = 'Active'
  AND a.IsDeleted = 0;";
            return _conn.ExecuteScalar<int>(sql, new { ProjectPath = projectPath });
        }

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

        private class PriceListRow
        {
            public int Id { get; set; }
            public string? PublicId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string? Source { get; set; }
            public string? Version { get; set; }
            public string? Region { get; set; }
            public int IsActive { get; set; }
            public int Priority { get; set; }
            public DateTime? ImportedAt { get; set; }
            public int RowCount { get; set; }

            public PriceList ToPriceList() => new()
            {
                Id = Id,
                PublicId = PublicId ?? string.Empty,
                Name = Name,
                Source = Source ?? string.Empty,
                Version = Version ?? string.Empty,
                Region = Region ?? string.Empty,
                IsActive = IsActive != 0,
                Priority = Priority,
                ImportedAt = ImportedAt ?? default,
                RowCount = RowCount
            };
        }

        /// <summary>
        /// Mapper per PriceItem con join a PriceLists.Name. Gestisce TEXT nullable → string.Empty
        /// (il model PriceItem usa string non-nullable con default "").
        /// </summary>
        private class PriceItemRow
        {
            public int Id { get; set; }
            public int PriceListId { get; set; }
            public string Code { get; set; } = string.Empty;
            public string? SuperChapter { get; set; }
            public string? Chapter { get; set; }
            public string? SubChapter { get; set; }
            public string Description { get; set; } = string.Empty;
            public string? ShortDesc { get; set; }
            public string? Unit { get; set; }
            public double? UnitPrice { get; set; }
            public string? Notes { get; set; }
            public int IsNP { get; set; }
            public string? ListName { get; set; }

            public PriceItem ToPriceItem() => new()
            {
                Id = Id,
                PriceListId = PriceListId,
                Code = Code,
                SuperChapter = SuperChapter ?? string.Empty,
                Chapter = Chapter ?? string.Empty,
                SubChapter = SubChapter ?? string.Empty,
                Description = Description,
                ShortDesc = ShortDesc ?? string.Empty,
                Unit = Unit ?? string.Empty,
                UnitPrice = UnitPrice ?? 0d,
                Notes = Notes ?? string.Empty,
                IsNP = IsNP != 0,
                ListName = ListName ?? string.Empty
            };
        }

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
            public int? LastUsedComputoChapterId { get; set; }

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
                ModelSnapshotDate = ModelSnapshotDate,
                LastUsedComputoChapterId = LastUsedComputoChapterId
            };
        }
    }
}
