# Revit QTO Plugin – Documentazione Tecnica Completa v3.0

## Executive Summary

Plug-in Revit per il Quantity Take-Off (QTO) destinato a professionisti BIM nell'ambito di appalti pubblici italiani (D.Lgs. 36/2023). Il sistema è un motore deterministico basato su regole, con gestione multi-listino, database SQLite per la persistenza incrementale, supporto ai Nuovi Prezzi (NP), gestione fasi Revit, rilevamento modifiche al modello e integrazione opzionale di AI per il mapping semantico. Compatibile con Revit 2022–2026.

---

## 1. Stack Tecnologico

### 1.1 Target Versioni Revit

| Versione | Runtime | Note |
|---|---|---|
| Revit 2022–2024 | .NET Framework 4.8 | API legacy `DisplayUnitType` |
| Revit 2025+ | .NET 8 | Breaking change, `ForgeTypeId` obbligatorio |

Strategia: **multi-targeting** con un unico codebase:

```xml
<TargetFrameworks>net48;net8.0-windows</TargetFrameworks>
<DefineConstants Condition="'$(TargetFramework)'=='net48'">REVIT2024_OR_EARLIER</DefineConstants>
<DefineConstants Condition="'$(TargetFramework)'=='net8.0-windows'">REVIT2025_OR_LATER</DefineConstants>
```

```csharp
#if REVIT2025_OR_LATER
    double area = UnitUtils.ConvertFromInternalUnits(raw, UnitTypeId.SquareMeters);
#else
    double area = UnitUtils.ConvertFromInternalUnits(raw, DisplayUnitType.DUT_SQUARE_METERS);
#endif
```

### 1.2 Dipendenze NuGet

| Libreria | Versione | Scopo |
|---|---|---|
| `RevitAPI` / `RevitAPIUI` | locale da Revit | Core API |
| `NCalc2` | 2.x | Formula engine deterministico |
| `ClosedXML` | 0.102+ | Export Excel |
| `CommunityToolkit.Mvvm` | 8.x | MVVM WPF |
| `Microsoft.Data.Sqlite` | 8.x | Database locale |
| `SQLite-net-pcl` | 1.9+ | ORM SQLite |
| `Revit.Async` | opzionale | Wrapper async/await ExternalEvent |

---

## 2. Architettura del Sistema

### 2.1 Layer Architecture

```
┌──────────────────────────────────────────────────────────────┐
│  UI Layer (WPF + MVVM)                                       │
│  SetupView · PhaseFilterView · SelectionView · TaggingView   │
│  HealthCheckView · ExportView · NpView · SessionView         │
│  DockablePane (Anteprima Live)                               │
├──────────────────────────────────────────────────────────────┤
│  Application Services Layer                                  │
│  QtoCommandOrchestrator · ExternalEventHandlers              │
│  AutoSaveService · SessionManager · ModelDiffService         │
├──────────────────────────────────────────────────────────────┤
│  Core Engine (C# / Revit API)                                │
│  PriceListParser · CategoryMapper · FormulaEngine (NCalc)    │
│  QuantityExtractor · MeasurementRulesEngine                  │
│  ExclusionRulesEngine · HealthCheckEngine                    │
│  NpEngine · ExportEngine                                     │
├──────────────────────────────────────────────────────────────┤
│  Data Layer                                                  │
│  QtoRepository (SQLite) · ExtensibleStorageRepo              │
│  SharedParameterManager · FileRepository                     │
└──────────────────────────────────────────────────────────────┘
```

### 2.2 Threading Model

La Revit API è single-threaded. Tutte le scritture avvengono via `IExternalEventHandler` + `ExternalEvent`. Il ViewModel non tocca mai la Revit API direttamente dal thread WPF.

```csharp
public class WriteQtoHandler : IExternalEventHandler
{
    public List<ElementId> TargetIds { get; set; }
    public string EpCode             { get; set; }
    public string EpShortDesc        { get; set; }

    public void Execute(UIApplication app)
    {
        var doc = app.ActiveUIDocument.Document;
        using var tx = new Transaction(doc, "Assegna QTO");
        tx.Start();
        foreach (var id in TargetIds)
        {
            var elem = doc.GetElement(id);
            elem.LookupParameter("QTO_Codice")?.Set(EpCode);
            elem.LookupParameter("QTO_DescrizioneBreve")?.Set(EpShortDesc);
            elem.LookupParameter("QTO_Stato")?.Set("COMPUTATO");
        }
        tx.Commit();
    }
    public string GetName() => "WriteQtoHandler";
}
```

