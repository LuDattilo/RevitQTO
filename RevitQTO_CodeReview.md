# RevitQTO — Code Review Tecnica e Funzionale

**Repository:** [LuDattilo/RevitQTO](https://github.com/LuDattilo/RevitQTO)  
**Revisione su commit:** `c08509c`  
**Data review:** 23 aprile 2026  
**Scope:** Plugin Revit C# — layer ViewModels, Services, Extraction

---

## Executive Summary

Il codebase mostra un'architettura MVVM ben strutturata, con separazione netta tra layer di presentazione, servizi e accesso dati. La qualità generale è buona per un plugin Revit in fase di sviluppo Sprint 4–5. Sono state identificate **14 osservazioni** classificate per severità: 3 critiche/alta priorità, 6 medie, 5 basse/miglioramenti. Le criticità principali riguardano la gestione del threading nel `SessionManager`, l'assenza di unit test su logica core, e un pattern di ID locale fragile in `MappingViewModel`.

---

## Architettura Generale

### Struttura del progetto

Il plugin è organizzato nei seguenti moduli principali:

| Cartella | Responsabilità | Qualità |
|---|---|---|
| `Application/` | Bootstrap, singleton `QtoApplication` | ✅ Buona |
| `Commands/` | IExternalCommand Revit | n/a (non esaminati) |
| `ExtensibleStorage/` | ES Revit per persistenza nel .rvt | ✅ Presente |
| `Extraction/` | Estrazione quantità da elementi Revit | ✅ Buona |
| `Services/` | Business logic (Session, Recovery, Diff) | ⚠️ Vedi note |
| `UI/ViewModels/` | MVVM con CommunityToolkit | ✅ Buona |

La decisione architetturale di separare la **UserLibrary globale** (listini, persistenti) dal file `.cme` per computo è corretta e ben documentata nel codice. Il pattern file-based con SQLite come backing store è adeguato per un plugin Revit desktop.

---

## Osservazioni Critiche (Alta Priorità)

### [CRIT-1] Threading race condition in `SessionManager.LaunchModelDiff`

**File:** `QtoRevitPlugin/Services/SessionManager.cs` — metodo `LaunchModelDiff`

```csharp
// PROBLEMA: _repository è catturato per reference al momento dell'avvio
// ma può essere nullato da CloseCurrent() prima che il lambda esegua
_ = Revit.Async.RevitTask.RunAsync(app =>
{
    var capturedRepo = _repository; // troppo tardi: cattura avviene all'esecuzione
    ...
    System.Windows.Application.Current.Dispatcher.BeginInvoke(new System.Action(() =>
    {
        if (capturedRepo != _repository) return; // guardia parziale
        var vm = new ReconciliationViewModel(result, capturedRepo, userContext);
```

**Problema:** Il commento nel codice stesso segnala il problema ("Capture repository reference before async work"), ma la cattura di `capturedRepo` avviene dentro il lambda, ovvero *dopo* lo scheduling async — non prima. Il campo `_repository` può essere nullato da `CloseCurrent()` tra la chiamata a `LaunchModelDiff` e l'esecuzione effettiva del lambda. La guardia `if (capturedRepo != _repository)` è corretta come idea ma non sufficiente: se `_repository` è null e `capturedRepo` era non-null, la condizione è `null != non-null = true` → si procede col `capturedRepo` obsoleto.

**Fix consigliato:**
```csharp
// Cattura PRIMA di passare il lambda a RevitTask
var capturedRepo = _repository;
if (capturedRepo == null) return;

_ = Revit.Async.RevitTask.RunAsync(app =>
{
    // capturedRepo qui è catturato correttamente nel closure
    ...
```

---

### [CRIT-2] `CanSyncSilently` confronta contatori sempre = 0

**File:** `QtoRevitPlugin/Services/RecoveryService.cs`

```csharp
public RecoveryAnalysis Analyze(Document doc, QtoRepository repo)
{
    var analysis = new RecoveryAnalysis
    {
        ModelAssignmentCount = 0,     // Sprint 3+: full scan ES opzionale
        DbAssignmentCount = 0         // Sprint 3+: COUNT(*) su QtoAssignments
    };
    ...
}

public bool CanSyncSilently(RecoveryAnalysis analysis)
{
    var delta = Math.Abs(analysis.ModelAssignmentCount - analysis.DbAssignmentCount);
    return delta <= SilentSyncThreshold; // delta è SEMPRE 0 → sempre silente
}
```

**Problema:** I campi `ModelAssignmentCount` e `DbAssignmentCount` sono sempre inizializzati a 0 e non vengono mai popolati (rimandato a Sprint 3+). Di conseguenza `CanSyncSilently` ritorna sempre `true`, rendendo il dialog utente irraggiungibile anche in caso di gravi divergenze. Questo può mascherare inconsistenze reali senza avvisare l'utente.

**Fix minimo:** Aggiungere almeno `DbAssignmentCount = repo.CountAssignmentsForProject(path)` usando una query SQL `COUNT(*)` economica.

---

### [CRIT-3] `ModelDiffService` — "Added" non è mai popolato

**File:** `QtoRevitPlugin/Services/ModelDiffService.cs`

```csharp
public class ModelDiffResult
{
    public List<DiffEntry> Deleted { get; } = new List<DiffEntry>();
    public List<DiffEntry> Modified { get; } = new List<DiffEntry>();
    public List<Element> Added { get; } = new List<Element>(); // mai popolato
}
```

Il metodo `ComputeDiff` scansiona solo gli snapshot esistenti (elementi già noti). Gli elementi **aggiunti** al modello dopo l'ultimo salvataggio non vengono mai rilevati. La `ReconciliationView` mostrerà quindi solo Deleted/Modified, mai Added — comportamento silenziosamente incompleto, che può indurre l'utente a credere di aver rilevato tutte le modifiche.

**Fix:** Aggiungere un secondo passaggio che raccoglie tutti gli ElementId della categoria nei snapshot e confronta con `FilteredElementCollector` per trovare gli Id non ancora in snapshot.

---

## Osservazioni Medie

### [MED-1] ID locale con `Max() + 1` in `MappingViewModel` — fragile

**File:** `QtoRevitPlugin/UI/ViewModels/MappingViewModel.cs`

```csharp
private static int NextLocalId(IEnumerable<int> existing)
{
    var max = existing.DefaultIfEmpty(0).Max();
    return max + 1;
}
```

Questo pattern è corretto per uso in-memory ma diventa problematico in Sprint 5 quando gli ID saranno assegnati dal DB (autoincrement SQLite). Dopo la persistenza, un `Id` locale 3 potrebbe collidere con il `rowid` 3 già esistente nel DB se lo stesso VM viene usato per l'update. Si raccomanda di usare un flag `IsNew` booleano invece dell'Id = 0 convention, e di azzerare l'Id dopo l'insert DB.

### [MED-2] `QuantityExtractor` — suggerimento sbagliato per `OST_Rooms`

**File:** `QtoRevitPlugin/Extraction/QuantityExtractor.cs`

```csharp
case BuiltInCategory.OST_Rooms:
    return "Volume";
```

Per i locali (Rooms) il parametro primario in una metrica QTO è tipicamente l'**Area** (m²), non il Volume. Il Volume nei Room Revit è spesso non valorizzato o inaffidabile. Questo porta a quantità errate nel computo se l'utente si fida del suggerimento pre-selezionato.

**Fix:** Cambiare il default a `"Area"` per `OST_Rooms` e `OST_MEPSpaces`.

### [MED-3] Doppio abbonamento a `PropertyChanged` in `MappingViewModel`

**File:** `QtoRevitPlugin/UI/ViewModels/MappingViewModel.cs`

```csharp
partial void OnEditingManualItemChanged(ManualItemVm? value)
{
    if (value != null)
        value.PropertyChanged += (_, _) => RecalcManualTotal();
}
```

Ogni volta che l'utente apre l'editor (Add/Edit), si sottoscrive un nuovo handler senza mai de-registrare quello precedente. Se lo stesso `ManualItemVm` viene riusato (e.g., dopo Cancel + re-Edit), l'handler viene aggiunto più volte. Ciò provoca calcoli duplicati e potenziali memory leak.

**Fix:**
```csharp
partial void OnEditingManualItemChanged(ManualItemVm? oldValue, ManualItemVm? newValue)
{
    if (oldValue != null) oldValue.PropertyChanged -= OnEditingItemPropertyChanged;
    if (newValue != null) newValue.PropertyChanged += OnEditingItemPropertyChanged;
}
private void OnEditingItemPropertyChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    => RecalcManualTotal();
```

### [MED-4] `ExtractHashParams` — fallback silenzioso sull'area

**File:** `QtoRevitPlugin/Services/ModelDiffService.cs`

```csharp
var p = elem.LookupParameter(paramName)
    ?? elem.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
```

Se il parametro specificato nella `MappingRule` non esiste sull'elemento, si cade silenziosamente su `HOST_AREA_COMPUTED`. Questo può produrre hash identici per elementi con area uguale ma parametri diversi (es. due porte della stessa larghezza ma altezze diverse), generando **false negative** nel diff (modifiche non rilevate).

**Fix:** Loggare il fallback e aggiungere il parametro mancante all'hash con value = 0 (mantenendo coerenza con l'hash storico).

