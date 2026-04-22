# Sprint 9 — Riconciliazione Attiva + Report/Export Multi-formato

**Data:** 2026-04-22
**Stato:** Design approvato, pronto per implementation plan
**Dipendenze:** Sprint 6-7-8 (schema v4, ReconciliationWindow, ElementSnapshot, ChangeLog)

---

## Obiettivi

Sprint 9 completa due sottosistemi indipendenti ma integrati:

1. **Riconciliazione Attiva** — completa il flow `DiffEntryViewModel.Accept` aperto in Sprint 8: clic "Accetta" scrive realmente nel DB seguendo un pattern **Supersede** (storia immutabile) con batch transaction SQL.

2. **Report/Export Multi-formato** — nuovo sottosistema per serializzare il computo in 4 formati: **XPWE** (XML PriMus-WEB), **Excel** (`.xlsx` compatibile PriMus), **PDF** (QuestPDF), **CSV** (italiano, separatore `;`).

Entrambi condividono il nuovo concetto di **struttura gerarchica del computo** (SuperCategoria → Categoria → SubCategoria), definita dall'utente per-sessione e indipendente dalla gerarchia del prezzario.

---

## Architettura

### Data model

**Nuova tabella `ComputoChapters` (schema v5):**

```sql
CREATE TABLE ComputoChapters (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId INTEGER NOT NULL REFERENCES Sessions(Id) ON DELETE CASCADE,
    ParentChapterId INTEGER NULL REFERENCES ComputoChapters(Id) ON DELETE CASCADE,
    Code TEXT NOT NULL,
    Name TEXT NOT NULL,
    Level INTEGER NOT NULL,    -- 1=SuperCat, 2=Cat, 3=SubCat
    SortOrder INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    UNIQUE(SessionId, Code)
);

CREATE INDEX IX_ComputoChapters_Session ON ComputoChapters(SessionId);
CREATE INDEX IX_ComputoChapters_Parent ON ComputoChapters(ParentChapterId);
```

**Modifica `QtoAssignments`:**

```sql
ALTER TABLE QtoAssignments ADD COLUMN ComputoChapterId INTEGER NULL
    REFERENCES ComputoChapters(Id) ON DELETE SET NULL;
CREATE INDEX IX_QtoAssignments_Chapter ON QtoAssignments(ComputoChapterId);
```

**Modifica `Sessions`:**

```sql
ALTER TABLE Sessions ADD COLUMN LastUsedComputoChapterId INTEGER NULL
    REFERENCES ComputoChapters(Id) ON DELETE SET NULL;
```

**Bump `CurrentVersion` in `DatabaseSchema.cs` da 4 → 5.**

### Modelli C#

```csharp
// QtoRevitPlugin.Core/Models/ComputoChapter.cs
public class ComputoChapter
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public int? ParentChapterId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int Level { get; set; }       // 1, 2, 3
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
}

// QtoRevitPlugin.Core/Models/SupersedeOp.cs
public enum SupersedeKind { Modified, Deleted }

public class SupersedeOp
{
    public int OldAssignmentId { get; set; }
    public QtoAssignment NewVersion { get; set; } = null!;
    public ElementSnapshot NewSnapshot { get; set; } = null!;
    public ChangeLogEntry Log { get; set; } = null!;
    public SupersedeKind Kind { get; set; }
}
```

**Modifica `QtoAssignment`** (Core): aggiunge `int? ComputoChapterId { get; set; }`.

### Report models

```csharp
// QtoRevitPlugin.Core/Reports/ReportDataSet.cs
public class ReportDataSet
{
    public WorkSession Session { get; set; } = null!;
    public ReportHeader Header { get; set; } = new();
    public List<ReportChapterNode> Chapters { get; set; } = new();
    public List<ReportEntry> UnchaperedEntries { get; set; } = new();  // "(senza capitolo)"
    public decimal GrandTotal { get; set; }
}

public class ReportHeader
{
    public string Titolo { get; set; } = "";
    public string Committente { get; set; } = "";
    public string DirettoreLavori { get; set; } = "";
    public DateTime DataCreazione { get; set; }
    // CompanyLogoPath vive solo in ReportExportOptions (non duplicato qui) — i PDF exporter leggono options.CompanyLogoPath.
}

public class ReportChapterNode
{
    public ComputoChapter Chapter { get; set; } = null!;
    public List<ReportChapterNode> Children { get; set; } = new();
    public List<ReportEntry> Entries { get; set; } = new();
    public decimal Subtotal { get; set; }
}

public class ReportEntry
{
    public int OrderIndex { get; set; }
    public string EpCode { get; set; } = "";
    public string EpDescription { get; set; } = "";
    public string Unit { get; set; } = "";
    public double Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }
    public string ElementId { get; set; } = "";
    public string Category { get; set; } = "";
    // Audit (solo CSV analitico)
    public int Version { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string AuditStatus { get; set; } = "";
}
```

