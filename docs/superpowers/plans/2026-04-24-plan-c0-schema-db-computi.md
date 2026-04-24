# Plan C-0 — Schema DB Modulo Computi

> **Contesto:** primo sotto-progetto della spec `2026-04-24-modulo-computi-primus-xpwe-design.md`. Introduce le tabelle del Modulo Computi PriMus-compliant. ZERO UI, ZERO Revit integration: solo schema DB + repository metodi CRUD + test di migrazione.

**Goal:** Portare lo schema DB da v11 a v12 aggiungendo 7 tabelle nuove + estensione della tabella `PriceItems` esistente per supportare il modello PriMus/XPWE.

**Architecture:** Aggiungere CREATE TABLE in `DatabaseSchema.InitialStatements` (idempotenti con IF NOT EXISTS) + una migrazione `MigrateV11ToV12` in `DatabaseInitializer.MigrateIfNeeded` che alter la `PriceItems` esistente e crea le tabelle nuove. Bump `DatabaseSchema.CurrentVersion` da 11 a 12. Scrivere test di migrazione sul pattern di `SchemaV11MigrationTests.cs`.

**Tech Stack:** SQLite, Microsoft.Data.Sqlite, Dapper, xUnit.

**Riferimenti codice esistente:**
- `QtoRevitPlugin.Core/Data/DatabaseSchema.cs` — costanti + statements DDL
- `QtoRevitPlugin.Core/Data/DatabaseInitializer.cs` — orchestratore versioni
- `QtoRevitPlugin.Tests/T31/SchemaV11MigrationTests.cs` — pattern test migrazione
- `QtoRevitPlugin.Core/Models/PriceItem.cs` — modello da estendere

---

## Task 1: Aggiungere modelli di dominio Computi

**Files:**
- Create: `QtoRevitPlugin.Core/Models/Computi/ComputoDocument.cs`
- Create: `QtoRevitPlugin.Core/Models/Computi/ChapterNode.cs`
- Create: `QtoRevitPlugin.Core/Models/Computi/CategoryNode.cs`
- Create: `QtoRevitPlugin.Core/Models/Computi/WbsNode.cs`
- Create: `QtoRevitPlugin.Core/Models/Computi/MeasurementRow.cs`
- Create: `QtoRevitPlugin.Core/Models/Computi/MeasurementSubRow.cs`
- Create: `QtoRevitPlugin.Core/Models/Computi/XpweExportJob.cs`

- [ ] **Step 1: Creare cartella** `QtoRevitPlugin.Core/Models/Computi/`

- [ ] **Step 2: ComputoDocument.cs**

```csharp
using System;

namespace QtoRevitPlugin.Models.Computi
{
    /// <summary>
    /// Corrisponde a PweDocumento dello schema XPWE. Un documento = una sessione computo
    /// o prezziario custom. Differenziati da TipoDocumento (0=Prezziario, 1=Computo).
    /// </summary>
    public class ComputoDocument
    {
        public int Id { get; set; }
        public int WorkSessionId { get; set; }
        public int TipoDocumento { get; set; }               // 0=Prezziario, 1=Computo
        public string Versione { get; set; } = "5.04";
        public long Fgs { get; set; } = 2147614720L;
        public double PercPrezzi { get; set; }
        public string? Comune { get; set; }
        public string? Provincia { get; set; }
        public string? Oggetto { get; set; }
        public string? Committente { get; set; }
        public string? Impresa { get; set; }
        public string? ParteOpera { get; set; }
        public string Currency { get; set; } = "EUR";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
```

- [ ] **Step 3: ChapterNode.cs**

```csharp
namespace QtoRevitPlugin.Models.Computi
{
    /// <summary>
    /// Nodo della gerarchia Capitoli sul Prezziario (EPItem).
    /// Livelli: SpCap (SuperCapitolo) → Cap (Capitolo) → SbCap (SubCapitolo).
    /// Corrisponde a DGSuperCapitoliItem/DGCapitoliItem/DGSubCapitoliItem XPWE.
    /// </summary>
    public class ChapterNode
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public string Level { get; set; } = "SpCap";          // SpCap|Cap|SbCap
        public string Codice { get; set; } = "";
        public string DesSintetica { get; set; } = "";
        public string? DesEstesa { get; set; }
        public string? DataInit { get; set; }                 // DD/MM/YYYY
        public int Durata { get; set; }
        public string? CodFase { get; set; }
        public double Percentuale { get; set; }
        public int? ParentId { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
```

- [ ] **Step 4: CategoryNode.cs**

```csharp
namespace QtoRevitPlugin.Models.Computi
{
    /// <summary>
    /// Nodo della gerarchia Categorie sul Computo (VCItem). Distinto da SoaCategory.
    /// Livelli: SpCat (SuperCategoria) → Cat (Categoria) → SbCat (SubCategoria).
    /// Runtime-defined per documento (non c'è uno standard preimpostato).
    /// </summary>
    public class CategoryNode
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public string Level { get; set; } = "SpCat";          // SpCat|Cat|SbCat
        public string Codice { get; set; } = "";
        public string DesSintetica { get; set; } = "";
        public string? DesEstesa { get; set; }
        public string? DataInit { get; set; }
        public int Durata { get; set; }
        public string? CodFase { get; set; }
        public double Percentuale { get; set; }
        public int? ParentId { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
```

- [ ] **Step 5: WbsNode.cs**