---

## 3. Struttura del Progetto Visual Studio

```
QtoRevitPlugin/
├── QtoRevitPlugin.csproj              ← multi-target net48;net8.0-windows
├── Application/
│   └── QtoApplication.cs             ← IExternalApplication, ribbon, DockablePane
├── Commands/
│   ├── LaunchQtoCommand.cs
│   └── HealthCheckCommand.cs
├── Core/
│   ├── Parsers/
│   │   ├── DcfParser.cs              ← XML .dcf / .xpwe ACCA PriMus
│   │   ├── ExcelParser.cs
│   │   └── CsvParser.cs
│   ├── Mapping/
│   │   ├── CategoryMapper.cs
│   │   └── FormulaEngine.cs          ← NCalc wrapper
│   ├── Extraction/
│   │   └── QuantityExtractor.cs      ← FilteredElementCollector multi-cat + fase
│   ├── Rules/
│   │   ├── MeasurementRulesEngine.cs ← vuoto per pieno, deduzioni aperture
│   │   └── ExclusionRulesEngine.cs   ← regole esclusione globale
│   ├── Validation/
│   │   └── HealthCheckEngine.cs
│   ├── NuoviPrezzi/
│   │   └── NpEngine.cs
│   ├── Diff/
│   │   └── ModelDiffService.cs       ← rilevamento nuovi/rimossi elementi
│   └── Export/
│       └── ExportEngine.cs
├── Data/
│   ├── QtoRepository.cs              ← SQLite ORM completo
│   ├── ExtensibleStorageRepo.cs
│   └── SharedParameterManager.cs
├── ExternalEvents/
│   ├── WriteParameterHandler.cs
│   ├── IsolateElementsHandler.cs
│   ├── OverrideColorHandler.cs
│   └── NavigateToElementHandler.cs
├── Models/
│   ├── PriceItem.cs
│   ├── PriceList.cs
│   ├── CategoryMapping.cs
│   ├── QtoAssignment.cs
│   ├── QtoResult.cs
│   ├── WorkSession.cs
│   ├── MeasurementRule.cs
│   ├── SelectionRule.cs
│   ├── NuovoPrezzo.cs
│   └── ModelDiffResult.cs
├── Services/
│   ├── AutoSaveService.cs
│   ├── SessionManager.cs
│   └── SmartMappingService.cs        ← AI opzionale
├── UI/
│   ├── Panes/
│   │   └── QtoPreviewPane.xaml(.cs)  ← DockablePane anteprima live
│   ├── Views/
│   │   ├── SetupView.xaml(.cs)       ← listini, regole misura, esclusioni
│   │   ├── PhaseFilterView.xaml(.cs) ← selezione fase Revit (filtro iniziale)
│   │   ├── SelectionView.xaml(.cs)   ← filtri stile Revit + ricerca inline
│   │   ├── TaggingView.xaml(.cs)     ← assegnazione EP, multi-EP, anteprima
│   │   ├── HealthCheckView.xaml(.cs)
│   │   ├── ExportView.xaml(.cs)
│   │   ├── NpView.xaml(.cs)
│   │   └── SessionView.xaml(.cs)     ← gestione sessioni, resume
│   └── ViewModels/  (uno per View)
└── QtoRevitPlugin.addin
```

---

## 4. Database SQLite – Schema Completo

Posizione: `%AppData%\QtoPlugin\db\{NomeProgetto}.db` — un file per ogni `.rvt`.