### Repository interface

Nuove firme in `IQtoRepository`:

```csharp
// ComputoChapter CRUD
int InsertComputoChapter(ComputoChapter ch);
void UpdateComputoChapter(ComputoChapter ch);
void DeleteComputoChapter(int chapterId);
IReadOnlyList<ComputoChapter> GetComputoChapters(int sessionId);

// Reconciliation (Supersede pattern)
void AcceptDiffBatch(IReadOnlyList<SupersedeOp> ops);
```

`AcceptDiffBatch` apre **una singola `SqliteTransaction`** e per ogni `op`:

- Se `op.Kind == Modified`:
  - `UPDATE QtoAssignments SET AuditStatus='Superseded' WHERE Id = op.OldAssignmentId`
  - `INSERT INTO QtoAssignments (...)` con `op.NewVersion` (campi: `Version = old.Version + 1`, `AuditStatus='Active'`, stesso `ElementId`/`UniqueId`/`EpCode`/`ComputoChapterId`, nuova `Quantity`, `CreatedBy = userContext.UserId`, `CreatedAt = now`)
  - `INSERT INTO ChangeLog (...)` con `ChangeType='Superseded'`, `OldValueJson={qty,hash}`, `NewValueJson={qty,hash}`
  - `INSERT OR REPLACE INTO ElementSnapshots (...)` con `op.NewSnapshot`
- Se `op.Kind == Deleted`:
  - `UPDATE QtoAssignments SET AuditStatus='Deleted' WHERE Id = op.OldAssignmentId`
  - `INSERT INTO ChangeLog (...)` con `ChangeType='Deleted'`, `OldValueJson={qty}`

Su exception: `tx.Rollback()` e rilancio. Niente scrittura parziale.

---

## Riconciliazione Attiva — UI

### ReconciliationWindow — modifiche

- Ogni riga "Eliminato"/"Modificato" sostituisce il pulsante "Accetta" con una **checkbox "Accetta"** (bind a `DiffEntryViewModel.Accepted`).
- Il pulsante "Accetta TUTTO" seleziona tutte le checkbox senza applicare.
- Il pulsante "Ignora TUTTO" de-seleziona e svuota le collezioni (comportamento invariato).
- Nuovo pulsante **"Applica selezionate (N)"** in fondo, evidenziato blu (`#1E6FD9`), abilitato solo se `N > 0`. Mostra il conteggio selezionati in tempo reale.
- Al clic "Applica": `ReconciliationViewModel.ApplyBatchAsync()` esegue su `Task.Run` il `_repo.AcceptDiffBatch`, mostra progress bar. Su successo: MessageBox "N modifiche applicate", chiude la finestra. Su errore: `CrashLogger.WriteException` + MessageBox di errore.
- Sezione "Nuovi" (azzurra): ogni riga ora ha un bottone **"Assegna EP…"** che chiude la ReconciliationWindow e apre il CatalogBrowserWindow pre-popolato con l'elemento selezionato.

### ReconciliationViewModel — modifiche

```csharp
public partial class ReconciliationViewModel : ObservableObject
{
    [ObservableProperty] private int _acceptedCount;  // computed via partial hook on DiffEntryViewModel.Accepted
    [ObservableProperty] private bool _isApplying;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyBatchAsync()
    {
        IsApplying = true;
        try
        {
            var ops = DeletedItems.Concat(ModifiedItems)
                .Where(d => d.Accepted)
                .Select(BuildSupersedeOp).ToList();
            await Task.Run(() => _repo.AcceptDiffBatch(ops));
            MessageBox.Show($"{ops.Count} modifiche applicate.", "Riconciliazione");
            // trigger refresh via SessionChanged
        }
        catch (Exception ex)
        {
            CrashLogger.WriteException("AcceptDiffBatch", ex);
            MessageBox.Show($"Errore: {ex.Message}", "Riconciliazione");
        }
        finally { IsApplying = false; }
    }

    private bool CanApply => AcceptedCount > 0 && !IsApplying;

    private SupersedeOp BuildSupersedeOp(DiffEntryViewModel vm) { ... }
}
```