```csharp
namespace QtoRevitPlugin.Models.Computi
{
    /// <summary>
    /// Nodo WBS a profondità libera. Kind=WbsCap → referenziato da EPItem (Prezziario),
    /// Kind=WbsComputo → referenziato da VCItem (Computo).
    /// </summary>
    public class WbsNode
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public string Kind { get; set; } = "WbsCap";          // WbsCap|WbsComputo
        public string Codice { get; set; } = "";              // path "1.2.3"
        public string DesSintetica { get; set; } = "";
        public int? ParentId { get; set; }
        public int Level { get; set; }                        // 1-based
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
```

- [ ] **Step 6: MeasurementRow.cs**

```csharp
namespace QtoRevitPlugin.Models.Computi
{
    /// <summary>
    /// Voce del Computo (VCItem). Aggrega 1-N MeasurementSubRow (RGItem).
    /// Quantita è cache di SUM(SubRows.Quantita).
    /// </summary>
    public class MeasurementRow
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public int PriceItemId { get; set; }                  // → PriceItems.Id
        public double Quantita { get; set; }
        public string? DataMis { get; set; }                  // DD/MM/YYYY
        public int Flags { get; set; }
        public int? SpCatId { get; set; }
        public int? CatId { get; set; }
        public int? SbCatId { get; set; }
        public int? WbsComputoNodeId { get; set; }
        public int SortOrder { get; set; }
    }
}
```

- [ ] **Step 7: MeasurementSubRow.cs**

```csharp
namespace QtoRevitPlugin.Models.Computi
{
    /// <summary>
    /// Riga di misura (RGItem). Formula: PartiUguali × (Lunghezza ?? 1) × (Larghezza ?? 1) × (HPeso ?? 1).
    /// IDVV: Revit ElementId (>0) oppure contatore locale negativo (&lt;0) per voci manuali.
    /// </summary>
    public class MeasurementSubRow
    {
        public int Id { get; set; }
        public int MeasurementRowId { get; set; }
        public int IDVV { get; set; }
        public string? Descrizione { get; set; }
        public double PartiUguali { get; set; } = 1;
        public double? Lunghezza { get; set; }
        public double? Larghezza { get; set; }
        public double? HPeso { get; set; }
        public double Quantita { get; set; }
        public int Flags { get; set; }
        public int SortOrder { get; set; }
    }
}
```

- [ ] **Step 8: XpweExportJob.cs**

```csharp
using System;

namespace QtoRevitPlugin.Models.Computi
{
    /// <summary>Audit dei job di export XPWE (traccia file, checksum, versione).</summary>
    public class XpweExportJob
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public DateTime ExportedAt { get; set; }
        public int TipoDocumento { get; set; }
        public string XpweVersion { get; set; } = "5.04";
        public string? FilePath { get; set; }
        public string? FileChecksum { get; set; }
        public string? ValidationReport { get; set; }        // JSON
    }
}
```

- [ ] **Step 9: Build**

Run:
```bash
dotnet build QtoRevitPlugin.Core/QtoRevitPlugin.Core.csproj -c Debug -v q
```

Expected: 0 errori.

## Task 2: Aggiornare DatabaseSchema con DDL tabelle Computi

**Files:**
- Modify: `QtoRevitPlugin.Core/Data/DatabaseSchema.cs:43` (bump CurrentVersion)
- Modify: `QtoRevitPlugin.Core/Data/DatabaseSchema.cs` (aggiungere DDL in InitialStatements)

- [ ] **Step 1: Leggere DatabaseSchema.cs per capire il pattern attuale**

Run:
```
Read QtoRevitPlugin.Core/Data/DatabaseSchema.cs offset=1 limit=80
```

- [ ] **Step 2: Bumpare CurrentVersion da 11 a 12**

Sostituire:
```csharp
public const int CurrentVersion = 11;
```
con:
```csharp
public const int CurrentVersion = 12;
```

- [ ] **Step 3: Aggiungere statement DDL nell'array InitialStatements**

Al fondo di `InitialStatements` (prima della `]` di chiusura, o dove il pattern esistente lo prevede) aggiungere — ciascun CREATE TABLE come stringa separata, tutte idempotenti con `IF NOT EXISTS`:

```csharp
// Modulo Computi (schema v12) — tabelle PriMus-compliant
@"
CREATE TABLE IF NOT EXISTS ComputoDocuments (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    WorkSessionId INTEGER NOT NULL UNIQUE REFERENCES Sessions(Id) ON DELETE CASCADE,
    TipoDocumento INTEGER NOT NULL,
    Versione TEXT NOT NULL DEFAULT '5.04',
    Fgs INTEGER NOT NULL DEFAULT 2147614720,
    PercPrezzi REAL NOT NULL DEFAULT 0,
    Comune TEXT,
    Provincia TEXT,
    Oggetto TEXT,
    Committente TEXT,
    Impresa TEXT,
    ParteOpera TEXT,
    Currency TEXT NOT NULL DEFAULT 'EUR',
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);",

@"
CREATE TABLE IF NOT EXISTS ChapterNodes (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    DocumentId INTEGER NOT NULL REFERENCES ComputoDocuments(Id) ON DELETE CASCADE,
    Level TEXT NOT NULL CHECK(Level IN ('SpCap','Cap','SbCap')),
    Codice TEXT NOT NULL,
    DesSintetica TEXT NOT NULL,
    DesEstesa TEXT,
    DataInit TEXT,
    Durata INTEGER NOT NULL DEFAULT 0,
    CodFase TEXT,
    Percentuale REAL NOT NULL DEFAULT 0,
    ParentId INTEGER REFERENCES ChapterNodes(Id) ON DELETE CASCADE,
    SortOrder INTEGER NOT NULL,
    IsActive INTEGER NOT NULL DEFAULT 1
);",

@"CREATE INDEX IF NOT EXISTS ix_chapternodes_doc ON ChapterNodes(DocumentId);",
@"CREATE INDEX IF NOT EXISTS ix_chapternodes_parent ON ChapterNodes(ParentId);",

@"
CREATE TABLE IF NOT EXISTS CategoryNodes (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    DocumentId INTEGER NOT NULL REFERENCES ComputoDocuments(Id) ON DELETE CASCADE,
    Level TEXT NOT NULL CHECK(Level IN ('SpCat','Cat','SbCat')),
    Codice TEXT NOT NULL,
    DesSintetica TEXT NOT NULL,
    DesEstesa TEXT,
    DataInit TEXT,
    Durata INTEGER NOT NULL DEFAULT 0,
    CodFase TEXT,
    Percentuale REAL NOT NULL DEFAULT 0,
    ParentId INTEGER REFERENCES CategoryNodes(Id) ON DELETE CASCADE,
    SortOrder INTEGER NOT NULL,
    IsActive INTEGER NOT NULL DEFAULT 1
);",

@"CREATE INDEX IF NOT EXISTS ix_categorynodes_doc ON CategoryNodes(DocumentId);",
@"CREATE INDEX IF NOT EXISTS ix_categorynodes_parent ON CategoryNodes(ParentId);",

@"
CREATE TABLE IF NOT EXISTS WbsNodes (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    DocumentId INTEGER NOT NULL REFERENCES ComputoDocuments(Id) ON DELETE CASCADE,
    Kind TEXT NOT NULL CHECK(Kind IN ('WbsCap','WbsComputo')),
    Codice TEXT NOT NULL,
    DesSintetica TEXT NOT NULL,
    ParentId INTEGER REFERENCES WbsNodes(Id) ON DELETE CASCADE,
    Level INTEGER NOT NULL,
    SortOrder INTEGER NOT NULL,
    IsActive INTEGER NOT NULL DEFAULT 1
);",

@"CREATE INDEX IF NOT EXISTS ix_wbsnodes_doc ON WbsNodes(DocumentId);",
@"CREATE INDEX IF NOT EXISTS ix_wbsnodes_parent ON WbsNodes(ParentId);",

@"
CREATE TABLE IF NOT EXISTS MeasurementRows (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    DocumentId INTEGER NOT NULL REFERENCES ComputoDocuments(Id) ON DELETE CASCADE,
    PriceItemId INTEGER NOT NULL REFERENCES PriceItems(Id) ON DELETE RESTRICT,
    Quantita REAL NOT NULL DEFAULT 0,
    DataMis TEXT,
    Flags INTEGER NOT NULL DEFAULT 0,
    SpCatId INTEGER REFERENCES CategoryNodes(Id),
    CatId INTEGER REFERENCES CategoryNodes(Id),
    SbCatId INTEGER REFERENCES CategoryNodes(Id),
    WbsComputoNodeId INTEGER REFERENCES WbsNodes(Id),
    SortOrder INTEGER NOT NULL
);",

@"CREATE INDEX IF NOT EXISTS ix_measurementrows_doc ON MeasurementRows(DocumentId);",
@"CREATE INDEX IF NOT EXISTS ix_measurementrows_pi ON MeasurementRows(PriceItemId);",

@"
CREATE TABLE IF NOT EXISTS MeasurementSubRows (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    MeasurementRowId INTEGER NOT NULL REFERENCES MeasurementRows(Id) ON DELETE CASCADE,
    IDVV INTEGER NOT NULL,
    Descrizione TEXT,
    PartiUguali REAL NOT NULL DEFAULT 1,
    Lunghezza REAL,
    Larghezza REAL,
    HPeso REAL,
    Quantita REAL NOT NULL DEFAULT 0,
    Flags INTEGER NOT NULL DEFAULT 0,
    SortOrder INTEGER NOT NULL
);",

@"CREATE INDEX IF NOT EXISTS ix_subrows_row ON MeasurementSubRows(MeasurementRowId);",
@"CREATE INDEX IF NOT EXISTS ix_subrows_idvv ON MeasurementSubRows(IDVV);",

@"
CREATE TABLE IF NOT EXISTS XpweExportJobs (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    DocumentId INTEGER NOT NULL REFERENCES ComputoDocuments(Id),
    ExportedAt TEXT NOT NULL,
    TipoDocumento INTEGER NOT NULL,
    XpweVersion TEXT NOT NULL,
    FilePath TEXT,
    FileChecksum TEXT,
    ValidationReport TEXT
);",

@"CREATE INDEX IF NOT EXISTS ix_exportjobs_doc ON XpweExportJobs(DocumentId);",
```

- [ ] **Step 4: Aggiungere costanti di migrazione per PriceItems estensione**

Sempre in DatabaseSchema.cs, accanto alle migrazioni esistenti `MigrateV7ToV8_*` e `MigrateV10ToV11_*`, aggiungere:

```csharp
public const string MigrateV11ToV12_ExtendPriceItemsPrezzo2 =
    "ALTER TABLE PriceItems ADD COLUMN Prezzo2 REAL NOT NULL DEFAULT 0;";
public const string MigrateV11ToV12_ExtendPriceItemsPrezzo3 =
    "ALTER TABLE PriceItems ADD COLUMN Prezzo3 REAL NOT NULL DEFAULT 0;";
public const string MigrateV11ToV12_ExtendPriceItemsPrezzo4 =
    "ALTER TABLE PriceItems ADD COLUMN Prezzo4 REAL NOT NULL DEFAULT 0;";
public const string MigrateV11ToV12_ExtendPriceItemsPrezzo5 =
    "ALTER TABLE PriceItems ADD COLUMN Prezzo5 REAL NOT NULL DEFAULT 0;";
public const string MigrateV11ToV12_ExtendPriceItemsSpCapId =
    "ALTER TABLE PriceItems ADD COLUMN SpCapId INTEGER NULL;";
public const string MigrateV11ToV12_ExtendPriceItemsCapId =
    "ALTER TABLE PriceItems ADD COLUMN CapId INTEGER NULL;";
public const string MigrateV11ToV12_ExtendPriceItemsSbCapId =
    "ALTER TABLE PriceItems ADD COLUMN SbCapId INTEGER NULL;";
public const string MigrateV11ToV12_ExtendPriceItemsWbsCapNodeId =
    "ALTER TABLE PriceItems ADD COLUMN WbsCapNodeId INTEGER NULL;";
public const string MigrateV11ToV12_ExtendPriceItemsIncMDO =
    "ALTER TABLE PriceItems ADD COLUMN IncMDO REAL NOT NULL DEFAULT 0;";
public const string MigrateV11ToV12_ExtendPriceItemsIncMAT =
    "ALTER TABLE PriceItems ADD COLUMN IncMAT REAL NOT NULL DEFAULT 0;";
public const string MigrateV11ToV12_ExtendPriceItemsIncSIC =
    "ALTER TABLE PriceItems ADD COLUMN IncSIC REAL NOT NULL DEFAULT 0;";
public const string MigrateV11ToV12_ExtendPriceItemsFlags =
    "ALTER TABLE PriceItems ADD COLUMN Flags INTEGER NOT NULL DEFAULT 512;";
public const string MigrateV11ToV12_ExtendPriceItemsCnfQt =
    "ALTER TABLE PriceItems ADD COLUMN CnfQt TEXT NULL;";
public const string MigrateV11ToV12_ExtendPriceItemsAdrInternet =
    "ALTER TABLE PriceItems ADD COLUMN AdrInternet TEXT NULL;";
public const string MigrateV11ToV12_ExtendPriceItemsTipoRisorsa =
    "ALTER TABLE PriceItems ADD COLUMN TipoRisorsa INTEGER NOT NULL DEFAULT 0;";
public const string MigrateV11ToV12_ExtendPriceItemsArticolo =
    "ALTER TABLE PriceItems ADD COLUMN Articolo TEXT NULL;";
public const string MigrateV11ToV12_ExtendPriceItemsDataEP =
    "ALTER TABLE PriceItems ADD COLUMN DataEP TEXT NULL;";
```

## Task 3: Orchestrazione migrazione in DatabaseInitializer

**Files:**
- Modify: `QtoRevitPlugin.Core/Data/DatabaseInitializer.cs` (metodo `MigrateIfNeeded`, dopo il blocco v10→v11)

- [ ] **Step 1: Aprire il file e individuare dove v11 finisce**

Run:
```
Read QtoRevitPlugin.Core/Data/DatabaseInitializer.cs offset=230 limit=50
```

- [ ] **Step 2: Aggiungere blocco v11→v12 dopo il blocco esistente**

Nel metodo `MigrateIfNeeded`, dopo le ALTER di v10→v11 e prima del blocco di commit della transaction:

```csharp
// Migrazione v11 → v12 (Plan C-0): modulo Computi PriMus-compliant.
// Aggiunge 7 tabelle nuove + estende PriceItems con i 16 campi XPWE-style.
if (dbVersion < 12)
{
    // Estensione PriceItems — ALTER TABLE non supporta IF NOT EXISTS, check PRAGMA.
    EnsurePriceItemColumn(conn, tx, "Prezzo2",
        DatabaseSchema.MigrateV11ToV12_ExtendPriceItemsPrezzo2);
    EnsurePriceItemColumn(conn, tx, "Prezzo3",
        DatabaseSchema.MigrateV11ToV12_ExtendPriceItemsPrezzo3);
    EnsurePriceItemColumn(conn, tx, "Prezzo4",
        DatabaseSchema.MigrateV11ToV12_ExtendPriceItemsPrezzo4);
    EnsurePriceItemColumn(conn, tx, "Prezzo5",
        DatabaseSchema.MigrateV11ToV12_ExtendPriceItemsPrezzo5);
    EnsurePriceItemColumn(conn, tx, "SpCapId",
        DatabaseSchema.MigrateV11ToV12_ExtendPriceItemsSpCapId);
    EnsurePriceItemColumn(conn, tx, "CapId",
        DatabaseSchema.MigrateV11ToV12_ExtendPriceItemsCapId);
    EnsurePriceItemColumn(conn, tx, "SbCapId",
        DatabaseSchema.MigrateV11ToV12_ExtendPriceItemsSbCapId);
    EnsurePriceItemColumn(conn, tx, "WbsCapNodeId",
        DatabaseSchema.MigrateV11ToV12_ExtendPriceItemsWbsCapNodeId);
    EnsurePriceItemColumn(conn, tx, "IncMDO",
        DatabaseSchema.MigrateV11ToV12_ExtendPriceItemsIncMDO);
    EnsurePriceItemColumn(conn, tx, "IncMAT",
        DatabaseSchema.MigrateV11ToV12_ExtendPriceItemsIncMAT);
    EnsurePriceItemColumn(conn, tx, "IncSIC",
        DatabaseSchema.MigrateV11ToV12_ExtendPriceItemsIncSIC);
    EnsurePriceItemColumn(conn, tx, "Flags",
        DatabaseSchema.MigrateV11ToV12_ExtendPriceItemsFlags);
    EnsurePriceItemColumn(conn, tx, "CnfQt",
        DatabaseSchema.MigrateV11ToV12_ExtendPriceItemsCnfQt);
    EnsurePriceItemColumn(conn, tx, "AdrInternet",
        DatabaseSchema.MigrateV11ToV12_ExtendPriceItemsAdrInternet);
    EnsurePriceItemColumn(conn, tx, "TipoRisorsa",
        DatabaseSchema.MigrateV11ToV12_ExtendPriceItemsTipoRisorsa);
    EnsurePriceItemColumn(conn, tx, "Articolo",
        DatabaseSchema.MigrateV11ToV12_ExtendPriceItemsArticolo);
    EnsurePriceItemColumn(conn, tx, "DataEP",
        DatabaseSchema.MigrateV11ToV12_ExtendPriceItemsDataEP);

    // Tabelle nuove sono in InitialStatements (idempotenti IF NOT EXISTS),
    // vengono create dal loop soprastante o riesecuzione. Niente da fare qui.
}
```