```sql
-- Sessioni di lavoro
CREATE TABLE Sessions (
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    ProjectPath    TEXT NOT NULL,
    ProjectName    TEXT,
    SessionName    TEXT,
    Status         TEXT DEFAULT 'InProgress', -- InProgress|Completed|Exported
    ActivePhaseId  INTEGER,
    ActivePhaseName TEXT,
    TotalElements  INTEGER DEFAULT 0,
    TaggedElements INTEGER DEFAULT 0,
    TotalAmount    REAL DEFAULT 0,
    LastEpCode     TEXT,
    Notes          TEXT,
    CreatedAt      DATETIME DEFAULT CURRENT_TIMESTAMP,
    LastSavedAt    DATETIME,
    ModelSnapshotDate DATETIME
);

-- Listini prezzi importati
CREATE TABLE PriceLists (
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    Name       TEXT NOT NULL,
    Source     TEXT,
    Version    TEXT,
    Region     TEXT,
    IsActive   INTEGER DEFAULT 1,
    Priority   INTEGER DEFAULT 0,   -- ordine per conflitti codici
    ImportedAt DATETIME,
    RowCount   INTEGER
);

-- Voci di elenco prezzi (inclusi NP)
CREATE TABLE PriceItems (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    PriceListId INTEGER REFERENCES PriceLists(Id),
    Code        TEXT NOT NULL,
    Chapter     TEXT,
    SubChapter  TEXT,
    Description TEXT NOT NULL,
    ShortDesc   TEXT,
    Unit        TEXT,
    UnitPrice   REAL,
    Notes       TEXT,
    IsNP        INTEGER DEFAULT 0,  -- 1 = Nuovo Prezzo
    UNIQUE(PriceListId, Code)
);

-- Indice FTS5 per ricerca full-text < 10ms su 30.000+ voci
CREATE VIRTUAL TABLE PriceItems_FTS USING fts5(
    Code, Description, ShortDesc, Chapter,
    content='PriceItems', content_rowid='Id'
);

-- Assegnazioni EP per elemento Revit
CREATE TABLE QtoAssignments (
    Id               INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId        INTEGER REFERENCES Sessions(Id),
    ElementId        INTEGER NOT NULL,
    UniqueId         TEXT NOT NULL,      -- Revit UniqueId, stabile tra sync
    Category         TEXT,
    FamilyName       TEXT,
    PhaseCreated     TEXT,
    PhaseDemolished  TEXT,
    EpCode           TEXT NOT NULL,
    EpDescription    TEXT,
    Quantity         REAL,
    QuantityGross    REAL,               -- lordo pre-deduzioni
    QuantityDeducted REAL,               -- totale deduzioni aperture
    Unit             TEXT,
    UnitPrice        REAL,
    Total            REAL,
    RuleApplied      TEXT,               -- descrizione regola applicata
    AssignedAt       DATETIME DEFAULT CURRENT_TIMESTAMP,
    ModifiedAt       DATETIME,
    IsDeleted        INTEGER DEFAULT 0,  -- soft delete per audit trail
    IsExcluded       INTEGER DEFAULT 0,
    ExclusionReason  TEXT,
    UNIQUE(SessionId, UniqueId, EpCode)
);

-- Nuovi Prezzi con analisi
CREATE TABLE NuoviPrezzi (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId    INTEGER REFERENCES Sessions(Id),
    Code         TEXT NOT NULL,          -- es. "NP.001"
    Description  TEXT NOT NULL,
    ShortDesc    TEXT,
    Unit         TEXT,
    Manodopera   REAL DEFAULT 0,
    Materiali    REAL DEFAULT 0,
    Noli         REAL DEFAULT 0,
    Trasporti    REAL DEFAULT 0,
    SpGenerali   REAL DEFAULT 15,        -- % 13-17% per D.Lgs. 36/2023
    UtileImpresa REAL DEFAULT 10,        -- % utile impresa
    UnitPrice    REAL,                   -- calcolato da NpEngine
    RibassoAsta  REAL DEFAULT 0,         -- opzionale, parere MIT 3545/2025
    Status       TEXT DEFAULT 'Bozza',   -- Bozza|Concordato|Approvato
    NoteAnalisi  TEXT,
    CreatedAt    DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- Regole di selezione salvate (preset filtri)
CREATE TABLE SelectionRules (
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    Name      TEXT NOT NULL,
    RuleJson  TEXT NOT NULL,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- Regole di misurazione per progetto
CREATE TABLE MeasurementRules (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId    INTEGER REFERENCES Sessions(Id),
    RuleId       TEXT NOT NULL,
    CategoryName TEXT,
    Description  TEXT,
    Method       TEXT,     -- VuotoPerPieno|Netto|Lordo|LuceNetta
    Threshold    REAL,
    AlwaysDeduct INTEGER DEFAULT 0,
    Source       TEXT,
    IsActive     INTEGER DEFAULT 1
);

-- Log modifiche al modello tra sessioni
CREATE TABLE ModelDiffLog (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId       INTEGER REFERENCES Sessions(Id),
    UniqueId        TEXT NOT NULL,
    ChangeType      TEXT NOT NULL,  -- Added|Removed|Modified
    Category        TEXT,
    FamilyName      TEXT,
    PhaseCreated    TEXT,
    PhaseDemolished TEXT,
    DetectedAt      DATETIME DEFAULT CURRENT_TIMESTAMP,
    Resolved        INTEGER DEFAULT 0
);
```

