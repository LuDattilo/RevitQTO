using Dapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using QtoRevitPlugin.Models;

namespace QtoRevitPlugin.Data
{
    // file-scoped: QtoRepository (partial) — PriceLists domain
    public partial class QtoRepository
    {
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

        public IReadOnlyList<PriceItem> GetPriceItems(IReadOnlyList<int> ids)
        {
            if (ids == null || ids.Count == 0) return new List<PriceItem>();

            // Dapper espande IN @Ids in parametri posizionali
            const string sql = @"
SELECT p.*, pl.Name AS ListName
FROM PriceItems p
JOIN PriceLists pl ON pl.Id = p.PriceListId
WHERE p.Id IN @Ids;";

            return _conn.Query<PriceItemRow>(sql, new { Ids = ids })
                        .Select(r => r.ToPriceItem())
                        .ToList();
        }

        public IReadOnlyList<PriceItem> GetPriceItemsByCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return new List<PriceItem>();
            // COLLATE NOCASE rende la LIKE case-insensitive (PriMus e import custom possono
            // differire per maiuscole/minuscole del Code). TRIM per tollerare eventuali spazi.
            const string sql = @"
SELECT p.*, pl.Name AS ListName
FROM PriceItems p
JOIN PriceLists pl ON pl.Id = p.PriceListId
WHERE TRIM(p.Code) = TRIM(@c) COLLATE NOCASE AND pl.IsActive = 1;";
            return _conn.Query<PriceItemRow>(sql, new { c = code.Trim() })
                        .Select(r => r.ToPriceItem())
                        .ToList();
        }

        /// <summary>
        /// Plan C-6: insert singolo di un PriceItem con tutti i campi v12 (XPWE) preservati.
        /// Usato per copiare voci da UserLibrary al .cme del progetto.
        /// Override PriceListId con quello passato (la lista di destinazione può essere diversa
        /// da source.PriceListId perché stiamo attraversando DB diversi).
        /// </summary>
        public PriceItem InsertPriceItemSingle(PriceItem source, int targetPriceListId)
        {
            const string sql = @"
                INSERT INTO PriceItems
                    (PriceListId, Code, SuperChapter, Chapter, SubChapter, Description, ShortDesc,
                     Unit, UnitPrice, Notes, IsNP,
                     Articolo, Tariffa, Prezzo1, Prezzo2, Prezzo3, Prezzo4, Prezzo5,
                     SpCapId, CapId, SbCapId, WbsCapNodeId,
                     IncMDO, IncMAT, IncSIC, TipoRisorsa, Flags, CnfQt, AdrInternet, DataEP)
                VALUES
                    (@PriceListId, @Code, @SuperChapter, @Chapter, @SubChapter, @Description, @ShortDesc,
                     @Unit, @UnitPrice, @Notes, @IsNP,
                     @Articolo, @Tariffa, @Prezzo1, @Prezzo2, @Prezzo3, @Prezzo4, @Prezzo5,
                     @SpCapId, @CapId, @SbCapId, @WbsCapNodeId,
                     @IncMDO, @IncMAT, @IncSIC, @TipoRisorsa, @Flags, @CnfQt, @AdrInternet, @DataEP);
                SELECT last_insert_rowid();";
            var id = _conn.ExecuteScalar<int>(sql, new
            {
                PriceListId = targetPriceListId,
                source.Code,
                source.SuperChapter,
                source.Chapter,
                source.SubChapter,
                source.Description,
                source.ShortDesc,
                source.Unit,
                source.UnitPrice,
                source.Notes,
                IsNP = source.IsNP ? 1 : 0,
                source.Articolo,
                source.Tariffa,
                source.Prezzo1,
                source.Prezzo2,
                source.Prezzo3,
                source.Prezzo4,
                source.Prezzo5,
                source.SpCapId,
                source.CapId,
                source.SbCapId,
                source.WbsCapNodeId,
                source.IncMDO,
                source.IncMAT,
                source.IncSIC,
                source.TipoRisorsa,
                source.Flags,
                source.CnfQt,
                source.AdrInternet,
                source.DataEP
            });
            var copy = new PriceItem
            {
                Id = id,
                PriceListId = targetPriceListId,
                Code = source.Code,
                SuperChapter = source.SuperChapter,
                Chapter = source.Chapter,
                SubChapter = source.SubChapter,
                Description = source.Description,
                ShortDesc = source.ShortDesc,
                Unit = source.Unit,
                UnitPrice = source.UnitPrice,
                Notes = source.Notes,
                IsNP = source.IsNP,
                ListName = "",  // sarà popolato su lettura successiva tramite JOIN
                Articolo = source.Articolo,
                Tariffa = source.Tariffa,
                Prezzo1 = source.Prezzo1,
                Prezzo2 = source.Prezzo2,
                Prezzo3 = source.Prezzo3,
                Prezzo4 = source.Prezzo4,
                Prezzo5 = source.Prezzo5,
                SpCapId = source.SpCapId,
                CapId = source.CapId,
                SbCapId = source.SbCapId,
                WbsCapNodeId = source.WbsCapNodeId,
                IncMDO = source.IncMDO,
                IncMAT = source.IncMAT,
                IncSIC = source.IncSIC,
                TipoRisorsa = source.TipoRisorsa,
                Flags = source.Flags,
                CnfQt = source.CnfQt,
                AdrInternet = source.AdrInternet,
                DataEP = source.DataEP
            };
            return copy;
        }

        public IReadOnlyList<PriceItem> SearchPriceItemsByCodeLike(string code, int limit)
        {
            if (string.IsNullOrWhiteSpace(code)) return new List<PriceItem>();
            // Ricerca fuzzy: estrae 8 caratteri centrali dal codice (se lungo >12)
            // e cerca LIKE '%core%'. Utile per diagnosticare discrepanze prefisso/suffisso.
            var trim = code.Trim();
            var core = trim.Length > 12
                ? trim.Substring(trim.Length / 2 - 4, 8)
                : trim;
            const string sql = @"
SELECT p.*, pl.Name AS ListName
FROM PriceItems p
JOIN PriceLists pl ON pl.Id = p.PriceListId
WHERE p.Code LIKE @pat COLLATE NOCASE
LIMIT @lim;";
            return _conn.Query<PriceItemRow>(sql, new { pat = "%" + core + "%", lim = limit })
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

            // Stripper caratteri FTS5 problematici: virgolette, star, parentesi, caret, trattino, colon,
            // cancelletto, tilde (NEAR), più (AND esplicito)
            var cleaned = new StringBuilder(src.Length);
            foreach (var ch in src)
            {
                if (ch == '"' || ch == '*' || ch == '(' || ch == ')' ||
                    ch == '^' || ch == '-' || ch == ':' || ch == '#' ||
                    ch == '~' || ch == '+')
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
    }
}