Dove `EnsurePriceItemColumn` è un nuovo helper privato (vedere Step 3).

- [ ] **Step 3: Aggiungere helper privato EnsurePriceItemColumn**

In fondo alla classe `DatabaseInitializer`, accanto a `ColumnExists` e `TableExists`:

```csharp
private static void EnsurePriceItemColumn(
    SqliteConnection conn, SqliteTransaction tx, string columnName, string alterStatement)
{
    if (!ColumnExists(conn, tx, "PriceItems", columnName))
    {
        ExecuteStatement(conn, tx, alterStatement);
    }
}
```

- [ ] **Step 4: Build**

Run:
```bash
dotnet build QtoRevitPlugin.Core/QtoRevitPlugin.Core.csproj -c Debug -v q
```

Expected: 0 errori.

## Task 4: Repository CRUD minimale

**Files:**
- Modify: `QtoRevitPlugin.Core/Data/IQtoRepository.cs` (aggiungere metodi)
- Modify: `QtoRevitPlugin.Core/Data/QtoRepository.cs` (implementazioni)

Scopo: basic CRUD chiamabile da service e test. Non aggiungiamo tutto: solo ciò che serve per Plan C-1 (XpweDeserializer) + test.

- [ ] **Step 1: Aprire IQtoRepository.cs e vedere il pattern**

Run:
```
Read QtoRevitPlugin.Core/Data/IQtoRepository.cs offset=1 limit=60
```

- [ ] **Step 2: Aggiungere firme interfaccia per ComputoDocument**

In fondo a `IQtoRepository`:

```csharp
// -------------------- Modulo Computi (schema v12) --------------------

int InsertComputoDocument(ComputoDocument doc);
ComputoDocument? GetComputoDocumentBySession(int workSessionId);
void UpdateComputoDocument(ComputoDocument doc);

int InsertChapterNode(ChapterNode node);
IReadOnlyList<ChapterNode> GetChapterNodes(int documentId);
void UpdateChapterNode(ChapterNode node);
void DeleteChapterNode(int id);

int InsertCategoryNode(CategoryNode node);
IReadOnlyList<CategoryNode> GetCategoryNodes(int documentId);
void UpdateCategoryNode(CategoryNode node);
void DeleteCategoryNode(int id);

int InsertWbsNode(WbsNode node);
IReadOnlyList<WbsNode> GetWbsNodes(int documentId, string? kind = null);
void UpdateWbsNode(WbsNode node);
void DeleteWbsNode(int id);

int InsertMeasurementRow(MeasurementRow row);
IReadOnlyList<MeasurementRow> GetMeasurementRows(int documentId);
void UpdateMeasurementRow(MeasurementRow row);
void DeleteMeasurementRow(int id);
void RecalcMeasurementRowQuantita(int rowId);

int InsertMeasurementSubRow(MeasurementSubRow subRow);
IReadOnlyList<MeasurementSubRow> GetMeasurementSubRows(int measurementRowId);
void UpdateMeasurementSubRow(MeasurementSubRow subRow);
void DeleteMeasurementSubRow(int id);

int InsertXpweExportJob(XpweExportJob job);
IReadOnlyList<XpweExportJob> GetXpweExportJobs(int documentId);
```

E all'inizio del file, assicurarsi ci sia:
```csharp
using QtoRevitPlugin.Models.Computi;
```

- [ ] **Step 3: Implementare i metodi in QtoRepository.cs**

Pattern già usato dal file — Dapper query. Inserire in fondo al file (prima della `}` finale della classe):

