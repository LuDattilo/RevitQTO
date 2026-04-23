namespace QtoRevitPlugin.Data
{
    /// <summary>
    /// DDL completo del database SQLite. Applicato in ordine da DatabaseInitializer al primo avvio.
    /// La versione dello schema è tracciata nella tabella SchemaInfo per permettere migrazioni future.
    ///
    /// Scope per sprint:
    /// - Sprint 1: Sessions, SchemaInfo (attivo). Le altre tabelle sono create vuote e popolate nei sprint successivi.
    /// - Sprint 2: PriceLists, PriceItems + FTS5 virtual table (attivi), ManualItems, RoomMappings.
    /// - Sprint 3: QtoAssignments, SelectionRules, MeasurementRules.
    /// - Sprint 7: ModelDiffLog.
    /// - Sprint 8: NuoviPrezzi.
    /// - Sprint 10: EmbeddingCache (AI).
    /// </summary>
    internal static class DatabaseSchema
    {
        // v9 (Infoproj v2): tabella comuni_italiani (ISTAT dataset, solo UserLibrary)
        //                + tabella RevitParamMapping (mapping parametri per-sessione, solo .cme).
        // v8 (Sprint 10 step 2): tabelle SoaCategories (seed OG 1..13 + OS 1..35 D.Lgs. 36/2023)
        //                 + colonna ComputoChapters.SoaCategoryId FK nullable per
        //                 assegnare OG/OS ai nodi della struttura computo. Eredità
        //                 implicita risolta lato ViewModel (no denormalizzazione DB).
        // v7 (Sprint 10): tabella ProjectInfo per metadati computo conformi PriMus
        //                 (DenominazioneOpera, Committente, Impresa, RUP, DL, Luogo, Comune,
        //                 Provincia, DataComputo, DataPrezzi, RiferimentoPrezzario, CIG, CUP,
        //                 RibassoPercentuale, LogoPath). UNIQUE(SessionId).
        // v6 (Sprint 9 Task 5): QtoAssignments UNIQUE constraint aggiornato a (SessionId, UniqueId, EpCode, Version)
        //                per supportare il pattern Supersede che inserisce nuove versioni della stessa riga.
        // v5 (Sprint 9): ComputoChapters + QtoAssignments.ComputoChapterId + Sessions.LastUsedComputoChapterId
        // v4 (Sprint 6): ChangeLog + ElementSnapshots + colonne audit su QtoAssignments
        //                (CreatedBy, CreatedAt, ModifiedBy, Version, AuditStatus).
        // v3 (Sprint 4): aggiunta colonna PriceLists.PublicId GUID per riferimenti portabili
        //                nel DataStorage ES del .rvt (ProjectPriceListSnapshot futuro — Sprint 5).
        // v2 (Sprint 2): aggiunta virtual table PriceItems_FTS per ricerca full-text.
        public const int CurrentVersion = 10;

        /// <summary>Ordine di esecuzione degli statement per setup iniziale.</summary>
        public static readonly string[] InitialStatements =
        {
            SchemaInfo,
            Sessions,
            PriceLists,
            PriceItems,
            PriceItemsFts,
            QtoAssignments,
            ManualItems,
            RoomMappings,
            NuoviPrezzi,
            SelectionRules,
            MeasurementRules,
            ModelDiffLog,
            ChangeLog,
            ElementSnapshots,
            EmbeddingCache,
            ComputoChapters,
            ProjectInfo,
            SoaCategories,
            ComuniItaliani,
            RevitParamMapping,
            UserFavorites,
            UserFavoritesIndexCode
        };

        /// <summary>
        /// Migration idempotenti v2→v3: aggiungi colonna PublicId a PriceLists se mancante.
        /// ALTER TABLE ADD COLUMN è idempotente solo con check preventivo — SQLite non supporta
        /// IF NOT EXISTS su ADD COLUMN. La Migration.cs chiamante fa il check via PRAGMA.
        /// </summary>
        public const string MigrateV2ToV3_AddPublicId =
            "ALTER TABLE PriceLists ADD COLUMN PublicId TEXT;";

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
    Id                          INTEGER PRIMARY KEY AUTOINCREMENT,
    ProjectPath                 TEXT NOT NULL,
    ProjectName                 TEXT,
    SessionName                 TEXT,
    Status                      TEXT NOT NULL DEFAULT 'InProgress',
    ActivePhaseId               INTEGER,
    ActivePhaseName             TEXT,
    TotalElements               INTEGER NOT NULL DEFAULT 0,
    TaggedElements              INTEGER NOT NULL DEFAULT 0,
    TotalAmount                 REAL NOT NULL DEFAULT 0,
    LastEpCode                  TEXT,
    Notes                       TEXT,
    CreatedAt                   DATETIME DEFAULT CURRENT_TIMESTAMP,
    LastSavedAt                 DATETIME,
    ModelSnapshotDate           DATETIME,
    LastUsedComputoChapterId    INTEGER NULL REFERENCES ComputoChapters(Id) ON DELETE SET NULL
);
CREATE INDEX IF NOT EXISTS IX_Sessions_ProjectPath ON Sessions(ProjectPath);
CREATE INDEX IF NOT EXISTS IX_Sessions_Status ON Sessions(Status);";

        // --- Listini (riempiti in Sprint 2) ------------------------------------

        // PublicId (GUID v3): identificatore STABILE portabile — usato dal
        // ProjectPriceListSnapshot nel DataStorage del .rvt per riferire un listino
        // anche quando UserLibrary.db non è presente (collaborazione cross-PC).
        public const string PriceLists = @"
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

        // Virtual table FTS5 per ricerca full-text su PriceItems.
        // contentless (content='PriceItems') → nessuna duplicazione dati; rowid = PriceItems.Id.
        // Sync via rebuild esplicito (INSERT INTO PriceItems_FTS(...) VALUES('rebuild')) dopo import batch.
        // unicode61 + remove_diacritics 1 → ricerca insensibile a accenti (es. "caldaia" trova "caldàia").
        public const string PriceItemsFts = @"
CREATE VIRTUAL TABLE IF NOT EXISTS PriceItems_FTS USING fts5(
    Code, Description, ShortDesc, Chapter,
    content='PriceItems', content_rowid='Id',
    tokenize='unicode61 remove_diacritics 1'
);";

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
    CreatedBy         TEXT NOT NULL DEFAULT '',
    CreatedAt         TEXT NOT NULL DEFAULT '',
    ModifiedBy        TEXT,
    Version           INTEGER NOT NULL DEFAULT 1,
    AuditStatus       TEXT NOT NULL DEFAULT 'Active',
    ComputoChapterId  INTEGER NULL REFERENCES ComputoChapters(Id) ON DELETE SET NULL,
    UNIQUE(SessionId, UniqueId, EpCode, Version)
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

        // --- ChangeLog (Sprint 6) -----------------------------------------------

        public const string ChangeLog = @"
CREATE TABLE IF NOT EXISTS ChangeLog (
    ChangeId        INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId       INTEGER NOT NULL,
    ElementUniqueId TEXT NOT NULL,
    PriceItemCode   TEXT NOT NULL,
    ChangeType      TEXT NOT NULL,
    OldValueJson    TEXT,
    NewValueJson    TEXT,
    UserId          TEXT NOT NULL,
    Timestamp       TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_ChangeLog_Session ON ChangeLog(SessionId);";

        // --- ElementSnapshots (Sprint 6) ----------------------------------------

        public const string ElementSnapshots = @"
CREATE TABLE IF NOT EXISTS ElementSnapshots (
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId      INTEGER NOT NULL REFERENCES Sessions(Id) ON DELETE CASCADE,
    ElementId      INTEGER NOT NULL,
    UniqueId       TEXT NOT NULL,
    SnapshotHash   TEXT NOT NULL,
    SnapshotQty    REAL NOT NULL DEFAULT 0,
    AssignedEPJson TEXT NOT NULL DEFAULT '[]',
    LastUpdated    TEXT NOT NULL,
    UNIQUE(SessionId, UniqueId)
);
CREATE INDEX IF NOT EXISTS IX_ElementSnapshots_Session ON ElementSnapshots(SessionId);";

        // --- Migration v3 → v4 (Sprint 6) audit columns on QtoAssignments ------

        public const string MigrateV3ToV4_CreatedBy   = "ALTER TABLE QtoAssignments ADD COLUMN CreatedBy TEXT NOT NULL DEFAULT '';";
        public const string MigrateV3ToV4_CreatedAt   = "ALTER TABLE QtoAssignments ADD COLUMN CreatedAt TEXT NOT NULL DEFAULT '';";
        public const string MigrateV3ToV4_ModifiedBy  = "ALTER TABLE QtoAssignments ADD COLUMN ModifiedBy TEXT;";
        public const string MigrateV3ToV4_Version     = "ALTER TABLE QtoAssignments ADD COLUMN Version INTEGER NOT NULL DEFAULT 1;";
        public const string MigrateV3ToV4_AuditStatus = "ALTER TABLE QtoAssignments ADD COLUMN AuditStatus TEXT NOT NULL DEFAULT 'Active';";

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

        // --- ComputoChapters (Sprint 9) ----------------------------------------

        public const string ComputoChapters = @"
CREATE TABLE IF NOT EXISTS ComputoChapters (
    Id               INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId        INTEGER NOT NULL REFERENCES Sessions(Id) ON DELETE CASCADE,
    ParentChapterId  INTEGER NULL REFERENCES ComputoChapters(Id) ON DELETE CASCADE,
    Code             TEXT NOT NULL,
    Name             TEXT NOT NULL,
    Level            INTEGER NOT NULL,
    SortOrder        INTEGER NOT NULL DEFAULT 0,
    SoaCategoryId    INTEGER NULL REFERENCES SoaCategories(Id) ON DELETE SET NULL,
    CreatedAt        TEXT NOT NULL,
    UNIQUE(SessionId, Code)
);
CREATE INDEX IF NOT EXISTS IX_ComputoChapters_Session ON ComputoChapters(SessionId);
CREATE INDEX IF NOT EXISTS IX_ComputoChapters_Parent ON ComputoChapters(ParentChapterId);
CREATE INDEX IF NOT EXISTS IX_ComputoChapters_Soa ON ComputoChapters(SoaCategoryId);";

        // --- Migration v4 → v5 (Sprint 9) --------------------------------------

        public const string MigrateV4ToV5_AddComputoChapterIdToAssignments =
            "ALTER TABLE QtoAssignments ADD COLUMN ComputoChapterId INTEGER NULL REFERENCES ComputoChapters(Id) ON DELETE SET NULL;";

        public const string MigrateV4ToV5_AddIndexOnComputoChapterId =
            "CREATE INDEX IF NOT EXISTS IX_QtoAssignments_Chapter ON QtoAssignments(ComputoChapterId);";

        public const string MigrateV4ToV5_AddLastUsedChapterToSessions =
            "ALTER TABLE Sessions ADD COLUMN LastUsedComputoChapterId INTEGER NULL REFERENCES ComputoChapters(Id) ON DELETE SET NULL;";

        // --- Migration v5 → v6 (Sprint 9 Task 5): fix UNIQUE constraint su QtoAssignments ----------
        // SQLite non supporta DROP CONSTRAINT → bisogna ricreare la tabella con il nuovo schema.
        // Passi: rename → create new → copy → drop old → recreate indexes.

        public const string MigrateV5ToV6_RenameQtoAssignments =
            "ALTER TABLE QtoAssignments RENAME TO QtoAssignments_v5_bak;";

        public const string MigrateV5ToV6_CreateQtoAssignments = @"
CREATE TABLE QtoAssignments (
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
    CreatedBy         TEXT NOT NULL DEFAULT '',
    CreatedAt         TEXT NOT NULL DEFAULT '',
    ModifiedBy        TEXT,
    Version           INTEGER NOT NULL DEFAULT 1,
    AuditStatus       TEXT NOT NULL DEFAULT 'Active',
    ComputoChapterId  INTEGER NULL REFERENCES ComputoChapters(Id) ON DELETE SET NULL,
    UNIQUE(SessionId, UniqueId, EpCode, Version)
);";

        public const string MigrateV5ToV6_CopyData = @"
INSERT INTO QtoAssignments
    (Id, SessionId, ElementId, UniqueId, Category, FamilyName, PhaseCreated, PhaseDemolished,
     EpCode, EpDescription, Quantity, QuantityGross, QuantityDeducted, Unit, UnitPrice, Total,
     RuleApplied, Source, AssignedAt, ModifiedAt, IsDeleted, IsExcluded, ExclusionReason,
     CreatedBy, CreatedAt, ModifiedBy, Version, AuditStatus, ComputoChapterId)
SELECT
    Id, SessionId, ElementId, UniqueId, Category, FamilyName, PhaseCreated, PhaseDemolished,
    EpCode, EpDescription, Quantity, QuantityGross, QuantityDeducted, Unit, UnitPrice, Total,
    RuleApplied, Source, AssignedAt, ModifiedAt, IsDeleted, IsExcluded, ExclusionReason,
    CreatedBy, CreatedAt, ModifiedBy, Version, AuditStatus, ComputoChapterId
FROM QtoAssignments_v5_bak;";

        public const string MigrateV5ToV6_DropBackup =
            "DROP TABLE QtoAssignments_v5_bak;";

        public const string MigrateV5ToV6_RecreateIndexes = @"
CREATE INDEX IF NOT EXISTS IX_QtoAssignments_Session_Unique ON QtoAssignments(SessionId, UniqueId);
CREATE INDEX IF NOT EXISTS IX_QtoAssignments_EpCode ON QtoAssignments(EpCode);
CREATE INDEX IF NOT EXISTS IX_QtoAssignments_Chapter ON QtoAssignments(ComputoChapterId);";

        // --- ProjectInfo (Sprint 10, schema v7) -------------------------------
        // Metadati di progetto per compatibilità PriMus / D.Lgs. 36/2023.
        // UNIQUE(SessionId) → al massimo una riga ProjectInfo per computo.

        public const string ProjectInfo = @"
CREATE TABLE IF NOT EXISTS ProjectInfo (
    Id                    INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId             INTEGER NOT NULL UNIQUE REFERENCES Sessions(Id) ON DELETE CASCADE,
    DenominazioneOpera    TEXT NOT NULL DEFAULT '',
    Committente           TEXT NOT NULL DEFAULT '',
    Impresa               TEXT NOT NULL DEFAULT '',
    RUP                   TEXT NOT NULL DEFAULT '',
    DirettoreLavori       TEXT NOT NULL DEFAULT '',
    Luogo                 TEXT NOT NULL DEFAULT '',
    Comune                TEXT NOT NULL DEFAULT '',
    Provincia             TEXT NOT NULL DEFAULT '',
    DataComputo           TEXT,
    DataPrezzi            TEXT,
    RiferimentoPrezzario  TEXT NOT NULL DEFAULT '',
    CIG                   TEXT NOT NULL DEFAULT '',
    CUP                   TEXT NOT NULL DEFAULT '',
    RibassoPercentuale    REAL NOT NULL DEFAULT 0,
    LogoPath              TEXT NOT NULL DEFAULT '',
    UpdatedAt             TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_ProjectInfo_Session ON ProjectInfo(SessionId);";

        // --- SoaCategories (Sprint 10 step 2, schema v8) ----------------------
        // Elenco normativo OG/OS (D.Lgs. 36/2023 All. II.12) — seedato al primo
        // creation con i dati di <see cref="Models.SoaCategorySeed"/>. Read-only
        // runtime (nessun CRUD utente).

        public const string SoaCategories = @"
CREATE TABLE IF NOT EXISTS SoaCategories (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    Code         TEXT NOT NULL UNIQUE,
    Description  TEXT NOT NULL DEFAULT '',
    Type         TEXT NOT NULL CHECK (Type IN ('OG','OS')),
    SortOrder    INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS IX_SoaCategories_Type ON SoaCategories(Type);
CREATE INDEX IF NOT EXISTS IX_SoaCategories_SortOrder ON SoaCategories(SortOrder);";

        // --- comuni_italiani (Infoproj v2, schema v9) ----------------------
        // Dataset ISTAT comuni per cascata Provincia → Comune.
        // Tabella vive solo in UserLibrary.db (seed da CSV embedded).
        // Nel .cme viene creata ma rimane vuota (useremo sempre UserLibrary).

        public const string ComuniItaliani = @"
CREATE TABLE IF NOT EXISTS comuni_italiani (
    CodiceIstat     TEXT PRIMARY KEY,
    Comune          TEXT NOT NULL,
    ProvinciaSigla  TEXT NOT NULL,
    ProvinciaNome   TEXT NOT NULL,
    Regione         TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_comuni_prov ON comuni_italiani(ProvinciaSigla);
CREATE INDEX IF NOT EXISTS idx_comuni_nome ON comuni_italiani(Comune COLLATE NOCASE);";

        // --- RevitParamMapping (Infoproj v2, schema v9) ----------------------
        // Mapping configurabile parametri Revit → campi Informazioni Progetto.
        // SkipIfFilled persiste la preferenza della checkbox "Non sovrascrivere
        // campi già compilati" (default 1 = ON). È per-riga per permettere
        // override granulari in futuro.

        public const string RevitParamMapping = @"
CREATE TABLE IF NOT EXISTS RevitParamMapping (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId       INTEGER NOT NULL REFERENCES Sessions(Id) ON DELETE CASCADE,
    FieldKey        TEXT NOT NULL,
    ParamName       TEXT,
    IsBuiltIn       INTEGER NOT NULL DEFAULT 0,
    SkipIfFilled    INTEGER NOT NULL DEFAULT 1,
    UNIQUE(SessionId, FieldKey)
);";

        // --- UserFavorites (Listino preferiti, schema v10) ----------------------
        // Lista preferiti utente in UserLibrary.db (globale per utente).
        // Vive anche nel .cme ma rimane vuota (preferiti sono globali per utente).
        // PriceItemId è NULL-able: se l'item viene cancellato dalla libreria, il preferito
        // rimane con solo i dati storici (Code, Description, UnitPrice) per preservare
        // la visibilità all'utente ("questo item non è più nel listino, rimuovi?").

        public const string UserFavorites = @"
CREATE TABLE IF NOT EXISTS UserFavorites (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    PriceItemId  INTEGER NULL,
    Code         TEXT NOT NULL,
    Description  TEXT NOT NULL DEFAULT '',
    Unit         TEXT NOT NULL DEFAULT '',
    UnitPrice    REAL NOT NULL DEFAULT 0,
    ListName     TEXT NOT NULL DEFAULT '',
    ListId       INTEGER NULL,
    AddedAt      TEXT NOT NULL,
    Note         TEXT NOT NULL DEFAULT '',
    UNIQUE(Code, ListId)
);";

        public const string UserFavoritesIndexCode =
            "CREATE INDEX IF NOT EXISTS idx_favorites_code ON UserFavorites(Code COLLATE NOCASE);";

        // --- Migration v7 → v8 (Sprint 10 step 2) -----------------------------

        public const string MigrateV7ToV8_AddSoaCategoryIdToChapters =
            "ALTER TABLE ComputoChapters ADD COLUMN SoaCategoryId INTEGER NULL REFERENCES SoaCategories(Id) ON DELETE SET NULL;";

        public const string MigrateV7ToV8_AddIndexOnSoaCategoryId =
            "CREATE INDEX IF NOT EXISTS IX_ComputoChapters_Soa ON ComputoChapters(SoaCategoryId);";

        // --- Migration v8 → v9 (Infoproj v2) --------------------------------

        public const string MigrateV8ToV9_CreateComuniItaliani =
            @"CREATE TABLE IF NOT EXISTS comuni_italiani (
                CodiceIstat     TEXT PRIMARY KEY,
                Comune          TEXT NOT NULL,
                ProvinciaSigla  TEXT NOT NULL,
                ProvinciaNome   TEXT NOT NULL,
                Regione         TEXT NOT NULL
            );";

        public const string MigrateV8ToV9_IndexComuniProv =
            "CREATE INDEX IF NOT EXISTS idx_comuni_prov ON comuni_italiani(ProvinciaSigla);";

        public const string MigrateV8ToV9_IndexComuniNome =
            "CREATE INDEX IF NOT EXISTS idx_comuni_nome ON comuni_italiani(Comune COLLATE NOCASE);";

        public const string MigrateV8ToV9_CreateRevitParamMapping =
            @"CREATE TABLE IF NOT EXISTS RevitParamMapping (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId       INTEGER NOT NULL REFERENCES Sessions(Id) ON DELETE CASCADE,
                FieldKey        TEXT NOT NULL,
                ParamName       TEXT,
                IsBuiltIn       INTEGER NOT NULL DEFAULT 0,
                SkipIfFilled    INTEGER NOT NULL DEFAULT 1,
                UNIQUE(SessionId, FieldKey)
            );";

        // --- Migration v9 → v10 (Listino preferiti) ----------------------------

        public const string MigrateV9ToV10_CreateUserFavorites =
            @"CREATE TABLE IF NOT EXISTS UserFavorites (
                Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                PriceItemId  INTEGER NULL,
                Code         TEXT NOT NULL,
                Description  TEXT NOT NULL DEFAULT '',
                Unit         TEXT NOT NULL DEFAULT '',
                UnitPrice    REAL NOT NULL DEFAULT 0,
                ListName     TEXT NOT NULL DEFAULT '',
                ListId       INTEGER NULL,
                AddedAt      TEXT NOT NULL,
                Note         TEXT NOT NULL DEFAULT '',
                UNIQUE(Code, ListId)
            );";

        public const string MigrateV9ToV10_IndexFavoritesCode =
            "CREATE INDEX IF NOT EXISTS idx_favorites_code ON UserFavorites(Code COLLATE NOCASE);";
    }
}