---

## Struttura Computo — UI

### Nuova tab nel DockablePane

Affianca le tab esistenti (Mapping, Catalog). Nome tab: **"Struttura Computo"**.

### Layout

```
┌─ Tab "Struttura Computo" ─────────────────────────┐
│ [+ Super] [+ Cat] [+ Sub] [✎ Rinomina] [🗑 Elimina]│
├───────────────────────────────────────────────────┤
│ ▼ 01 DEMOLIZIONI                       (12 voci) │
│   ▼ 01.A Demolizioni strutturali       (8 voci)  │
│     ▸ 01.A.01 Solette in c.a.          (5 voci)  │
│     ▸ 01.A.02 Murature portanti        (3 voci)  │
│   ▸ 01.B Rimozioni impianti            (4 voci)  │
│ ▼ 02 OPERE EDILI                       (45 voci) │
│ ▼ (senza capitolo)                     (3 voci)  │
└───────────────────────────────────────────────────┘
```

### ComputoChapterViewModel

```csharp
public partial class ComputoChapterViewModel : ObservableObject
{
    public ComputoChapter Model { get; }
    public ObservableCollection<ComputoChapterViewModel> Children { get; } = new();
    public int DirectAssignmentsCount { get; set; }
    [ObservableProperty] private bool _isExpanded = true;
    [ObservableProperty] private bool _isSelected;

    public string DisplayLabel => $"{Model.Code}  {Model.Name}  ({TotalCount} voci)";
    public int TotalCount => DirectAssignmentsCount + Children.Sum(c => c.TotalCount);
}
```

### ComputoStructureViewModel

- Carica `GetComputoChapters(sessionId)` + `GetAssignments(sessionId)` e costruisce l'albero.
- Comandi: `AddSuperCommand`, `AddCategoryCommand`, `AddSubCategoryCommand`, `RenameCommand`, `DeleteCommand`.
- Validazione livelli: `AddCategory` abilitato solo se `SelectedItem?.Level == 1`; `AddSubCategory` solo se `Level == 2`.
- Delete con conferma se il capitolo contiene assegnazioni (ON DELETE SET NULL: le voci tornano a "senza capitolo").
- Drag&drop: tramite `GongSolutions.WPF.DragDrop` (NuGet già nel progetto?) oppure implementazione manuale con `DragDrop.DoDragDrop`; questo è opzionale per Sprint 9 — MVP è bottoni + "Sposta su/giù".

### Popup di edit

`ChapterEditorPopup.xaml` — inline popup con TextBox per `Code` e `Name`, validazione: `Code` unico per-sessione, non vuoto.

### CatalogBrowserWindow — dropdown Capitolo

Nuova riga sopra il pulsante ASSEGNA:

```xml
<Grid>
  <Grid.ColumnDefinitions>
    <ColumnDefinition Width="Auto"/>
    <ColumnDefinition Width="*"/>
    <ColumnDefinition Width="Auto"/>
  </Grid.ColumnDefinitions>
  <TextBlock Grid.Column="0" Text="Capitolo:" Margin="0,0,6,0"/>
  <ComboBox Grid.Column="1"
            ItemsSource="{Binding AvailableChapters}"
            SelectedItem="{Binding SelectedChapterOption}"
            DisplayMemberPath="DisplayPath"/>
  <Button Grid.Column="2" Content="+" Width="28"
          Command="{Binding CreateChapterInlineCommand}"/>
</Grid>
```

`AvailableChapters`: lista piatta di wrapper `ChapterOption { ComputoChapter? Chapter, string DisplayPath }` dove `DisplayPath` è il path completo separato da `/` (es. `"01 DEMOLIZIONI / 01.A Strutturali / 01.A.01 Solette"`). Include un'opzione con `Chapter=null` e `DisplayPath="(senza capitolo)"` in cima. `DisplayPath` viene calcolato dal ViewModel risalendo la catena `ParentChapterId`.