### [MED-5] `CatalogBrowserViewModel.AssignAsync` — loop senza Transaction

**File:** `QtoRevitPlugin/UI/ViewModels/CatalogBrowserViewModel.cs`

```csharp
foreach (var elemId in selectedIds)
{
    _qtoRepository.InsertAssignment(assignment);
    _qtoRepository.UpsertSnapshot(...);
    _qtoRepository.AppendChangeLog(...);
    assigned++;
}
```

Il loop esegue 3 operazioni SQLite per ogni elemento selezionato **senza una transaction esplicita**. Su 100 elementi, sono 300 operazioni SQLite atomiche separate. SQLite in autocommit è ~100x più lento di un batch transazionale. Su selezioni grandi, l'UI si congelerà per secondi.

**Fix:** Avvolgere il loop in `using var tx = _qtoRepository.BeginTransaction()` e fare `tx.Commit()` alla fine.

### [MED-6] `SetupViewModel.ImportFromFile` — parser discovery statica

```csharp
var parsers = new IPriceListParser[] { new DcfParser(), new ExcelParser(), new CsvParser() };
```

L'array di parser è hardcoded. Aggiungere un nuovo formato (es. XPWE, SIX) richiede modifica del ViewModel. Preferibile un pattern di registrazione (lista in `QtoApplication` o semplice factory).