---

## 5. Flusso di Lavoro Completo (Workflow)

### Fase 0 — Filtro Iniziale: Selezione Fase Revit

**Primo passo obbligatorio** ad ogni apertura di sessione. Il plug-in legge dinamicamente le fasi del progetto:

```csharp
public List<Phase> GetProjectPhases(Document doc)
{
    return new FilteredElementCollector(doc)
        .OfClass(typeof(Phase))
        .Cast<Phase>()
        .OrderBy(p => p.get_Parameter(
            BuiltInParameter.PHASE_SEQUENCE_NUMBER)?.AsInteger() ?? 0)
        .ToList();
}
```

L'utente sceglie quali tipi di elementi computare:

```
FASE DI LAVORO
☑ Nuova costruzione  → Phase Created    = [Progetto ▼]
☑ Demolizioni        → Phase Demolished = [Progetto ▼]
☐ Esistente (solo visualizzazione)
```

I filtri di fase sono applicati tramite `ElementPhaseStatusFilter`:

```csharp
// Nuova costruzione
var newFilter = new ElementPhaseStatusFilter(phaseId, ElementOnPhaseStatus.New);
// Demolizioni
var demolishedFilter = new ElementPhaseStatusFilter(phaseId, ElementOnPhaseStatus.Demolished);
```

Quando si lavora su elementi demoliti, la ricerca nel listino si apre automaticamente sul capitolo Demolizioni del prezzario attivo.

### Fase 1 — Setup: Listini, Regole, Esclusioni

**Caricamento multi-listino**: supporto simultaneo di più prezzari (es. Toscana 2024 + DEI 2025 + Nuovi Prezzi). La priorità gestisce i conflitti di codice.

**Regole di misurazione**: preset per prezzario regionale, modificabili per progetto, salvate nell'ES del `.rvt`.

**Regole di esclusione globale**: elementi che corrispondono a criteri specifici (es. `Fase = Esistente`, `Commenti contiene "NP"`) vengono esclusi automaticamente dalla computazione.

**Importazione asincrona** con progress bar (500 voci/batch, FTS5 ricostruito ogni 2000):

```csharp
public async Task ImportAsync(string filePath, IProgress<ImportProgress> progress)
{
    var items = await Task.Run(() => _parser.Parse(filePath));
    const int batchSize = 500;
    for (int i = 0; i < items.Count; i += batchSize)
    {
        await Task.Run(() => _repo.BulkInsert(items.Skip(i).Take(batchSize).ToList()));
        if (i % 2000 == 0) await Task.Run(() => _repo.RebuildFtsIndex());
        progress.Report(new ImportProgress { Current = i, Total = items.Count });
    }
}
```

### Fase 2 — Selezione Elementi con FilterBuilder

La `SelectionView` replica il sistema filtri nativi di Revit con ricerca testuale inline aggiuntiva:

```csharp
public List<Element> GetFilteredElements(Document doc, FilterCriteria criteria)
{
    var phaseFilter = new ElementPhaseStatusFilter(
        criteria.PhaseId, criteria.PhaseStatus);

    var paramRules = criteria.Rules.Select(r =>
        ParameterFilterRuleFactory.CreateRule(
            new ElementId(r.BuiltInParameter ?? r.SharedParamGuid),
            r.Evaluator, r.Value)).ToList();

    var elements = new FilteredElementCollector(doc)
        .OfCategory(criteria.BuiltInCategory)
        .WherePasses(phaseFilter)
        .WherePasses(new ElementParameterFilter(paramRules))
        .WhereElementIsNotElementType()
        .ToElements();

    // Ricerca testuale inline post-filter
    if (!string.IsNullOrEmpty(criteria.InlineSearch))
    {
        var s = criteria.InlineSearch.ToLower();
        elements = elements.Where(e =>
            e.Name.ToLower().Contains(s) ||
            (e.LookupParameter(criteria.SearchParameter)?.AsString()
             ?.ToLower().Contains(s) ?? false)).ToList();
    }
    return elements;
}
```