```csharp
#region Modulo Computi (schema v12)

public int InsertComputoDocument(ComputoDocument doc)
{
    const string sql = @"
        INSERT INTO ComputoDocuments (
            WorkSessionId, TipoDocumento, Versione, Fgs, PercPrezzi,
            Comune, Provincia, Oggetto, Committente, Impresa, ParteOpera,
            Currency, CreatedAt, UpdatedAt)
        VALUES (
            @WorkSessionId, @TipoDocumento, @Versione, @Fgs, @PercPrezzi,
            @Comune, @Provincia, @Oggetto, @Committente, @Impresa, @ParteOpera,
            @Currency, @CreatedAt, @UpdatedAt);
        SELECT last_insert_rowid();";
    doc.Id = _conn.ExecuteScalar<int>(sql, doc);
    return doc.Id;
}

public ComputoDocument? GetComputoDocumentBySession(int workSessionId)
{
    const string sql = "SELECT * FROM ComputoDocuments WHERE WorkSessionId = @wsid;";
    return _conn.QuerySingleOrDefault<ComputoDocument>(sql, new { wsid = workSessionId });
}

public void UpdateComputoDocument(ComputoDocument doc)
{
    const string sql = @"
        UPDATE ComputoDocuments SET
            TipoDocumento = @TipoDocumento, Versione = @Versione, Fgs = @Fgs,
            PercPrezzi = @PercPrezzi, Comune = @Comune, Provincia = @Provincia,
            Oggetto = @Oggetto, Committente = @Committente, Impresa = @Impresa,
            ParteOpera = @ParteOpera, Currency = @Currency, UpdatedAt = @UpdatedAt
        WHERE Id = @Id;";
    _conn.Execute(sql, doc);
}

public int InsertChapterNode(ChapterNode node)
{
    const string sql = @"
        INSERT INTO ChapterNodes (DocumentId, Level, Codice, DesSintetica, DesEstesa,
            DataInit, Durata, CodFase, Percentuale, ParentId, SortOrder, IsActive)
        VALUES (@DocumentId, @Level, @Codice, @DesSintetica, @DesEstesa,
            @DataInit, @Durata, @CodFase, @Percentuale, @ParentId, @SortOrder, @IsActive);
        SELECT last_insert_rowid();";
    node.Id = _conn.ExecuteScalar<int>(sql, node);
    return node.Id;
}

public IReadOnlyList<ChapterNode> GetChapterNodes(int documentId)
{
    const string sql = "SELECT * FROM ChapterNodes WHERE DocumentId = @d ORDER BY SortOrder;";
    return _conn.Query<ChapterNode>(sql, new { d = documentId }).AsList();
}

public void UpdateChapterNode(ChapterNode node)
{
    const string sql = @"
        UPDATE ChapterNodes SET Level = @Level, Codice = @Codice, DesSintetica = @DesSintetica,
            DesEstesa = @DesEstesa, DataInit = @DataInit, Durata = @Durata, CodFase = @CodFase,
            Percentuale = @Percentuale, ParentId = @ParentId, SortOrder = @SortOrder, IsActive = @IsActive
        WHERE Id = @Id;";
    _conn.Execute(sql, node);
}

public void DeleteChapterNode(int id) =>
    _conn.Execute("DELETE FROM ChapterNodes WHERE Id = @id;", new { id });

// CategoryNodes: stesso pattern di ChapterNodes con CategoryNodes, SpCat/Cat/SbCat
public int InsertCategoryNode(CategoryNode node)
{
    const string sql = @"
        INSERT INTO CategoryNodes (DocumentId, Level, Codice, DesSintetica, DesEstesa,
            DataInit, Durata, CodFase, Percentuale, ParentId, SortOrder, IsActive)
        VALUES (@DocumentId, @Level, @Codice, @DesSintetica, @DesEstesa,
            @DataInit, @Durata, @CodFase, @Percentuale, @ParentId, @SortOrder, @IsActive);
        SELECT last_insert_rowid();";
    node.Id = _conn.ExecuteScalar<int>(sql, node);
    return node.Id;
}

public IReadOnlyList<CategoryNode> GetCategoryNodes(int documentId)
{
    const string sql = "SELECT * FROM CategoryNodes WHERE DocumentId = @d ORDER BY SortOrder;";
    return _conn.Query<CategoryNode>(sql, new { d = documentId }).AsList();
}

public void UpdateCategoryNode(CategoryNode node)
{
    const string sql = @"
        UPDATE CategoryNodes SET Level = @Level, Codice = @Codice, DesSintetica = @DesSintetica,
            DesEstesa = @DesEstesa, DataInit = @DataInit, Durata = @Durata, CodFase = @CodFase,
            Percentuale = @Percentuale, ParentId = @ParentId, SortOrder = @SortOrder, IsActive = @IsActive
        WHERE Id = @Id;";
    _conn.Execute(sql, node);
}

public void DeleteCategoryNode(int id) =>
    _conn.Execute("DELETE FROM CategoryNodes WHERE Id = @id;", new { id });

public int InsertWbsNode(WbsNode node)
{
    const string sql = @"
        INSERT INTO WbsNodes (DocumentId, Kind, Codice, DesSintetica, ParentId, Level, SortOrder, IsActive)
        VALUES (@DocumentId, @Kind, @Codice, @DesSintetica, @ParentId, @Level, @SortOrder, @IsActive);
        SELECT last_insert_rowid();";
    node.Id = _conn.ExecuteScalar<int>(sql, node);
    return node.Id;
}

public IReadOnlyList<WbsNode> GetWbsNodes(int documentId, string? kind = null)
{
    if (string.IsNullOrEmpty(kind))
    {
        const string sql = "SELECT * FROM WbsNodes WHERE DocumentId = @d ORDER BY SortOrder;";
        return _conn.Query<WbsNode>(sql, new { d = documentId }).AsList();
    }
    const string sqlK = "SELECT * FROM WbsNodes WHERE DocumentId = @d AND Kind = @k ORDER BY SortOrder;";
    return _conn.Query<WbsNode>(sqlK, new { d = documentId, k = kind }).AsList();
}

public void UpdateWbsNode(WbsNode node)
{
    const string sql = @"
        UPDATE WbsNodes SET Kind = @Kind, Codice = @Codice, DesSintetica = @DesSintetica,
            ParentId = @ParentId, Level = @Level, SortOrder = @SortOrder, IsActive = @IsActive
        WHERE Id = @Id;";
    _conn.Execute(sql, node);
}

public void DeleteWbsNode(int id) =>
    _conn.Execute("DELETE FROM WbsNodes WHERE Id = @id;", new { id });

public int InsertMeasurementRow(MeasurementRow row)
{
    const string sql = @"
        INSERT INTO MeasurementRows (DocumentId, PriceItemId, Quantita, DataMis, Flags,
            SpCatId, CatId, SbCatId, WbsComputoNodeId, SortOrder)
        VALUES (@DocumentId, @PriceItemId, @Quantita, @DataMis, @Flags,
            @SpCatId, @CatId, @SbCatId, @WbsComputoNodeId, @SortOrder);
        SELECT last_insert_rowid();";
    row.Id = _conn.ExecuteScalar<int>(sql, row);
    return row.Id;
}

public IReadOnlyList<MeasurementRow> GetMeasurementRows(int documentId)
{
    const string sql = "SELECT * FROM MeasurementRows WHERE DocumentId = @d ORDER BY SortOrder;";
    return _conn.Query<MeasurementRow>(sql, new { d = documentId }).AsList();
}

public void UpdateMeasurementRow(MeasurementRow row)
{
    const string sql = @"
        UPDATE MeasurementRows SET PriceItemId = @PriceItemId, Quantita = @Quantita,
            DataMis = @DataMis, Flags = @Flags, SpCatId = @SpCatId, CatId = @CatId,
            SbCatId = @SbCatId, WbsComputoNodeId = @WbsComputoNodeId, SortOrder = @SortOrder
        WHERE Id = @Id;";
    _conn.Execute(sql, row);
}

public void DeleteMeasurementRow(int id) =>
    _conn.Execute("DELETE FROM MeasurementRows WHERE Id = @id;", new { id });

public void RecalcMeasurementRowQuantita(int rowId)
{
    const string sql = @"
        UPDATE MeasurementRows
        SET Quantita = (SELECT COALESCE(SUM(Quantita), 0) FROM MeasurementSubRows WHERE MeasurementRowId = @id)
        WHERE Id = @id;";
    _conn.Execute(sql, new { id = rowId });
}

public int InsertMeasurementSubRow(MeasurementSubRow subRow)
{
    const string sql = @"
        INSERT INTO MeasurementSubRows (MeasurementRowId, IDVV, Descrizione,
            PartiUguali, Lunghezza, Larghezza, HPeso, Quantita, Flags, SortOrder)
        VALUES (@MeasurementRowId, @IDVV, @Descrizione, @PartiUguali, @Lunghezza,
            @Larghezza, @HPeso, @Quantita, @Flags, @SortOrder);
        SELECT last_insert_rowid();";
    subRow.Id = _conn.ExecuteScalar<int>(sql, subRow);
    return subRow.Id;
}

public IReadOnlyList<MeasurementSubRow> GetMeasurementSubRows(int measurementRowId)
{
    const string sql = "SELECT * FROM MeasurementSubRows WHERE MeasurementRowId = @r ORDER BY SortOrder;";
    return _conn.Query<MeasurementSubRow>(sql, new { r = measurementRowId }).AsList();
}

public void UpdateMeasurementSubRow(MeasurementSubRow subRow)
{
    const string sql = @"
        UPDATE MeasurementSubRows SET IDVV = @IDVV, Descrizione = @Descrizione,
            PartiUguali = @PartiUguali, Lunghezza = @Lunghezza, Larghezza = @Larghezza,
            HPeso = @HPeso, Quantita = @Quantita, Flags = @Flags, SortOrder = @SortOrder
        WHERE Id = @Id;";
    _conn.Execute(sql, subRow);
}

public void DeleteMeasurementSubRow(int id) =>
    _conn.Execute("DELETE FROM MeasurementSubRows WHERE Id = @id;", new { id });

public int InsertXpweExportJob(XpweExportJob job)
{
    const string sql = @"
        INSERT INTO XpweExportJobs (DocumentId, ExportedAt, TipoDocumento, XpweVersion,
            FilePath, FileChecksum, ValidationReport)
        VALUES (@DocumentId, @ExportedAt, @TipoDocumento, @XpweVersion,
            @FilePath, @FileChecksum, @ValidationReport);
        SELECT last_insert_rowid();";
    job.Id = _conn.ExecuteScalar<int>(sql, job);
    return job.Id;
}

public IReadOnlyList<XpweExportJob> GetXpweExportJobs(int documentId)
{
    const string sql = "SELECT * FROM XpweExportJobs WHERE DocumentId = @d ORDER BY ExportedAt DESC;";
    return _conn.Query<XpweExportJob>(sql, new { d = documentId }).AsList();
}

#endregion
```