Durante `AssignAsync`: `assignment.ComputoChapterId = SelectedChapterOption?.Chapter?.Id` e aggiorna `Session.LastUsedComputoChapterId` se != null.

---

## Report/Export Subsystem

### Interfaccia comune

```csharp
// QtoRevitPlugin.Core/Reports/IReportExporter.cs
public interface IReportExporter
{
    string FormatName { get; }
    string FileExtension { get; }
    string FileFilter { get; }
    ReportExportOptions DefaultOptions { get; }
    void Export(ReportDataSet data, string outputPath, ReportExportOptions options);
}

public class ReportExportOptions
{
    public bool IncludeAuditFields { get; set; }
    public bool IncludeDeletedAndSuperseded { get; set; } = false;
    public bool GroupByChapter { get; set; } = true;
    public string? CompanyLogoPath { get; set; }
    public string Titolo { get; set; } = "";
    public string Committente { get; set; } = "";
    public string DirettoreLavori { get; set; } = "";
}
```

### ReportDataSetBuilder

```csharp
// QtoRevitPlugin.Core/Reports/ReportDataSetBuilder.cs
public class ReportDataSetBuilder
{
    private readonly IQtoRepository _repo;
    public ReportDataSetBuilder(IQtoRepository repo) => _repo = repo;

    public ReportDataSet Build(int sessionId, ReportExportOptions options)
    {
        var session = _repo.GetSession(sessionId)!;
        var chapters = _repo.GetComputoChapters(sessionId);
        var assignments = _repo.GetAssignments(sessionId)
            .Where(a => options.IncludeDeletedAndSuperseded || a.AuditStatus == AssignmentStatus.Active)
            .ToList();

        var dataset = new ReportDataSet { Session = session, Header = BuildHeader(options) };

        // Costruisce albero da lista piatta
        var roots = chapters.Where(c => c.Level == 1).OrderBy(c => c.SortOrder).ToList();
        int orderCounter = 1;
        foreach (var root in roots)
            dataset.Chapters.Add(BuildNode(root, chapters, assignments, ref orderCounter));

        dataset.UnchaperedEntries = assignments
            .Where(a => a.ComputoChapterId == null)
            .Select(a => BuildEntry(a, ref orderCounter))
            .ToList();

        dataset.GrandTotal = dataset.Chapters.Sum(c => c.Subtotal)
                           + dataset.UnchaperedEntries.Sum(e => e.Total);
        return dataset;
    }
}
```

### XpweExporter

Usa `System.Xml.XmlWriter` con impostazioni: `Encoding.UTF8`, `Indent=true`, `IndentChars="  "`.

Struttura gerarchica:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<PriMus xmlns="http://www.acca.it/primus/xpwe/v1" versione="1.0">
  <Intestazione>
    <Titolo>...</Titolo>
    <Committente>...</Committente>
    <DirettoreLavori>...</DirettoreLavori>
    <DataCreazione>2026-04-22T18:00:00</DataCreazione>
  </Intestazione>
  <Computo>
    <SuperCategoria codice="01" descrizione="DEMOLIZIONI">
      <Categoria codice="01.A" descrizione="Demolizioni strutturali">
        <SubCategoria codice="01.A.01" descrizione="Solette in c.a.">
          <Voce numero="1">
            <CodiceEP>TOS25_01.A03.001.001</CodiceEP>
            <Descrizione>...</Descrizione>
            <UM>m³</UM>
            <Quantita>12.50</Quantita>
            <PrezzoUnitario>13.63017</PrezzoUnitario>
            <Importo>170.38</Importo>
          </Voce>
        </SubCategoria>
      </Categoria>
    </SuperCategoria>
  </Computo>
  <Totali>
    <Totale>170.38</Totale>
  </Totali>