**Comandi finali selezione**:

| Comando | API Revit |
|---|---|
| Seleziona | `uidoc.Selection.SetElementIds(ids)` |
| Isola | `view.IsolateElementsTemporary(ids)` |
| Nascondi | `view.HideElements(ids)` |
| Togli Isolamento | `view.DisableTemporaryViewMode(...)` |
| **➤ INSERISCI** | `externalEvent.Raise()` → scrittura + calcolo + DB |

### Fase 3 — Mappatura Categorie e Shared Parameters

**Shared Parameters creati automaticamente**:

| Parametro | Tipo | Contenuto |
|---|---|---|
| `QTO_Codice` | Text | Codice EP ultima/primaria assegnazione |
| `QTO_DescrizioneBreve` | Text | Descrizione sintetica voce EP |
| `QTO_Stato` | Text | Stato computazione |

Valori `QTO_Stato`: *(vuoto)* / `COMPUTATO` / `ESCLUSO` / `NP` / `PARZIALE`

**Persistenza multi-EP sull'elemento** via Extensible Storage (campo `IList<string>` con JSON serializzato per ogni assegnazione): permette a un singolo elemento di essere assegnato a più voci EP (es. muro → muratura + intonaco + isolamento).

**Categorie e parametri geometrici supportati**:

| Categoria | Parametri | Sorgente |
|---|---|---|
| `OST_Walls` | Area, Volume, Lunghezza | A (famiglia host) |
| `OST_Floors` | Area, Volume | A |
| `OST_Ceilings` | Area | A |
| `OST_Roofs` | Area, Volume | A |
| `OST_Columns` / `OST_StructuralColumns` | Volume, Lunghezza, Conteggio | A |
| `OST_Doors` / `OST_Windows` | Conteggio, Area apertura | A |
| `OST_GenericModel` | Area, Volume, Lunghezza, Conteggio | A |
| `OST_StructuralFraming` | Volume, Lunghezza | A |
| `OST_Rooms` | Area, Volume, Perimeter, Unbounded Height + shared params custom | B (NCalc su Room) |
| `OST_MEPSpaces` | Area, Volume, Computation Height + MEP params | B (NCalc su Space) |

> **Tre sorgenti di quantità** (vedi QTO-Implementazioni-v3.md §I12, §I13):
> - **Sorgente A** — parametri built-in di elementi host (estrazione diretta via `FilteredElementCollector`)
> - **Sorgente B** — formule NCalc su Room/Space per voci non direttamente agganciabili a famiglie
> - **Sorgente C** — voci manuali orfane (oneri, trasporti, noli) persistite in tabella `ManualItems`
> Il totalizzatore di export somma A + B + C per codice EP, con rilevamento discrepanze di prezzo unitario.

### Fase 4 — Regole di Misurazione

Il `MeasurementRulesEngine` corregge le quantità grezze di Revit applicando le norme dei capitolati italiani prima del calcolo finale.

**Regole default (configurabili per progetto)**:

| Categoria | Regola | Soglia default |
|---|---|---|
| Murature (mc) | Detrai aperture netta > X m² | 1,00 m² |
| Murature | Piattabande luci ≤ X m: non detrarre | 1,50 m |
| Murature | Canne fumarie, incassature | Mai detrarre |
| Intonaci sp. > 15 cm (mq) | Vuoto per pieno, detrai > X m² | 2,00 m² |
| Tinteggiature calce/tempera | Vuoto per pieno, detrai > X m² | 4,00 m² |
| Altre tinteggiature | Vuoto per pieno, detrai > X m² | 2,50 m² |
| Solai misti lat.-cem. (mq) | Luce netta al rustico, detrai fori > X m² | 1,00 m² |
| Tramezzi foglio/testa (mq) | Detrai vuoti > X m² | 1,00 m² |
| Pareti cartongesso | Detrai > X m² | 3,00 m² |
| Demolizioni (mc) | Vuoto per pieno, volumi geometrici | — |

