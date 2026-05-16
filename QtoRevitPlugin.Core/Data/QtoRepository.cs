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
    /// <summary>
    /// Facciata unica per l'accesso al DB SQLite. Responsabile di aprire la connessione,
    /// gestire il ciclo di vita e esporre i metodi CRUD per ogni entità.
    /// La connessione è keep-alive per la durata della sessione (performance flush &lt; 5ms).
    /// </summary>
    public partial class QtoRepository : IQtoRepository, IDisposable
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
        // (delegato a QtoRepository.PriceLists.cs)
        // =====================================================================

        // =====================================================================
        // Data layer (QtoAssignments, ChangeLog, Snapshots, Chapters, ProjectInfo,
        // Favorites, Embeddings, NuoviPrezzi, ManualItems, RoomMappings, SelectionRules)
        // (delegato a QtoRepository.Data.cs)
        // =====================================================================
        // Row DTO per Query<dynamic> → Query<Tipizzato>
        // =====================================================================

        private class AssignmentRow
        {
            public int Id { get; set; }
            public int SessionId { get; set; }
            public int ElementId { get; set; }
            public string UniqueId { get; set; } = "";
            public string Category { get; set; } = "";
            public string FamilyName { get; set; } = "";
            public string PhaseCreated { get; set; } = "";
            public string PhaseDemolished { get; set; } = "";
            public string EpCode { get; set; } = "";
            public string EpDescription { get; set; } = "";
            public double Quantity { get; set; }
            public double? QuantityGross { get; set; }
            public double? QuantityDeducted { get; set; }
            public string Unit { get; set; } = "";
            public double UnitPrice { get; set; }
            public string RuleApplied { get; set; } = "";
            public string Source { get; set; } = "RevitElement";
            public string AssignedAt { get; set; } = "";
            public string? ModifiedAt { get; set; }
            public int? IsDeleted { get; set; }
            public int? IsExcluded { get; set; }
            public string ExclusionReason { get; set; } = "";
            public string CreatedBy { get; set; } = "";
            public string CreatedAt { get; set; } = "";
            public string? ModifiedBy { get; set; }
            public int? Version { get; set; }
            public string AuditStatus { get; set; } = "Active";
            public int? ComputoChapterId { get; set; }

            public QtoAssignment ToAssignment() => new()
            {
                Id = Id, SessionId = SessionId, ElementId = ElementId,
                UniqueId = UniqueId, Category = Category, FamilyName = FamilyName,
                PhaseCreated = PhaseCreated, PhaseDemolished = PhaseDemolished,
                EpCode = EpCode, EpDescription = EpDescription,
                Quantity = Quantity, QuantityGross = QuantityGross ?? 0.0, QuantityDeducted = QuantityDeducted ?? 0.0,
                Unit = Unit, UnitPrice = UnitPrice,
                RuleApplied = RuleApplied,
                Source = Enum.TryParse<QtoSource>(Source, out var src) ? src : QtoSource.RevitElement,
                AssignedAt = !string.IsNullOrWhiteSpace(AssignedAt) && DateTime.TryParse(AssignedAt, out var at) ? at : DateTime.UtcNow,
                ModifiedAt = !string.IsNullOrWhiteSpace(ModifiedAt) && DateTime.TryParse(ModifiedAt, out var mt) ? mt : null,
                IsDeleted = (IsDeleted ?? 0) != 0,
                IsExcluded = (IsExcluded ?? 0) != 0,
                ExclusionReason = ExclusionReason,
                CreatedBy = CreatedBy,
                CreatedAt = !string.IsNullOrWhiteSpace(CreatedAt) && DateTime.TryParse(CreatedAt, out var cat) ? cat : DateTime.UtcNow,
                ModifiedBy = ModifiedBy,
                Version = Version ?? 1,
                AuditStatus = Enum.TryParse<AssignmentStatus>(AuditStatus, out var ast) ? ast : AssignmentStatus.Active,
                ComputoChapterId = ComputoChapterId
            };
        }

        private class ChangeLogRow
        {
            public int ChangeId { get; set; }
            public int SessionId { get; set; }
            public string ElementUniqueId { get; set; } = "";
            public string PriceItemCode { get; set; } = "";
            public string ChangeType { get; set; } = "";
            public string? OldValueJson { get; set; }
            public string? NewValueJson { get; set; }
            public string UserId { get; set; } = "";
            public string Timestamp { get; set; } = "";

            public ChangeLogEntry ToEntry() => new()
            {
                ChangeId = ChangeId, SessionId = SessionId,
                ElementUniqueId = ElementUniqueId, PriceItemCode = PriceItemCode,
                ChangeType = ChangeType, OldValueJson = OldValueJson,
                NewValueJson = NewValueJson, UserId = UserId,
                Timestamp = DateTime.TryParse(Timestamp, out var ts) ? ts : DateTime.UtcNow
            };
        }

        private class SnapshotRow
        {
            public int Id { get; set; }
            public int SessionId { get; set; }
            public int ElementId { get; set; }
            public string UniqueId { get; set; } = "";
            public string SnapshotHash { get; set; } = "";
            public double SnapshotQty { get; set; }
            public string? AssignedEPJson { get; set; }
            public string LastUpdated { get; set; } = "";

            public ElementSnapshot ToSnapshot() => new()
            {
                Id = Id, SessionId = SessionId, ElementId = ElementId,
                UniqueId = UniqueId, SnapshotHash = SnapshotHash,
                SnapshotQty = SnapshotQty,
                AssignedEP = System.Text.Json.JsonSerializer.Deserialize<List<string>>(AssignedEPJson ?? "[]") ?? new List<string>(),
                LastUpdated = DateTime.TryParse(LastUpdated, out var lu) ? lu : DateTime.UtcNow
            };
        }

        private class ComputoChapterRow
        {
            public long Id { get; set; }
            public long SessionId { get; set; }
            public long? ParentChapterId { get; set; }
            public string Code { get; set; } = "";
            public string Name { get; set; } = "";
            public long Level { get; set; }
            public long SortOrder { get; set; }
            public long? SoaCategoryId { get; set; }
            public string CreatedAt { get; set; } = "";

            public ComputoChapter ToChapter() => new()
            {
                Id = (int)Id, SessionId = (int)SessionId,
                ParentChapterId = ParentChapterId != null ? (int)(long)ParentChapterId : null,
                Code = Code, Name = Name,
                Level = (int)Level, SortOrder = (int)SortOrder,
                SoaCategoryId = SoaCategoryId != null ? (int)(long)SoaCategoryId : null,
                CreatedAt = DateTime.TryParse(CreatedAt, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : DateTime.UtcNow
            };
        }

        private class SoaCategoryRow
        {
            public long Id { get; set; }
            public string Code { get; set; } = "";
            public string Description { get; set; } = "";
            public string Type { get; set; } = "";
            public long SortOrder { get; set; }

            public SoaCategory ToCategory() => new()
            {
                Id = (int)Id, Code = Code, Description = Description,
                Type = Type, SortOrder = (int)SortOrder
            };
        }

        private class ProjectInfoRow
        {
            public long Id { get; set; }
            public long SessionId { get; set; }
            public string DenominazioneOpera { get; set; } = "";
            public string Committente { get; set; } = "";
            public string Impresa { get; set; } = "";
            public string RUP { get; set; } = "";
            public string DirettoreLavori { get; set; } = "";
            public string Luogo { get; set; } = "";
            public string Comune { get; set; } = "";
            public string Provincia { get; set; } = "";
            public string? DataComputo { get; set; }
            public string? DataPrezzi { get; set; }
            public string RiferimentoPrezzario { get; set; } = "";
            public string CIG { get; set; } = "";
            public string CUP { get; set; } = "";
            public decimal RibassoPercentuale { get; set; }
            public string LogoPath { get; set; } = "";
            public string UpdatedAt { get; set; } = "";

            public ProjectInfo ToProjectInfo() => new()
            {
                Id = (int)Id, SessionId = (int)SessionId,
                DenominazioneOpera = DenominazioneOpera, Committente = Committente,
                Impresa = Impresa, RUP = RUP, DirettoreLavori = DirettoreLavori,
                Luogo = Luogo, Comune = Comune, Provincia = Provincia,
                DataComputo = !string.IsNullOrWhiteSpace(DataComputo) && DateTime.TryParse(DataComputo,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var dc) ? dc : null,
                DataPrezzi = !string.IsNullOrWhiteSpace(DataPrezzi) && DateTime.TryParse(DataPrezzi,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var dp) ? dp : null,
                RiferimentoPrezzario = RiferimentoPrezzario,
                CIG = CIG, CUP = CUP, RibassoPercentuale = RibassoPercentuale,
                LogoPath = LogoPath,
                UpdatedAt = DateTime.TryParse(UpdatedAt, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var ua) ? ua : DateTime.UtcNow
            };
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

            // Plan C-4 / schema v12: campi XPWE. Nullable per tolleranza DB pre-migrazione.
            public string? Articolo { get; set; }
            public string? Tariffa { get; set; }
            public double? Prezzo1 { get; set; }
            public double? Prezzo2 { get; set; }
            public double? Prezzo3 { get; set; }
            public double? Prezzo4 { get; set; }
            public double? Prezzo5 { get; set; }
            public int? SpCapId { get; set; }
            public int? CapId { get; set; }
            public int? SbCapId { get; set; }
            public int? WbsCapNodeId { get; set; }
            public double? IncMDO { get; set; }
            public double? IncMAT { get; set; }
            public double? IncSIC { get; set; }
            public int? TipoRisorsa { get; set; }
            public int? Flags { get; set; }
            public string? CnfQt { get; set; }
            public string? AdrInternet { get; set; }
            public string? DataEP { get; set; }

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
                ListName = ListName ?? string.Empty,

                // Plan C-4: campi XPWE
                Articolo = Articolo,
                Tariffa = Tariffa,
                Prezzo1 = Prezzo1 ?? 0d,
                Prezzo2 = Prezzo2 ?? 0d,
                Prezzo3 = Prezzo3 ?? 0d,
                Prezzo4 = Prezzo4 ?? 0d,
                Prezzo5 = Prezzo5 ?? 0d,
                SpCapId = SpCapId,
                CapId = CapId,
                SbCapId = SbCapId,
                WbsCapNodeId = WbsCapNodeId,
                IncMDO = IncMDO ?? 0d,
                IncMAT = IncMAT ?? 0d,
                IncSIC = IncSIC ?? 0d,
                TipoRisorsa = TipoRisorsa ?? 0,
                Flags = Flags ?? 512,
                CnfQt = CnfQt,
                AdrInternet = AdrInternet,
                DataEP = DataEP
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