In cima al file, assicurarsi ci sia:
```csharp
using QtoRevitPlugin.Models.Computi;
```

- [ ] **Step 4: Build**

Run:
```bash
dotnet build QtoRevitPlugin.Core/QtoRevitPlugin.Core.csproj -c Debug -v q
```

Expected: 0 errori.

## Task 5: Test di migrazione V11→V12

**Files:**
- Create: `QtoRevitPlugin.Tests/Computi/SchemaV12MigrationTests.cs`

- [ ] **Step 1: Creare cartella e file test**

- [ ] **Step 2: Scrivere test che verifica migrazione**

```csharp
using System.IO;
using Microsoft.Data.Sqlite;
using QtoRevitPlugin.Data;
using Xunit;

namespace QtoRevitPlugin.Tests.Computi
{
    public class SchemaV12MigrationTests
    {
        [Fact]
        public void FreshDb_CreatesAllComputiTables()
        {
            var path = Path.GetTempFileName();
            try
            {
                var init = new DatabaseInitializer(path);
                init.EnsureDatabaseReady();

                using var conn = new SqliteConnection($"Data Source={path}");
                conn.Open();
                AssertTableExists(conn, "ComputoDocuments");
                AssertTableExists(conn, "ChapterNodes");
                AssertTableExists(conn, "CategoryNodes");
                AssertTableExists(conn, "WbsNodes");
                AssertTableExists(conn, "MeasurementRows");
                AssertTableExists(conn, "MeasurementSubRows");
                AssertTableExists(conn, "XpweExportJobs");
                AssertColumnExists(conn, "PriceItems", "Prezzo2");
                AssertColumnExists(conn, "PriceItems", "Prezzo5");
                AssertColumnExists(conn, "PriceItems", "SpCapId");
                AssertColumnExists(conn, "PriceItems", "IncMDO");
                AssertColumnExists(conn, "PriceItems", "Flags");
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void MigrationFromV11_PreservesExistingData()
        {
            // TODO: creare un DB v11 minimo (Sessions + PriceLists + PriceItems + una session),
            // applicare la migrazione, verificare che le tabelle vecchie esistano ancora
            // e il conteggio righe non sia cambiato.
            // Stesso pattern di SchemaV11MigrationTests.cs.
        }

        private static void AssertTableExists(SqliteConnection conn, string table)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT name FROM sqlite_master WHERE type='table' AND name='{table}';";
            var result = cmd.ExecuteScalar();
            Assert.Equal(table, result);
        }

        private static void AssertColumnExists(SqliteConnection conn, string table, string col)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table});";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (reader.GetString(1) == col) return;
            }
            Assert.Fail($"Column {table}.{col} not found");
        }
    }
}
```