```csharp
public double ApplyMeasurementRules(Element elem, double rawQuantity,
                                     MeasurementRule rule)
{
    if (rule.Method == MeasureMethod.Lordo) return rawQuantity;
    var openingAreas = GetHostedOpeningAreas(elem);
    double deduction = openingAreas
        .Where(a => rule.AlwaysDeduct || a > rule.Threshold)
        .Sum();
    return Math.Max(0.0, rawQuantity - deduction);
}
```

Nota esplicativa nella preview: `"45,2 m² lordi − 3,1 m² (2 aperture > 2 m²) = 42,1 m² netti"`

### Fase 5 — Tagging e Feedback Visivo

La `TaggingView` è una finestra modeless con:
- Ricerca FTS5 multi-listino (< 10ms su 30.000+ voci)
- Pannello "Assegnazioni Esistenti" per gli elementi selezionati
- Conferma/Annulla prima di qualsiasi scrittura sul modello

**Color coding stato elementi**:

| Colore | Stato |
|---|---|
| Verde `(100,200,100)` | Computato (≥ 1 EP) |
| Rosso `(220,80,80)` | Non computato |
| Arancione `(255,140,0)` | Aggiunto dopo prima computazione |
| Giallo `(255,200,0)` | Parziale (EP assegnato, quantità = 0) |
| Blu `(80,130,220)` | Multi-EP (≥ 2 assegnazioni) |
| Grigio/halftone | Escluso (manuale o filtro) |

### Fase 6 — Ricerca Multi-Listino

```csharp
public List<PriceItem> Search(string query, List<int> activeListIds, int limit = 100)
{
    var ftsQuery = string.Join(" AND ",
        query.Trim().Split(' ').Select(t => $"{t}*"));
    return _conn.Query<PriceItem>($@"
        SELECT pi.*, pl.Name as ListName
        FROM PriceItems pi
        JOIN PriceLists pl ON pi.PriceListId = pl.Id
        JOIN PriceItems_FTS fts ON fts.rowid = pi.Id
        WHERE PriceItems_FTS MATCH ?
          AND pi.PriceListId IN ({string.Join(",", activeListIds)})
        ORDER BY bm25(PriceItems_FTS) ASC, pl.Id ASC
        LIMIT ?", ftsQuery, limit);
}
```

Navigazione alternativa per capitoli/sottocapitoli (albero laterale).
Badge colorato per listino di provenienza (`T` = Toscana, `D` = DEI, `NP` = Nuovo Prezzo).

### Fase 7 — Nuovi Prezzi (NP)

I NP sono voci per lavorazioni non presenti nell'elenco prezzi contrattuale (art. 5, All. II.14 e art. 120, D.Lgs. 36/2023).

**Formula analisi prezzi**:
```
CT = Manodopera + Materiali + Noli + Trasporti
NP = CT × (1 + SpGenerali%) × (1 + UtileImpresa%)
```
Spese generali: 13–17% | Utile impresa: 10%

**Workflow NP**: Bozza → Concordato (contraddittorio DL/Impresa) → Approvato (RUP)

Le voci approvate sono inserite nel DB come `PriceItems` con `IsNP = 1` e ricercabili come qualsiasi altra voce.

### Fase 8 — Health Check

**Regole di validazione pre-export**:

| Stato | Condizione |
|---|---|
| ✅ Computato | `QtoAssignments.Count > 0` |
| ⚠ Parziale | EP assegnato, `Quantity = 0` |
| ❌ Non computato | `QtoAssignments` vuoto |
| 🔄 Multi-EP | `QtoAssignments.Count > 1` |
| 🚫 Escluso manuale | `IsExcluded = true` |
| 🚫 Escluso filtro | `QTO_Stato = "NP"` |

Doppio click → `NavigateToElementHandler` → selezione e zoom sull'elemento in Revit.

### Fase 9 — Rilevamento Modifiche al Modello

Quando il `.rvt` viene modificato tra sessioni, il `ModelDiffService` rileva nuovi/rimossi elementi confrontando gli `UniqueId` nel DB con quelli attuali nel modello:

```csharp
public ModelDiffResult ComputeDiff(Document doc,
                                    List<CategoryMapping> mappings,
                                    int sessionId)
{
    var currentIds = new FilteredElementCollector(doc)
        .WherePasses(new ElementMultiCategoryFilter(
            mappings.Select(m => new ElementId((int)m.BuiltInCategory)).ToList()))
        .WhereElementIsNotElementType()
        .WherePasses(new ElementPhaseStatusFilter(_activePhaseId,
            ElementOnPhaseStatus.New))
        .Select(e => e.UniqueId)
        .ToHashSet();

    var processedIds = _repo.GetAllUniqueIds(sessionId).ToHashSet();

    return new ModelDiffResult
    {
        NewElements     = currentIds.Except(processedIds).ToList(),
        RemovedElements = processedIds.Except(currentIds).ToList()
    };
}
```

Dialog di notifica all'apertura sessione con lista elementi aggiunti/rimossi e pulsante "Mostra nuovi elementi" che li isola nella vista 3D pronti per il tagging.

### Fase 10 — Calcolo Deterministico

Per ogni elemento:
```
Totale_Voce = Quantità_Netta × f(PrezzoListino, Parametri_Config)
```

Con NCalc per formule personalizzate (es. `Prezzo * (1 + PercSicurezza / 100)`).

### Fase 11 — Anteprima Live (DockablePane)

Registrato in `OnStartup`, rimane agganciato alla UI di Revit durante tutta la sessione.

**Tab 1 – Selezione Corrente**: quantità lorde, deduzioni aperture, nette, prezzo unitario e totale voce aggiornati in real-time prima della conferma.

**Tab 2 – Riepilogo Complessivo**: tabella cumulativa tutte le voci EP con barra avanzamento e totale sessione.

**Status bar**: `[💾 Salvato 17:32] | Sessione: "Computo Lotto A" | 142/318 (44,7%) | € 48.320 | [● Sync Revit ✓]`

### Fase 12 — Export

| Formato | Contenuto |
|---|---|
| Excel (.xlsx) | Computo completo con formattazione, NP evidenziati, foglio analisi NP |
| TSV | Testo tabulato compatibile PriMus/importazione SA |
| Delta report | Solo elementi aggiunti/modificati dall'ultima export |

---

## 6. Gestione Sessioni e Persistenza

### AutoSalvataggio

- **Ad ogni INSERISCI**: flush immediato SQLite (< 5ms) — priorità massima
- **Timer 5 min**: aggiorna `LastSavedAt` e `LastEpCode` — silenzioso
- **Recovery automatico**: confronto SQLite vs ES al riapertura documento, proposta ripristino

### Funzioni File

| Comando | Comportamento |
|---|---|
| Salva | Flush SQLite + sync ES Revit |
| Salva con Nome | Fork della sessione nel DB |
| Chiudi | Salva + `Status = InProgress` + log resume point |
| Riapri | Lista sessioni per il .rvt, scelta e resume |
| Esporta Excel | ClosedXML da DB corrente |
| Esporta TSV | Compatibile PriMus |
| Nuovo computo | Nuova sessione vuota |

### Dialog Riprendi Sessione

```
● Computo Lotto A – Strutture
  Ultima modifica: 21/04/2026 17:32
  Avanzamento: 142/318 (44,7%)
  Ultima voce: B.02.004 – Cls Rck 30 MPa
  Importo parziale: € 48.320,00

○ Computo Lotto A – Finiture  ✅ 100%
  Importo totale: € 112.500,00

[Riprendi]  [Nuovo Computo]  [Annulla]
```

Il resume point ripristina: listino caricato, regole di misurazione, filtro fase, ultimo filtro categoria e scroll position EP.

---

## 7. Piano di Sviluppo – Sprint Planning

### Parametri

- **Team**: 1 sviluppatore senior C#/Revit API (80% del tempo)
- **Sprint**: 2 settimane
- **Totale stimato**: 20 settimane (10 sprint)

### Roadmap Sprint

