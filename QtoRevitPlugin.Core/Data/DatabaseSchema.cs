namespace QtoRevitPlugin.Data
{
    /// <summary>
    /// DDL completo del database SQLite. Applicato in ordine da DatabaseInitializer al primo avvio.
    /// La versione dello schema è tracciata nella tabella SchemaInfo per permettere migrazioni future.
    ///
    /// Scope per sprint:
    /// - Sprint 1: Sessions, SchemaInfo (attivo). Le altre tabelle sono create vuote e popolate nei sprint successivi.
    /// - Sprint 2: PriceLists, PriceItems + FTS5 virtual table, ManualItems, RoomMappings.
    /// - Sprint 3: QtoAssignments, SelectionRules, MeasurementRules.
    /// - Sprint 7: ModelDiffLog.
    /// - Sprint 8: NuoviPrezzi.
    /// - Sprint 10: EmbeddingCache (AI).
    /// </summary>
    internal static class DatabaseSchema
    {
        public const int CurrentVersion = 1;

        /// <summary>Ordine di esecuzione degli statement per setup iniziale.</summary>
        public static readonly string[] InitialStatements =
        {
            SchemaInfo,
            Sessions,
            PriceLists,
            PriceItems,
            QtoAssignments,
            ManualItems,
            RoomMappings,
            NuoviPrezzi,
            SelectionRules,
            MeasurementRules,
            ModelDiffLog,
            EmbeddingCache
        };

        // --- Meta --------------------------------------------------------------

        public const string SchemaInfo = @"
CREATE TABLE IF NOT EXISTS SchemaInfo (
    Version    INTEGER PRIMARY KEY,
    AppliedAt  DATETIME DEFAULT CURRENT_TIMESTAMP,
    Notes      TEXT
);";

        // --- Sessioni ----------------------------------------------------------

        public const string Sessions = @"
CREATE TABLE IF NOT EXISTS Sessions (
    Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    ProjectPath        TEXT NOT NULL,
    ProjectName        TEXT,
    SessionName        TEXT,
    Status             TEXT NOT NULL DEFAULT 'InProgress',
    ActivePhaseId      INTEGER,
    ActivePhaseName    TEXT,
    TotalElements      INTEGER NOT NULL DEFAULT 0,
    TaggedElements     INTEGER NOT NULL DEFAULT 0,
    TotalAmount        REAL NOT NULL DEFAULT 0,
    LastEpCode         TEXT,
    Notes              TEXT,
    CreatedAt          DATETIME DEFAULT CURRENT_TIMESTAMP,
    LastSavedAt        DATETIME,
    ModelSnapshotDate  DATETIME
);
CREATE INDEX IF NOT EXISTS IX_Sessions_ProjectPath ON Sessions(ProjectPath);
CREATE INDEX IF NOT EXISTS IX_Sessions_Status ON Sessions(Status);";

        // --- Listini (riempiti in Sprint 2) ------------------------------------

        public const string PriceLists = @"
CREATE TABLE IF NOT EXISTS PriceLists (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Name        TEXT NOT NULL,
    Source      TEXT,
    Version     TEXT,
    Region      TEXT,
    IsActive    INTEGER NOT NULL DEFAULT 1,
    Priority    INTEGER NOT NULL DEFAULT 0,
    ImportedAt  DATETIME,
    RowCount    INTEGER NOT NULL DEFAULT 0
);";

        public const string PriceItems = @"
CREATE TABLE IF NOT EXISTS PriceItems (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    PriceListId   INTEGER NOT NULL REFERENCES PriceLists(Id) ON DELETE CASCADE,
    Code          TEXT NOT NULL,
    SuperChapter  TEXT,
    Chapter       TEXT,
    SubChapter    TEXT,
    Description   TEXT NOT NULL,
    ShortDesc     TEXT,
    Unit          TEXT,
    UnitPrice     REAL,
    Notes         TEXT,
    IsNP          INTEGER NOT NULL DEFAULT 0,
    UNIQUE(PriceListId, Code)
);
CREATE INDEX IF NOT EXISTS IX_PriceItems_Code ON PriceItems(Code);";

        // --- Assegnazioni QTO (Sprint 3) ---------------------------------------

        public const string QtoAssignments = @"
CREATE TABLE IF NOT EXISTS QtoAssignments (
    Id                INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId         INTEGER NOT NULL REFERENCES Sessions(Id) ON DELETE CASCADE,
    ElementId         INTEGER NOT NULL,
    UniqueId          TEXT NOT NULL,
    Category          TEXT,
    FamilyName        TEXT,
    PhaseCreated      TEXT,
    PhaseDemolished   TEXT,
    EpCode            TEXT NOT NULL,
    EpDescription     TEXT,
    Quantity          REAL NOT NULL DEFAULT 0,
    QuantityGross     REAL NOT NULL DEFAULT 0,
    QuantityDeducted  REAL NOT NULL DEFAULT 0,
    Unit              TEXT,
    UnitPrice         REAL NOT NULL DEFAULT 0,
    Total             REAL NOT NULL DEFAULT 0,
    RuleApplied       TEXT,
    Source            TEXT NOT NULL DEFAULT 'RevitElement',
    AssignedAt        DATETIME DEFAULT CURRENT_TIMESTAMP,
    ModifiedAt        DATETIME,
    IsDeleted         INTEGER NOT NULL DEFAULT 0,
    IsExcluded        INTEGER NOT NULL DEFAULT 0,
    ExclusionReason   TEXT,
    UNIQUE(SessionId, UniqueId, EpCode)
);
CREATE INDEX IF NOT EXISTS IX_QtoAssignments_Session_Unique ON QtoAssignments(SessionId, UniqueId);
CREATE INDEX IF NOT EXISTS IX_QtoAssignments_EpCode ON QtoAssignments(EpCode);";

        // --- Voci manuali Sorgente C (§I13) ------------------------------------

        public const string ManualItems = @"
CREATE TABLE IF NOT EXISTS ManualItems (
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId      INTEGER NOT NULL REFERENCES Sessions(Id) ON DELETE CASCADE,
    EpCode         TEXT NOT NULL,
    EpDescription  TEXT,
    Quantity       REAL NOT NULL,
    Unit           TEXT,
    UnitPrice      REAL NOT NULL DEFAULT 0,
    Total          REAL NOT NULL DEFAULT 0,
    Notes          TEXT,
    AttachmentPath TEXT,
    CreatedBy      TEXT,
    CreatedAt      DATETIME DEFAULT CURRENT_TIMESTAMP,
    ModifiedAt     DATETIME,
    IsDeleted      INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS IX_ManualItems_Session ON ManualItems(SessionId);
CREATE INDEX IF NOT EXISTS IX_ManualItems_EpCode ON ManualItems(EpCode);";

        // --- Configurazioni mapping Room Sorgente B (§I12) ---------------------

        public const string RoomMappings = @"
CREATE TABLE IF NOT EXISTS RoomMappings (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId       INTEGER NOT NULL REFERENCES Sessions(Id) ON DELETE CASCADE,
    EpCode          TEXT NOT NULL,
    EpDescription   TEXT,
    Unit            TEXT,
    Formula         TEXT NOT NULL,
    TargetCategory  TEXT NOT NULL DEFAULT 'Rooms',
    RoomNameFilter  TEXT
);
CREATE INDEX IF NOT EXISTS IX_RoomMappings_Session ON RoomMappings(SessionId);";

        // --- Nuovi Prezzi (Sprint 8) -------------------------------------------

        public const string NuoviPrezzi = @"
CREATE TABLE IF NOT EXISTS NuoviPrezzi (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId     INTEGER NOT NULL REFERENCES Sessions(Id) ON DELETE CASCADE,
    Code          TEXT NOT NULL,
    Description   TEXT NOT NULL,
    ShortDesc     TEXT,
    Unit          TEXT,
    Manodopera    REAL NOT NULL DEFAULT 0,
    Materiali     REAL NOT NULL DEFAULT 0,
    Noli          REAL NOT NULL DEFAULT 0,
    Trasporti     REAL NOT NULL DEFAULT 0,
    SpGenerali    REAL NOT NULL DEFAULT 15,
    UtileImpresa  REAL NOT NULL DEFAULT 10,
    UnitPrice     REAL NOT NULL DEFAULT 0,
    RibassoAsta   REAL NOT NULL DEFAULT 0,
    Status        TEXT NOT NULL DEFAULT 'Bozza',
    NoteAnalisi   TEXT,
    CreatedAt     DATETIME DEFAULT CURRENT_TIMESTAMP
);";

        // --- Preset regole selezione (Sprint 4) --------------------------------

        public const string SelectionRules = @"
CREATE TABLE IF NOT EXISTS SelectionRules (
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    Name       TEXT NOT NULL,
    RuleJson   TEXT NOT NULL,
    CreatedAt  DATETIME DEFAULT CURRENT_TIMESTAMP
);";

        // --- Regole misurazione per progetto (Sprint 6) ------------------------

        public const string MeasurementRules = @"
CREATE TABLE IF NOT EXISTS MeasurementRules (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId     INTEGER NOT NULL REFERENCES Sessions(Id) ON DELETE CASCADE,
    RuleId        TEXT NOT NULL,
    CategoryName  TEXT,
    Description   TEXT,
    Method        TEXT,
    Threshold     REAL,
    AlwaysDeduct  INTEGER NOT NULL DEFAULT 0,
    Source        TEXT,
    IsActive      INTEGER NOT NULL DEFAULT 1
);";

        // --- Log modifiche modello (Sprint 7) ----------------------------------

        public const string ModelDiffLog = @"
CREATE TABLE IF NOT EXISTS ModelDiffLog (
    Id               INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId        INTEGER NOT NULL REFERENCES Sessions(Id) ON DELETE CASCADE,
    UniqueId         TEXT NOT NULL,
    ChangeType       TEXT NOT NULL,
    Category         TEXT,
    FamilyName       TEXT,
    PhaseCreated     TEXT,
    PhaseDemolished  TEXT,
    DetectedAt       DATETIME DEFAULT CURRENT_TIMESTAMP,
    Resolved         INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS IX_ModelDiffLog_Session ON ModelDiffLog(SessionId);";

        // --- Embedding cache AI (Sprint 10) ------------------------------------

        public const string EmbeddingCache = @"
CREATE TABLE IF NOT EXISTS EmbeddingCache (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    PriceItemId INTEGER NOT NULL REFERENCES PriceItems(Id) ON DELETE CASCADE,
    ModelName   TEXT NOT NULL,
    VectorBlob  BLOB NOT NULL,
    CreatedAt   DATETIME DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(PriceItemId, ModelName)
);";
    }
}