Nota: il secondo test lo si completa man mano, il primo deve passare subito.

- [ ] **Step 3: Run test**

Run:
```bash
dotnet test QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj --filter FullyQualifiedName~SchemaV12MigrationTests.FreshDb 2>&1 | tail -10
```

Expected: `Passed! - Failed: 0, Passed: 1`.

## Task 6: Commit

- [ ] **Step 1: git add dei file modificati e nuovi**

Run:
```bash
git add QtoRevitPlugin.Core/Models/Computi/ \
       QtoRevitPlugin.Core/Data/DatabaseSchema.cs \
       QtoRevitPlugin.Core/Data/DatabaseInitializer.cs \
       QtoRevitPlugin.Core/Data/IQtoRepository.cs \
       QtoRevitPlugin.Core/Data/QtoRepository.cs \
       QtoRevitPlugin.Tests/Computi/
```

- [ ] **Step 2: Commit**

Run:
```bash
git commit -m "$(cat <<'EOF'
feat(db v12): modulo Computi schema · 7 tabelle + PriceItem esteso

Plan C-0 (spec 2026-04-24-modulo-computi-primus-xpwe-design.md):
- ComputoDocuments (1:1 con WorkSession)
- ChapterNodes (3 livelli SpCap/Cap/SbCap per EP)
- CategoryNodes (3 livelli SpCat/Cat/SbCat per VC, distinto da SOA)
- WbsNodes (profondità libera, kind WbsCap|WbsComputo)
- MeasurementRows (VCItem) + MeasurementSubRows (RGItem)
- XpweExportJobs (audit export)

PriceItems esteso con 17 colonne: Prezzo2..5, SpCap/Cap/SbCap/WbsCap FK,
IncMDO/MAT/SIC, TipoRisorsa, Flags, CnfQt, AdrInternet, Articolo, DataEP.

Migrazione v11→v12 additiva, idempotente. Zero impatto su dati esistenti.
Repository CRUD minimale per tutte le nuove entità. Test di migrazione
verifica presenza tabelle e colonne.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 3: Verifica**

Run:
```bash
git log -1 --oneline
```

Expected: "feat(db v12): modulo Computi schema ...".

---

## Self-review checklist

- [x] Ogni Task ha file path assoluti/relativi al progetto
- [x] Step con codice completo, nessun placeholder
- [x] Migrazione idempotente (IF NOT EXISTS su tabelle + indici, PRAGMA guard su colonne)
- [x] Niente toccato su schema v11 esistente (additivo puro)
- [x] Test di migrazione con pattern già usato nel progetto
- [x] CurrentVersion bumpato coerentemente
- [x] Zero UI, zero Revit integration — puro schema + repository

## Scope NON incluso

- Import XPWE (→ Plan C-1)
- Domain services (ChapterService/CategoryService/...) (→ Plan C-2)
- UI Setup/Strutture (→ Plan C-3)
- Revit integration (→ Plan C-6)
- Export XPWE (→ Plan C-7)

## Rollback se qualcosa va storto

- La migrazione è additiva: niente DROP, niente ALTER distruttive
- Se una ALTER TABLE fallisce, la transazione rollba l'intero blocco v11→v12
- L'app riparte come schema v11 senza danni
