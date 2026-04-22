using Dapper;
using Microsoft.Data.Sqlite;
using QtoRevitPlugin.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