</PriMus>
```

**Nota schema:** in assenza di XSD ufficiale XPWE accessibile, si genera XML gerarchico best-effort. L'utente testa l'import in PriMus reale durante la fase di QA; eventuali correzioni (namespace, attributi `cam`/`tipo`) vengono applicate al codice dell'exporter senza cambiare il data model.

### ExcelExporter (ClosedXML)

File `.xlsx` con 2 sheet:

**Sheet "Computo":**

| Colonna | Header | Format |
|---|---|---|
| A | N° | numero intero |
| B | Capitolo | testo |
| C | Codice | testo |
| D | Descrizione | testo wrap |
| E | UM | testo |
| F | Quantità | `#,##0.00` |
| G | Prezzo | `#,##0.00 €` |
| H | Importo | `#,##0.00 €` |

Righe raggruppate per capitolo con sub-totali evidenziati (bold, fill `#F0F0F0`). Totale generale in fondo (bold, fill `#1E6FD9` fg white). Header row 1 bold fill `#1E6FD9` fg white, freeze panes row 2, colonne auto-fit.

**Sheet "Metadati":** tabella key-value con `Titolo`, `Committente`, `DirettoreLavori`, `DataCreazione`, `SessionName`, `ProjectName`, `TotalElements`, `GrandTotal`.

### PdfExporter (QuestPDF)

NuGet package: `QuestPDF 2024.*` in `QtoRevitPlugin.csproj` (non Core — dipendenze WPF-heavy).

Layout:

- **Page size:** A4 Portrait
- **Margin:** 20mm
- **Header** (ogni pagina): logo (se presente) + titolo computo + data a destra. Linea separatrice.
- **Footer** (ogni pagina): "Pag. X di Y" centrato + nome file CME a destra.
- **Body:** tabella con colonne `[N°, Codice, Descrizione, UM, Quantità, Prezzo, Importo]`. Raggruppata con `.Section()` per SuperCat. Cat/Sub come sub-header in-table (bold, fill grigio chiaro).
- **Ultima pagina:** box riepilogo "Totale per SuperCategoria" + "Totale generale" (bold, 14pt, fill blu).

Paginazione automatica di QuestPDF. Font: **Arial** 10pt body, 9pt header tabella, 14pt titolo.

### CsvExporter

UTF-8 **con BOM** (per compatibilità Excel italiano), separatore `;`, decimal `,` (formato italiano).

**Modalità base:**
```
Capitolo;Codice;Descrizione;UM;Quantità;PrezzoUnit;Importo;ElementId;Categoria
"01.A.01 Solette in c.a.";"TOS25_01.A03.001.001";"Demolizione con mezzo meccanico...";"m³";"12,50";"13,63";"170,38";"1001-abc";"Floors"
```

**Modalità analitica** (`IncludeAuditFields=true`): aggiunge `Version;CreatedBy;CreatedAt;AuditStatus`.

Quoting: cella con `;` `"` o `\n` → racchiusa in `"`, doppi apici raddoppiati. Formattazione numerica via `CultureInfo.GetCultureInfo("it-IT")`.

### ExportWizardWindow

Nuovo dialog WPF con 3 pagine navigabili (Avanti/Indietro):

**Pagina 1 — Formato:**
4 radio button (XPWE, Excel, PDF, CSV) con icona + descrizione breve.