---

## Osservazioni Basse / Miglioramenti

### [LOW-1] `ViewModelBase` vuoto — opportunità persa

**File:** `QtoRevitPlugin/UI/ViewModels/ViewModelBase.cs`

La classe base è pressoché vuota (159 byte). Sarebbe il posto naturale per esporre helper come `RunOnUiThread`, `SetBusy`, logging centralizzato e gestione errori condivisa tra tutti i VM.

### [LOW-2] `CatalogNode.ApplyFilter` — O(n) per ogni keystroke

Il filtro è applicato a ogni modifica del testo con una scansione completa dell'albero. Per cataloghi con 23k voci distribuiti su 3 livelli, questo può essere lento. Si suggerisce un debounce (già presente in `SetupViewModel` ma mancante in `CatalogBrowserViewModel`), oppure una struttura indice separata.

### [LOW-3] `PriceListRow` e `PriceItemRow` — DTO immutabili senza interfaccia

Le row DTO sono corrette ma non implementano alcuna interfaccia. Nell'ottica Sprint 5 (export Excel, stampa), avere `IPriceItemProjection` facilita la serializzazione generica.

### [LOW-4] Commenti TODO Sprint 5 — nessun tracking

I commenti `// TODO Sprint 5:` sono numerosi e ben scritti, ma non sono tracciati come issue GitHub. Si raccomanda di aprire issue dedicati per ogni TODO per evitare che siano dimenticati al cambio di sprint.

### [LOW-5] `RecoveryService.Analyze` — TimeSpan comparison senza UTC normalization

```csharp
var diff = (analysis.ModelLastSync!.Value - analysis.DbLastSync!.Value).TotalSeconds;
```

Se `ModelLastSync` è `DateTimeKind.Utc` e `DbLastSync` è `DateTimeKind.Local` (o Unspecified, comune con SQLite), il confronto produce risultati errati in certi fusi orari. Normalizzare entrambi a UTC prima della sottrazione.

---

