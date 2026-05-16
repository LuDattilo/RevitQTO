using Dapper;
using Microsoft.Data.Sqlite;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Models.Computi;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace QtoRevitPlugin.Data
{
    // file-scoped: QtoRepository (partial) — data layer
    public partial class QtoRepository
    {
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
            return _conn.Query<AssignmentRow>(sql, new { sessionId })
                        .Select(r => r.ToAssignment())
                        .ToList();
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
            return _conn.Query<ChangeLogRow>(sql, new { sessionId })
                        .Select(r => r.ToEntry())
                        .ToList();
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
            return _conn.Query<SnapshotRow>(sql, new { sessionId })
                        .Select(r => r.ToSnapshot())
                        .ToList();
        }

        // =====================================================================
        // ComputoChapters (Sprint 9)
        // =====================================================================

        public int InsertComputoChapter(ComputoChapter ch)
        {
            const string sql = @"
INSERT INTO ComputoChapters (SessionId, ParentChapterId, Code, Name, Level, SortOrder, SoaCategoryId, CreatedAt)
VALUES (@SessionId, @ParentChapterId, @Code, @Name, @Level, @SortOrder, @SoaCategoryId, @CreatedAt);
SELECT last_insert_rowid();";
            var id = _conn.ExecuteScalar<int>(sql, new
            {
                ch.SessionId, ch.ParentChapterId, ch.Code, ch.Name, ch.Level, ch.SortOrder, ch.SoaCategoryId,
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
    Level = @Level, SortOrder = @SortOrder, SoaCategoryId = @SoaCategoryId
WHERE Id = @Id;";
            _conn.Execute(sql, new { ch.Id, ch.ParentChapterId, ch.Code, ch.Name, ch.Level, ch.SortOrder, ch.SoaCategoryId });
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
SELECT Id, SessionId, ParentChapterId, Code, Name, Level, SortOrder, SoaCategoryId, CreatedAt
FROM ComputoChapters
WHERE SessionId = @SessionId
ORDER BY Level, SortOrder, Code;";
            return _conn.Query<ComputoChapterRow>(sql, new { SessionId = sessionId })
                .Select(r => r.ToChapter())
                .ToList();
        }

        public IReadOnlyList<SoaCategory> GetSoaCategories()
        {
            const string sql = @"
SELECT Id, Code, Description, Type, SortOrder
FROM SoaCategories
ORDER BY SortOrder;";
            return _conn.Query<SoaCategoryRow>(sql)
                .Select(r => r.ToCategory())
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
            return _conn.Query<ProjectInfoRow>(sql, new { SessionId = sessionId })
                .Select(r => r.ToProjectInfo())
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
        // UserFavorites (v10)
        // =====================================================================

        public IReadOnlyList<UserFavorite> GetFavorites()
        {
            // v11: PriceListPublicId è incluso nel SELECT. Dapper mappa
            // automaticamente TEXT→string? (null se colonna NULL).
            const string sql = @"
SELECT Id, PriceItemId, Code, Description, Unit, UnitPrice,
       ListName, ListId, PriceListPublicId, AddedAt, Note
FROM UserFavorites
ORDER BY AddedAt DESC, Code";
            return _conn.Query<UserFavorite>(sql).ToList();
        }

        /// <summary>
        /// Aggiunge un preferito (INSERT OR IGNORE idempotente su UNIQUE(Code, ListId)) e
        /// ritorna l'Id della riga risultante (nuova o preesistente).
        ///
        /// <para>
        /// Atomicità (rev. 2026-04-23): INSERT + SELECT risoluzione Id sono wrappati in
        /// una singola transazione. Se l'INSERT produce una nuova riga (<c>changes() &gt; 0</c>)
        /// uso <c>last_insert_rowid()</c> (fast-path senza secondo SELECT); altrimenti
        /// risolvo l'Id della riga preesistente con SELECT su (Code, ListId). Sicuro
        /// anche se due popout multi-monitor chiamano AddFavorite sulla stessa voce.
        /// </para>
        /// </summary>
        public int AddFavorite(UserFavorite fav)
        {
            // v11: PriceListPublicId incluso nell'INSERT. Auto-popolato da
            // PriceLists.PublicId se il chiamante non lo fornisce esplicitamente
            // (vedi ResolvePublicIdIfMissing sotto).
            const string insertSql = @"
INSERT OR IGNORE INTO UserFavorites
(PriceItemId, Code, Description, Unit, UnitPrice, ListName, ListId, PriceListPublicId, AddedAt, Note)
VALUES (@PriceItemId, @Code, @Description, @Unit, @UnitPrice, @ListName, @ListId, @PriceListPublicId, @AddedAt, @Note);";

            // SELECT changes() immediatamente dopo INSERT restituisce 1 se la riga è stata
            // inserita, 0 se la constraint UNIQUE(Code, ListId) ha scattato OR IGNORE.
            // In SQLite, "SELECT changes()" deve essere eseguito sulla STESSA connessione
            // dell'INSERT — garantito qui perché usiamo sempre _conn.
            const string selectBySearchSql = @"
SELECT Id FROM UserFavorites
WHERE Code = @Code AND ListId IS @ListId LIMIT 1;";

            using var tx = _conn.BeginTransaction();
            try
            {
                // v11 auto-resolve: se il chiamante non fornisce PriceListPublicId
                // ma fornisce ListId, guardiamo il PublicId nella tabella PriceLists.
                // Questo mantiene il dato coerente anche per chiamanti legacy che
                // non conoscono il nuovo campo.
                var resolvedPublicId = fav.PriceListPublicId;
                if (string.IsNullOrWhiteSpace(resolvedPublicId) && fav.ListId.HasValue)
                {
                    resolvedPublicId = _conn.ExecuteScalar<string?>(
                        "SELECT PublicId FROM PriceLists WHERE Id = @Id LIMIT 1;",
                        new { Id = fav.ListId.Value },
                        transaction: tx);
                }

                var inserted = _conn.Execute(insertSql, new
                {
                    fav.PriceItemId,
                    fav.Code,
                    fav.Description,
                    fav.Unit,
                    fav.UnitPrice,
                    fav.ListName,
                    fav.ListId,
                    PriceListPublicId = resolvedPublicId,
                    AddedAt = fav.AddedAt.ToString("o"),
                    fav.Note
                }, transaction: tx);

                int id;
                if (inserted > 0)
                {
                    // Fast-path: nuova riga → last_insert_rowid() è il PK generato
                    id = _conn.ExecuteScalar<int>("SELECT last_insert_rowid();", transaction: tx);
                }
                else
                {
                    // Riga esistente (constraint UNIQUE) → risolvi via SELECT
                    id = _conn.ExecuteScalar<int>(selectBySearchSql,
                        new { fav.Code, fav.ListId },
                        transaction: tx);
                }

                tx.Commit();
                return id;
            }
            catch
            {
                try { tx.Rollback(); } catch { /* best-effort */ }
                throw;
            }
        }

        public void RemoveFavorite(int id)
        {
            const string sql = "DELETE FROM UserFavorites WHERE Id = @Id";
            _conn.Execute(sql, new { Id = id });
        }

        public bool IsFavorite(string code, int? listId)
        {
            const string sql = @"
SELECT COUNT(*) FROM UserFavorites WHERE Code = @Code AND ListId IS @ListId";

            var n = _conn.ExecuteScalar<int>(sql, new { Code = code, ListId = listId });
            return n > 0;
        }

        /// <summary>
        /// Ritorna i codici EP assegnati attivamente nel computo per una sessione.
        /// NOTA: QtoAssignments vive nel .cme (DB di sessione), NON nell'UserLibrary.db.
        /// Questo metodo funziona SOLO se invocato su un repository aperto sul .cme
        /// corretto. Se la tabella QtoAssignments non esiste (es. UserLibrary.db),
        /// ritorna un HashSet vuoto senza throw.
        /// </summary>
        public System.Collections.Generic.HashSet<string> GetUsedEpCodes(int sessionId)
        {
            // Guard: se il DB non ha QtoAssignments (es. chiamato su UserLibrary.db),
            // ritorniamo vuoto invece di lanciare. L'UI chiama questo metodo sul
            // SessionManager.Repository che è il .cme.
            var tableExists = _conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='QtoAssignments'");
            if (tableExists == 0) return new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            const string sql = @"
SELECT DISTINCT EpCode
FROM QtoAssignments
WHERE SessionId = @SessionId AND AuditStatus = 'Active' AND EpCode IS NOT NULL AND EpCode <> ''";

            var codes = _conn.Query<string>(sql, new { SessionId = sessionId });
            return new System.Collections.Generic.HashSet<string>(codes, System.StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Bulk-delete preferiti in una transazione. Ritorna il numero di righe cancellate.
        /// NON tocca PriceItems / PriceLists.
        /// </summary>
        public int RemoveFavorites(System.Collections.Generic.IEnumerable<int> favoriteIds)
        {
            var ids = favoriteIds?.ToList();
            if (ids == null || ids.Count == 0) return 0;

            using var tx = _conn.BeginTransaction();
            int deleted = _conn.Execute(
                "DELETE FROM UserFavorites WHERE Id IN @Ids",
                new { Ids = ids },
                transaction: tx);
            tx.Commit();
            return deleted;
        }

        // =====================================================================
        // RevitParamMapping (v9) — mapping campi Informazioni Progetto
        // =====================================================================

        public IReadOnlyList<RevitParamMapping> GetRevitParamMappings(int sessionId)
        {
            const string sql = @"
SELECT Id, SessionId, FieldKey, ParamName, IsBuiltIn, SkipIfFilled
FROM RevitParamMapping
WHERE SessionId = @SessionId
ORDER BY FieldKey;";
            return _conn.Query<RevitParamMapping>(sql, new { SessionId = sessionId }).ToList();
        }

        public void UpsertRevitParamMapping(RevitParamMapping mapping)
        {
            // UNIQUE(SessionId, FieldKey) nello schema → ON CONFLICT REPLACE
            const string sql = @"
INSERT INTO RevitParamMapping (SessionId, FieldKey, ParamName, IsBuiltIn, SkipIfFilled)
VALUES (@SessionId, @FieldKey, @ParamName, @IsBuiltIn, @SkipIfFilled)
ON CONFLICT(SessionId, FieldKey) DO UPDATE SET
    ParamName = excluded.ParamName,
    IsBuiltIn = excluded.IsBuiltIn,
    SkipIfFilled = excluded.SkipIfFilled;";

            _conn.Execute(sql, new
            {
                mapping.SessionId,
                mapping.FieldKey,
                mapping.ParamName,
                IsBuiltIn = mapping.IsBuiltIn ? 1 : 0,
                SkipIfFilled = mapping.SkipIfFilled ? 1 : 0
            });
        }

        public void DeleteRevitParamMapping(int sessionId, string fieldKey)
        {
            const string sql = "DELETE FROM RevitParamMapping WHERE SessionId = @SessionId AND FieldKey = @FieldKey";
            _conn.Execute(sql, new { SessionId = sessionId, FieldKey = fieldKey });
        }

        // =====================================================================
        // EmbeddingCache (AI — modulo opzionale)
        // =====================================================================
        //
        // La tabella è sul .cme (popolata one-shot al caricamento del listino).
        // Schema: UNIQUE(PriceItemId, ModelName) — un solo embedding per item
        // per modello. Se l'utente cambia modello, invalidare via
        // DeleteEmbeddingsForModel + ricalcolo batch dal service AI.

        public bool HasEmbedding(int priceItemId, string modelName)
        {
            const string sql = @"
SELECT COUNT(*) FROM EmbeddingCache
WHERE PriceItemId = @PriceItemId AND ModelName = @ModelName";
            return _conn.ExecuteScalar<int>(sql, new { PriceItemId = priceItemId, ModelName = modelName }) > 0;
        }

        public void UpsertEmbedding(int priceItemId, string modelName, byte[] vectorBlob)
        {
            if (vectorBlob == null) throw new ArgumentNullException(nameof(vectorBlob));
            if (vectorBlob.Length == 0)
                throw new ArgumentException("Vector blob vuoto.", nameof(vectorBlob));

            const string sql = @"
INSERT INTO EmbeddingCache (PriceItemId, ModelName, VectorBlob)
VALUES (@PriceItemId, @ModelName, @VectorBlob)
ON CONFLICT(PriceItemId, ModelName) DO UPDATE SET
    VectorBlob = excluded.VectorBlob,
    CreatedAt  = CURRENT_TIMESTAMP;";

            _conn.Execute(sql, new
            {
                PriceItemId = priceItemId,
                ModelName = modelName,
                VectorBlob = vectorBlob
            });
        }

        public IReadOnlyList<QtoRevitPlugin.AI.EmbeddingEntry> GetEmbeddings(
            IReadOnlyList<int> priceItemIds,
            string modelName)
        {
            if (priceItemIds == null || priceItemIds.Count == 0)
                return new List<QtoRevitPlugin.AI.EmbeddingEntry>();

            // Dapper gestisce IN con parametro IEnumerable automaticamente
            const string sql = @"
SELECT Id, PriceItemId, ModelName, VectorBlob, CreatedAt
FROM EmbeddingCache
WHERE ModelName = @ModelName AND PriceItemId IN @Ids;";

            return _conn.Query<QtoRevitPlugin.AI.EmbeddingEntry>(sql, new
            {
                ModelName = modelName,
                Ids = priceItemIds
            }).ToList();
        }

        public int DeleteEmbeddingsForModel(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName)) return 0;
            const string sql = "DELETE FROM EmbeddingCache WHERE ModelName = @ModelName;";
            return _conn.Execute(sql, new { ModelName = modelName });
        }

        public int DeleteEmbeddingsForPriceList(int priceListId)
        {
            // Join necessario perché EmbeddingCache riferisce PriceItems, non PriceLists
            const string sql = @"
DELETE FROM EmbeddingCache
WHERE PriceItemId IN (SELECT Id FROM PriceItems WHERE PriceListId = @PriceListId);";
            return _conn.Execute(sql, new { PriceListId = priceListId });
        }

        // =====================================================================
        // NuoviPrezzi (I8)
        // =====================================================================

        public IReadOnlyList<NuovoPrezzo> GetNuoviPrezzi(int sessionId)
        {
            const string sql = @"
SELECT Id, SessionId, Code, Description, ShortDesc, Unit,
       Manodopera, Materiali, Noli, Trasporti,
       SpGenerali, UtileImpresa, RibassoAsta,
       Status, NoteAnalisi, CreatedAt
FROM NuoviPrezzi
WHERE SessionId = @SessionId
ORDER BY Code;";
            return _conn.Query<NpRow>(sql, new { SessionId = sessionId })
                        .Select(r => r.ToNuovoPrezzo())
                        .ToList();
        }

        public int InsertNuovoPrezzo(NuovoPrezzo np)
        {
            if (np == null) throw new ArgumentNullException(nameof(np));
            const string sql = @"
INSERT INTO NuoviPrezzi
(SessionId, Code, Description, ShortDesc, Unit,
 Manodopera, Materiali, Noli, Trasporti,
 SpGenerali, UtileImpresa, RibassoAsta,
 UnitPrice, Status, NoteAnalisi, CreatedAt)
VALUES
(@SessionId, @Code, @Description, @ShortDesc, @Unit,
 @Manodopera, @Materiali, @Noli, @Trasporti,
 @SpGenerali, @UtileImpresa, @RibassoAsta,
 @UnitPrice, @Status, @NoteAnalisi, @CreatedAt);
SELECT last_insert_rowid();";

            return _conn.ExecuteScalar<int>(sql, new
            {
                np.SessionId, np.Code, np.Description, np.ShortDesc, np.Unit,
                np.Manodopera, np.Materiali, np.Noli, np.Trasporti,
                np.SpGenerali, np.UtileImpresa, np.RibassoAsta,
                UnitPrice = np.UnitPrice, // computed via getter del model
                Status = np.Status.ToString(),
                np.NoteAnalisi,
                CreatedAt = np.CreatedAt.ToString("o")
            });
        }

        public void UpdateNuovoPrezzo(NuovoPrezzo np)
        {
            if (np == null) throw new ArgumentNullException(nameof(np));
            const string sql = @"
UPDATE NuoviPrezzi SET
    Code = @Code, Description = @Description, ShortDesc = @ShortDesc, Unit = @Unit,
    Manodopera = @Manodopera, Materiali = @Materiali, Noli = @Noli, Trasporti = @Trasporti,
    SpGenerali = @SpGenerali, UtileImpresa = @UtileImpresa, RibassoAsta = @RibassoAsta,
    UnitPrice = @UnitPrice, Status = @Status, NoteAnalisi = @NoteAnalisi
WHERE Id = @Id;";
            _conn.Execute(sql, new
            {
                np.Id, np.Code, np.Description, np.ShortDesc, np.Unit,
                np.Manodopera, np.Materiali, np.Noli, np.Trasporti,
                np.SpGenerali, np.UtileImpresa, np.RibassoAsta,
                UnitPrice = np.UnitPrice,
                Status = np.Status.ToString(),
                np.NoteAnalisi
            });
        }

        public void DeleteNuovoPrezzo(int id)
        {
            _conn.Execute("DELETE FROM NuoviPrezzi WHERE Id = @Id;", new { Id = id });
        }

        /// <summary>Row interno per mapping Dapper Status string → enum.</summary>
        private class NpRow
        {
            public int Id { get; set; }
            public int SessionId { get; set; }
            public string Code { get; set; } = "";
            public string Description { get; set; } = "";
            public string? ShortDesc { get; set; }
            public string? Unit { get; set; }
            public double Manodopera { get; set; }
            public double Materiali { get; set; }
            public double Noli { get; set; }
            public double Trasporti { get; set; }
            public double SpGenerali { get; set; }
            public double UtileImpresa { get; set; }
            public double RibassoAsta { get; set; }
            public string Status { get; set; } = "Bozza";
            public string? NoteAnalisi { get; set; }
            public DateTime CreatedAt { get; set; }

            public NuovoPrezzo ToNuovoPrezzo() => new NuovoPrezzo
            {
                Id = Id, SessionId = SessionId, Code = Code, Description = Description,
                ShortDesc = ShortDesc ?? "", Unit = Unit ?? "",
                Manodopera = Manodopera, Materiali = Materiali, Noli = Noli, Trasporti = Trasporti,
                SpGenerali = SpGenerali, UtileImpresa = UtileImpresa, RibassoAsta = RibassoAsta,
                Status = Enum.TryParse<NpStatus>(Status, out var s) ? s : NpStatus.Bozza,
                NoteAnalisi = NoteAnalisi ?? "",
                CreatedAt = CreatedAt
            };
        }

        // =====================================================================
        // ManualItems (I13)
        // =====================================================================

        public IReadOnlyList<ManualQuantityEntry> GetManualItems(int sessionId)
        {
            const string sql = @"
SELECT Id, SessionId, EpCode, EpDescription, Unit, Quantity, UnitPrice, Total,
       Notes, AttachmentPath, CreatedBy, CreatedAt, ModifiedAt, IsDeleted
FROM ManualItems
WHERE SessionId = @SessionId AND IsDeleted = 0
ORDER BY EpCode, Id;";
            return _conn.Query<ManualItemRow>(sql, new { SessionId = sessionId })
                        .Select(r => r.ToEntry())
                        .ToList();
        }

        public int InsertManualItem(ManualQuantityEntry item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            const string sql = @"
INSERT INTO ManualItems
(SessionId, EpCode, EpDescription, Quantity, Unit, UnitPrice, Total,
 Notes, AttachmentPath, CreatedBy, CreatedAt, IsDeleted)
VALUES
(@SessionId, @EpCode, @EpDescription, @Quantity, @Unit, @UnitPrice, @Total,
 @Notes, @AttachmentPath, @CreatedBy, @CreatedAt, 0);
SELECT last_insert_rowid();";
            return _conn.ExecuteScalar<int>(sql, new
            {
                item.SessionId, item.EpCode, item.EpDescription, item.Quantity,
                item.Unit, item.UnitPrice, Total = item.Total,
                item.Notes, item.AttachmentPath, item.CreatedBy,
                CreatedAt = item.CreatedAt.ToString("o")
            });
        }

        public void UpdateManualItem(ManualQuantityEntry item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            const string sql = @"
UPDATE ManualItems SET
    EpCode = @EpCode, EpDescription = @EpDescription, Quantity = @Quantity,
    Unit = @Unit, UnitPrice = @UnitPrice, Total = @Total,
    Notes = @Notes, AttachmentPath = @AttachmentPath,
    ModifiedAt = @ModifiedAt
WHERE Id = @Id;";
            _conn.Execute(sql, new
            {
                item.Id, item.EpCode, item.EpDescription, item.Quantity,
                item.Unit, item.UnitPrice, Total = item.Total,
                item.Notes, item.AttachmentPath,
                ModifiedAt = DateTime.UtcNow.ToString("o")
            });
        }

        public void DeleteManualItem(int id)
        {
            // Soft delete per audit trail
            _conn.Execute(
                "UPDATE ManualItems SET IsDeleted = 1, ModifiedAt = @Now WHERE Id = @Id;",
                new { Id = id, Now = DateTime.UtcNow.ToString("o") });
        }

        // =====================================================================
        // RoomMappingConfigs (Sprint 11)
        // =====================================================================

        public int InsertRoomMappingConfig(RoomMappingConfig cfg)
        {
            const string sql = @"
INSERT INTO RoomMappings (SessionId, EpCode, EpDescription, Unit, Formula, TargetCategory, RoomNameFilter)
VALUES (@SessionId, @EpCode, @EpDescription, @Unit, @Formula, @TargetCategory, @RoomNameFilter);
SELECT last_insert_rowid();";
            return _conn.ExecuteScalar<int>(sql, new
            {
                cfg.SessionId,
                cfg.EpCode,
                cfg.EpDescription,
                cfg.Unit,
                cfg.Formula,
                TargetCategory = cfg.TargetCategory.ToString(),
                cfg.RoomNameFilter
            });
        }

        public IReadOnlyList<RoomMappingConfig> GetRoomMappingConfigs(int sessionId)
        {
            const string sql = @"
SELECT Id, SessionId, EpCode, EpDescription, Unit, Formula, TargetCategory, RoomNameFilter
FROM RoomMappings
WHERE SessionId = @SessionId
ORDER BY Id;";
            return _conn.Query<RoomMappingRow>(sql, new { SessionId = sessionId })
                .Select(r => r.ToConfig())
                .ToList();
        }

        public void UpdateRoomMappingConfig(RoomMappingConfig cfg)
        {
            const string sql = @"
UPDATE RoomMappings
SET EpCode=@EpCode, EpDescription=@EpDescription, Unit=@Unit,
    Formula=@Formula, TargetCategory=@TargetCategory, RoomNameFilter=@RoomNameFilter
WHERE Id=@Id;";
            _conn.Execute(sql, new
            {
                cfg.Id,
                cfg.EpCode,
                cfg.EpDescription,
                cfg.Unit,
                cfg.Formula,
                TargetCategory = cfg.TargetCategory.ToString(),
                cfg.RoomNameFilter
            });
        }

        public void DeleteRoomMappingConfig(int id)
        {
            _conn.Execute("DELETE FROM RoomMappings WHERE Id=@Id;", new { Id = id });
        }

        private class RoomMappingRow
        {
            public int Id { get; set; }
            public int SessionId { get; set; }
            public string EpCode { get; set; } = "";
            public string? EpDescription { get; set; }
            public string? Unit { get; set; }
            public string Formula { get; set; } = "";
            public string TargetCategory { get; set; } = "Rooms";
            public string? RoomNameFilter { get; set; }

            public RoomMappingConfig ToConfig() => new RoomMappingConfig
            {
                Id = Id,
                SessionId = SessionId,
                EpCode = EpCode,
                EpDescription = EpDescription ?? "",
                Unit = Unit ?? "",
                Formula = Formula,
                TargetCategory = TargetCategory == "MEPSpaces"
                    ? RoomTargetCategory.MEPSpaces
                    : RoomTargetCategory.Rooms,
                RoomNameFilter = RoomNameFilter ?? ""
            };
        }

        private class ManualItemRow
        {
            public int Id { get; set; }
            public int SessionId { get; set; }
            public string EpCode { get; set; } = "";
            public string? EpDescription { get; set; }
            public string? Unit { get; set; }
            public double Quantity { get; set; }
            public double UnitPrice { get; set; }
            public double Total { get; set; }
            public string? Notes { get; set; }
            public string? AttachmentPath { get; set; }
            public string? CreatedBy { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime? ModifiedAt { get; set; }
            public int IsDeleted { get; set; }

            public ManualQuantityEntry ToEntry() => new ManualQuantityEntry
            {
                Id = Id, SessionId = SessionId, EpCode = EpCode,
                EpDescription = EpDescription ?? "", Unit = Unit ?? "",
                Quantity = Quantity, UnitPrice = UnitPrice,
                // Total è computed via getter — non serve riassegnarlo
                Notes = Notes ?? "", AttachmentPath = AttachmentPath ?? "",
                CreatedBy = CreatedBy ?? "", CreatedAt = CreatedAt, ModifiedAt = ModifiedAt,
                IsDeleted = IsDeleted != 0
            };
        }

        // =====================================================================
        // SelectionRules (I6) — preset regole di selezione in JSON
        // =====================================================================

        public IReadOnlyList<(int Id, string Name)> GetSelectionRulePresetNames()
        {
            const string sql = "SELECT Id, Name FROM SelectionRules ORDER BY Name;";
            // Dapper non supporta direttamente ValueTuple → leggiamo in un record privato
            return _conn.Query<SelectionRuleRow>(sql)
                .Select(r => (r.Id, r.Name))
                .ToList();
        }

        public SelectionRulePreset? GetSelectionRulePreset(int id)
        {
            const string sql = "SELECT Id, Name, RuleJson, CreatedAt FROM SelectionRules WHERE Id = @Id;";
            var row = _conn.QueryFirstOrDefault<SelectionRuleRow>(sql, new { Id = id });
            if (row == null) return null;

            try
            {
                var preset = QtoRevitPlugin.Services.SelectionRulePresetService.Deserialize(row.RuleJson);
                // Garantisce il nome corrispondente anche se il JSON salvato aveva un nome diverso
                if (string.IsNullOrEmpty(preset.RuleName)) preset.RuleName = row.Name;
                return preset;
            }
            catch
            {
                // JSON corrotto → non throw qui; il chiamante vedrà null e può mostrare errore UI
                return null;
            }
        }

        public int UpsertSelectionRulePreset(SelectionRulePreset preset)
        {
            if (preset == null) throw new ArgumentNullException(nameof(preset));
            if (string.IsNullOrWhiteSpace(preset.RuleName))
                throw new ArgumentException("Il preset deve avere un RuleName non vuoto.", nameof(preset));

            var json = QtoRevitPlugin.Services.SelectionRulePresetService.Serialize(preset);

            // SelectionRules non ha UNIQUE(Name) nello schema — implementiamo manualmente
            // l'upsert: cerca per Name, UPDATE se esiste altrimenti INSERT.
            using var tx = _conn.BeginTransaction();
            try
            {
                var existingId = _conn.ExecuteScalar<int?>(
                    "SELECT Id FROM SelectionRules WHERE Name = @Name LIMIT 1;",
                    new { Name = preset.RuleName },
                    transaction: tx);

                int id;
                if (existingId.HasValue)
                {
                    _conn.Execute(
                        "UPDATE SelectionRules SET RuleJson = @RuleJson WHERE Id = @Id;",
                        new { Id = existingId.Value, RuleJson = json },
                        transaction: tx);
                    id = existingId.Value;
                }
                else
                {
                    id = _conn.ExecuteScalar<int>(
                        @"INSERT INTO SelectionRules (Name, RuleJson, CreatedAt)
                          VALUES (@Name, @RuleJson, @CreatedAt);
                          SELECT last_insert_rowid();",
                        new
                        {
                            Name = preset.RuleName,
                            RuleJson = json,
                            CreatedAt = DateTime.UtcNow.ToString("o")
                        },
                        transaction: tx);
                }

                tx.Commit();
                return id;
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                throw;
            }
        }

        public void DeleteSelectionRulePreset(int id)
        {
            _conn.Execute("DELETE FROM SelectionRules WHERE Id = @Id;", new { Id = id });
        }

        private class SelectionRuleRow
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public string RuleJson { get; set; } = "";
            public DateTime CreatedAt { get; set; }
        }

        // =====================================================================
    }
}