**Pagina 2 — Opzioni:**
- TextBox `Titolo`, `Committente`, `DirettoreLavori` (pre-popolati dall'ultima esportazione, salvati in `CmeSettings`)
- Checkbox `IncludeAuditFields` (solo se formato=CSV)
- Checkbox `IncludeDeletedAndSuperseded` (warning: esportazione storica completa, default false)
- File picker `CompanyLogoPath` (solo se formato=PDF)

**Pagina 3 — Destinazione:**
- `SaveFileDialog` con filtro pre-impostato dal formato scelto, nome file proposto = `{SessionName}_{yyyy-MM-dd}.{ext}`
- Bottone "Esporta" → `exporter.Export(dataset, path, options)` su `Task.Run`, progress bar, messaggio successo/errore.

### Ribbon button

Nuovo pulsante nel ribbon panel `QtoConstants.RibbonPanelName`:

```csharp
var exportButton = new PushButtonData(
    "ExportCme", "Export",
    assemblyPath, "QtoRevitPlugin.Commands.ExportCommand")
{
    ToolTip = "Esporta il computo in XPWE/Excel/PDF/CSV",
    LargeImage = IconFactory.CreateExportIcon(32),
    Image = IconFactory.CreateExportIcon(16)
};
panel.AddSeparator();
panel.AddItem(exportButton);
```

`ExportCommand` (IExternalCommand) apre `ExportWizardWindow` passando il `QtoRepository` attivo dal `SessionManager`.

---

## Threading

- **Reconciliation batch**: `await Task.Run(() => _repo.AcceptDiffBatch(...))` — SQL su thread di background, no Revit API, no UI. `Dispatcher.Invoke` per messaggi.
- **Export**: `await Task.Run(() => exporter.Export(dataset, path, opts))` — thread di background; no Revit API chiamata durante export (i dati sono già letti dal DB).
- **ReportDataSetBuilder**: sincrono, chiamato dal thread di background dell'Export.
- **ComputoStructureViewModel** CRUD: sincrono UI-thread (SQLite non thread-safe, coerente col modello esistente).

**Nessuna modifica al threading model esistente** — no nuovi `RevitTask.RunAsync`, no nuovi Dispatcher, no cambiamenti al SessionManager threading.

---

## Cross-target compatibility

- `System.Text.Json`: già in Core via NuGet `System.Text.Json 6.0.11`.
- `System.Xml`: built-in.
- `ClosedXML`: già nel solution (usato da altri writer).
- `QuestPDF`: nuovo NuGet in `QtoRevitPlugin.csproj` (main plugin). Versione `2024.*`. Supporta sia net48 che net8.
- **Verifica LangVersion**: `ComputoChapter` usa `= new()` collection initializer. Se LangVersion Core < 9, sostituire con `= new List<...>()`.

---

## Testing

Tests in `QtoRevitPlugin.Tests/Sprint9/` (net8.0, xUnit + FluentAssertions):

### ComputoChapterRepositoryTests.cs (4 test)

1. `Insert_AssignsId_AndRoundtrips`
2. `Update_PersistsChanges`
3. `Delete_WithAssignments_SetsNullViaOnDeleteSetNull`
4. `GetForSession_ReturnsOrderedBySortOrder`

### SchemaV5MigrationTests.cs (3 test)

1. `NewDb_HasComputoChaptersTable`
2. `NewDb_QtoAssignments_HasComputoChapterIdColumn`
3. `ExistingV4Db_MigratesTo_V5_PreservingData`

### SupersedeFlowTests.cs (4 test)

1. `AcceptDiffBatch_ModifiedOp_CreatesVersionPlus1_AndMarksOldSuperseded`
2. `AcceptDiffBatch_DeletedOp_MarksAssignmentDeleted`
3. `AcceptDiffBatch_AllOpsInSingleTransaction_RollsBackOnException`
4. `AcceptDiffBatch_WritesChangeLog_WithOldAndNewJson`

### ReportDataSetBuilderTests.cs (3 test)

1. `Build_FiltersActiveOnly_ByDefault`
2. `Build_GroupsByComputoChapter_ThreeLevels`
3. `Build_CalculatesSubtotalsAndGrandTotalCorrectly`

### XpweExporterTests.cs (3 test)

1. `Export_ProducesValidXmlWithRootElement`
2. `Export_HierarchyIsSuperCategoriaCategoriaSubCategoriaVoce`
3. `Export_UsesUtf8EncodingDeclaration`

### CsvExporterTests.cs (3 test)

1. `Export_BasicMode_HasNineColumns`
2. `Export_AnalyticMode_IncludesAuditColumns`
3. `Export_QuotesCellsContainingSemicolons`

### ExcelExporterTests.cs (2 test)

1. `Export_FileHasTwoSheets_ComputoAndMetadati`
2. `Export_ComputoSheet_HasEightColumnsInOrder`

### PdfExporterTests.cs (1 test)

1. `Export_ProducesNonEmptyPdfFile`

**Target: 23 nuovi test → 148 totali passing.**

---

## File structure

```
QtoRevitPlugin.Core/
├── Models/
│   ├── ComputoChapter.cs               [NEW]
│   ├── SupersedeOp.cs                  [NEW]
│   └── QtoAssignment.cs                [MODIFY: + ComputoChapterId]
├── Reports/                            [NEW folder]
│   ├── ReportDataSet.cs                [NEW]
│   ├── ReportEntry.cs                  [NEW]
│   ├── ReportChapterNode.cs            [NEW]
│   ├── ReportHeader.cs                 [NEW]
│   ├── ReportExportOptions.cs          [NEW]
│   ├── IReportExporter.cs              [NEW]
│   ├── ReportDataSetBuilder.cs         [NEW]
│   ├── XpweExporter.cs                 [NEW]
│   └── CsvExporter.cs                  [NEW]
├── Data/
│   ├── DatabaseSchema.cs               [MODIFY: v5]
│   ├── DatabaseInitializer.cs          [MODIFY: v4→v5 migration]
│   ├── IQtoRepository.cs               [MODIFY: ComputoChapter CRUD + AcceptDiffBatch]
│   └── QtoRepository.cs                [MODIFY: implement]

QtoRevitPlugin/
├── Reports/                            [NEW folder - WPF/QuestPDF deps]
│   ├── ExcelExporter.cs                [NEW]
│   └── PdfExporter.cs                  [NEW]
├── Commands/
│   └── ExportCommand.cs                [NEW]
├── Application/
│   └── QtoApplication.cs               [MODIFY: ribbon button Export]
├── UI/
│   ├── ViewModels/
│   │   ├── ComputoChapterViewModel.cs  [NEW]
│   │   ├── ComputoStructureViewModel.cs [NEW]
│   │   ├── CatalogBrowserViewModel.cs  [MODIFY: chapter dropdown]
│   │   ├── ReconciliationViewModel.cs  [MODIFY: batch apply]
│   │   └── ExportWizardViewModel.cs    [NEW]
│   ├── Views/
│   │   ├── ComputoStructureView.xaml(.cs)         [NEW]
│   │   ├── ChapterEditorPopup.xaml(.cs)           [NEW]
│   │   ├── CatalogBrowserWindow.xaml              [MODIFY]
│   │   ├── ReconciliationWindow.xaml              [MODIFY]
│   │   └── ExportWizardWindow.xaml(.cs)           [NEW]
│   └── Panes/
│       └── QtoDockablePane.xaml                   [MODIFY: + tab ComputoStructure]

QtoRevitPlugin.Tests/
└── Sprint9/
    ├── ComputoChapterRepositoryTests.cs       [NEW]
    ├── SchemaV5MigrationTests.cs              [NEW]
    ├── SupersedeFlowTests.cs                  [NEW]
    ├── ReportDataSetBuilderTests.cs           [NEW]
    ├── XpweExporterTests.cs                   [NEW]
    ├── CsvExporterTests.cs                    [NEW]
    ├── ExcelExporterTests.cs                  [NEW]
    └── PdfExporterTests.cs                    [NEW]
```

---

## Error handling

- **AcceptDiffBatch failure**: rollback transazione SQL, re-throw al ViewModel che cattura e logga via `CrashLogger.WriteException("AcceptDiffBatch", ex)`, MessageBox all'utente con `ex.Message`. Nessun commit parziale.
- **Export file write error**: cattura `IOException`, `UnauthorizedAccessException`. MessageBox "Impossibile scrivere il file: {msg}". File parziale cancellato se esiste.
- **XPWE write error**: se XmlWriter fallisce a metà, file parziale cancellato (`File.Delete(path)`).
- **PDF QuestPDF error**: catch generico, log, MessageBox. QuestPDF ha validazione robusta — errori rari tranne su path invalido.
- **Missing IQtoRepository method on old QtoRepository**: Sprint 9 aggiunge i metodi nell'interfaccia E nell'implementazione nello stesso commit; nessun breaking change intermedio.

---

## Out of scope (non fa parte di Sprint 9)

- **Drag&drop** nell'albero `ComputoStructureView` — MVP usa bottoni "Sposta su/giù". D&D è Sprint 10+.
- **Preview** PDF prima dell'export.
- **Template personalizzabili** (Regione Lombardia, DEI, bando generico) — usa un solo template standard per formato.
- **Export multi-sessione** (merge più CME in un report). Singola sessione attiva.
- **Firma digitale PDF** — rimandato a Sprint 10+.
- **Import XPWE** (import di un computo PriMus nel plugin). Solo export.
- **Reverse engineering `.dcf` binario** — non supportato.