| Sprint | Contenuto | Settimane |
|---|---|---|
| **0** | Setup VS multi-target, ribbon, MVVM template, CI/CD | 1–2 |
| **1** | DB SQLite schema completo, SessionManager, AutoSave, Recovery | 3–4 |
| **2** | Parser DCF/Excel/CSV, FTS5, multi-listino UI, importazione async | 5–6 |
| **3** | Extensible Storage, Shared Parameters, ForgeTypeId multi-versione | 7–8 |
| **4** | PhaseFilterView (fasi Revit), SelectionView (FilterBuilder + ricerca inline + esclusioni) | 9–10 |
| **5** | TaggingView: scrittura bidirezionale, multi-EP, color coding, pannello assegnazioni | 11–12 |
| **6** | MeasurementRulesEngine (vuoto per pieno + deduzioni), HealthCheckView | 13–14 |
| **7** | ModelDiffService (rilevamento nuovi/rimossi), DockablePane anteprima live | 15–16 |
| **8** | NpEngine: analisi prezzi, workflow Bozza→Concordato→Approvato, export NP | 17–18 |
| **9** | Export Excel/TSV, gestione sessioni completa, AI Smart Mapping, installer multi-versione | 19–20 |

### Gantt

```
Settimane:  1  2  3  4  5  6  7  8  9 10 11 12 13 14 15 16 17 18 19 20
Sprint 0:   ████
Sprint 1:         ████
Sprint 2:               ████
Sprint 3:                     ████
Sprint 4:                           ████
Sprint 5:                                 ████
Sprint 6:                                       ████
Sprint 7:                                             ████
Sprint 8:                                                   ████
Sprint 9:                                                         ████
```

### Tabella Rischi

| Rischio | Prob. | Impatto | Mitigazione |
|---|---|---|---|
| Struttura XML .dcf non documentata ufficialmente | Alta | Media | Analisi empirica file campione prezzario Toscana |
| Breaking changes API Revit 2026+ | Media | Alta | Conditional compilation + test per ogni versione |
| Prestazioni su modelli > 50.000 elementi | Media | Alta | FEC con filtri rapidi, lazy loading, paginazione ResultGrid |
| ES schema conflict tra versioni plug-in | Bassa | Alta | Schema versioning GUID distinti + migrazione automatica |
| Thread violation Revit API da WPF | Media | Alta | Tutti i write via ExternalEvent, nessuna API Revit dal thread WPF |
| UniqueId instabile dopo detach/relink | Bassa | Media | Fallback su ElementId + hash famiglia+tipo+posizione |

---

## 8. Conformità Normativa

- **ISO 19650 / UNI PdR 74:2019**: parametri QTO inseriti nel Piano di Gestione Informativa come parametri documentati
- **D.Lgs. 36/2023 art. 5, All. II.14**: supporto nativo NP con analisi prezzi strutturata (CT + SG + utile)
- **D.Lgs. 36/2023 art. 120**: export NP per perizie di variante e atti di sottomissione entro il quinto d'obbligo
- **Parere MIT n. 3545/2025**: ribasso d'asta sui NP gestito come campo opzionale
- **Prezzari regionali**: parser testato su Toscana, Lombardia, DEI; ACCA PW-CONV per conversione formati
- **Tracciabilità audit trail**: soft delete + `ModifiedAt` su ogni assegnazione; `ElementId` Revit in ogni riga export

---

## 9. Riferimenti Tecnici

- Revit API Docs: https://www.revitapidocs.com/
- Autodesk Blog – ForgeTypeId and Units Revisited: https://blog.autodesk.io/forgetypeid-and-units-revisited/
- archi-lab.net – Extensible Storage: https://archi-lab.net/what-why-and-how-of-the-extensible-storage/
- archi-lab.net – Revit 2022 Unit Changes: https://archi-lab.net/handling-the-revit-2022-unit-changes/
- archi-lab.net – Multi-version maintenance: https://archi-lab.net/how-to-maintain-revit-plug-ins-for-multiple-versions/
- GitHub – NCalc: https://github.com/ncalc/ncalc
- LearnRevitAPI.com – Automate Shared Parameters
- LearnRevitAPI.com – Override Graphics in View
- LearnRevitAPI.com – ElementParameterFilter
- EasyRevitAPI.com – MVVM Pattern for Revit
- Building Coder – External Events: https://jeremytammik.github.io/tbc/a/0743_external_event.htm
- Revit API Forum – .NET 8 Migration
- D.Lgs. 36/2023 art. 120 – Varianti in corso d'opera
- D.Lgs. 36/2023 All. II.14 art. 5 – Determinazione nuovi prezzi
- Parere MIT n. 3545/2025 – NP e ribasso d'asta