## Punti di Forza

L'architettura presenta diversi pattern ben applicati che vale la pena evidenziare:

- **Gestione multi-versione Revit API** con `#if REVIT2025_OR_LATER`: il codice gestisce correttamente le breaking change di Revit 2025 (`ElementId.Value` vs `.IntegerValue`, `UnitTypeId` vs `DisplayUnitType`), garantendo compatibilità backward.
- **FormulaEngine con validazione sintattica** prima del salvataggio: il `SaveRoomMapping` valida la formula NCalc prima di accettarla — ottima UX e protezione contro formule corrotte nel DB.
- **Pattern Clone-for-Edit** in `RoomMappingConfigVm` e `ManualItemVm`: il deep clone prima dell'editing garantisce che il Cancel non modifichi la riga originale. Pattern corretto e coerente.
- **Snapshot SHA256 per Model Diff**: l'uso di un hash deterministico (UniqueId + parametri geometrici) per rilevare modifiche al modello è una soluzione elegante che evita la scansione completa del documento ad ogni apertura.
- **Debounce della ricerca** in `SetupViewModel` (300ms via `DispatcherTimer`): evita query FTS5 ad ogni keystroke — best practice per search-as-you-type.
- **Separazione UserLibrary / .cme**: la decisione di tenere i listini in una libreria globale separata dal file computo è architetturalmente solida e semplifica la condivisione dei listini tra progetti.
- **`RecoveryService` con filosofia ES-centric**: la logica di riconciliazione tra SQLite e Extensible Storage è documentata e ragionata, con casi d'uso ben mappati.

---

## Riepilogo Priorità Interventi

| ID | Severità | File | Effort | Impatto |
|---|---|---|---|---|
| CRIT-1 | 🔴 Alta | `SessionManager.cs` | Basso | Race condition UI crash |
| CRIT-2 | 🔴 Alta | `RecoveryService.cs` | Medio | Dialog recovery mai mostrato |
| CRIT-3 | 🔴 Alta | `ModelDiffService.cs` | Medio | Diff incompleto (Added sempre vuoto) |
| MED-1 | 🟡 Media | `MappingViewModel.cs` | Basso | Sprint 5 ID collision |
| MED-2 | 🟡 Media | `QuantityExtractor.cs` | Minimo | Quantità Rooms errate (Volume→Area) |
| MED-3 | 🟡 Media | `MappingViewModel.cs` | Basso | Memory leak PropertyChanged |
| MED-4 | 🟡 Media | `ModelDiffService.cs` | Basso | False negative nel diff |
| MED-5 | 🟡 Media | `CatalogBrowserViewModel.cs` | Basso | Performance bulk assign |
| MED-6 | 🟡 Media | `SetupViewModel.cs` | Basso | Estensibilità parser |
| LOW-1 | 🟢 Bassa | `ViewModelBase.cs` | Medio | Qualità codice |
| LOW-2 | 🟢 Bassa | `CatalogBrowserViewModel.cs` | Medio | Performance filtro |
| LOW-3 | 🟢 Bassa | `SetupViewModel.cs` | Basso | Estensibilità export |
| LOW-4 | 🟢 Bassa | Vari | Minimo | Tracciamento tecnico |
| LOW-5 | 🟢 Bassa | `RecoveryService.cs` | Minimo | Bug datetime timezone |

---

## Azioni Raccomandate per Sprint 5

1. **Fix immediato CRIT-1**: spostare la cattura di `capturedRepo` prima del lambda in `LaunchModelDiff`.
2. **Fix MED-2**: cambiare `OST_Rooms` da `"Volume"` ad `"Area"` in `QuantityExtractor.SuggestedParam`.
3. **Fix MED-3**: usare `partial void OnEditingManualItemChanged(ManualItemVm? oldValue, ManualItemVm? newValue)` per de-registrare il vecchio handler.
4. **Fix MED-5**: avvolgere il loop di assign in una transaction SQLite esplicita.
5. **Completare CRIT-2**: aggiungere almeno `DbAssignmentCount = repo.CountAssignmentsForProject(path)` prima di passare all'analisi.
6. **Tracciare CRIT-3** come issue GitHub Sprint 5: implementare il rilevamento degli elementi "Added" nel Model Diff.

