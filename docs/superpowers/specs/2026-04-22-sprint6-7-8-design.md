# Design: Sprint 6–7–8 — Prezzario, Preferiti, MappingRules, ModelDiff, Repository Layer

**Data:** 2026-04-22  
**Autore:** Luigi Dattilo  
**Stato:** Approvato

---

## Contesto

Tre nuove funzionalità principali emerse dalla sessione del 22/04/2026:

1. **Pannello Prezzario separato** con Preferiti e toggle ribbon
2. **MappingRules JSON** — parametro quantitativo per categoria persistente
3. **Model Diff Check** — riconciliazione CME esistente con modello modificato

Più la roadmap multiutente (Fase 1): layer di astrazione repository + audit trail + ChangeLog locale.

---

## Strategia: 3 sprint sequenziali

```
Sprint 6 → Fondamenta dati (IQtoRepository, audit fields, ChangeLog, IUserContext)
Sprint 7 → UI Prezzario + Preferiti + MappingRules + flusso assegnazione bidirezionale
Sprint 8 → Model Diff Check (snapshot hash, dialog riconciliazione, applicazione modifiche)
```

Ogni sprint produce un deliverable testabile in Revit prima di procedere al successivo.

---

## Sprint 6 — Fondamenta dati

### Obiettivo

Rendere il codice "cloud-ready" senza cambiare il flusso utente. Tutte le modifiche sono interne al layer dati.

### 1. Interfacce repository

```csharp
interface IQtoRepository
{
    // Sessioni
    int InsertSession(WorkSession session);
    void UpdateSession(WorkSession session);
    WorkSession? GetSession(int sessionId);

    // Assegnazioni
    int InsertAssignment(QtoAssignment assignment);
    void UpdateAssignment(QtoAssignment assignment);
    IReadOnlyList<QtoAssignment> GetAssignments(int sessionId);

    // ChangeLog
    void AppendChangeLog(ChangeLogEntry entry);
    IReadOnlyList<ChangeLogEntry> GetChangeLog(int sessionId);
}

interface IPriceListRepository
{
    IReadOnlyList<PriceList> GetAllLists();
    IReadOnlyList<PriceItem> GetItems(int listId);
    PriceItem? GetItem(string code);
}

interface IFavoritesRepository
{
    FavoriteSet LoadGlobal();
    void SaveGlobal(FavoriteSet set);
    FavoriteSet? LoadForProject(string cmePath);
    void SaveForProject(string cmePath, FavoriteSet set);
}
```

Implementazioni concrete:
- `SqliteQtoRepository` — wrappa `QtoRepository` esistente
- `SqlitePriceListRepository` — wrappa `UserLibraryManager` esistente
- `FileFavoritesRepository` — JSON su filesystem (nuovo)

La UI/WPF e i ViewModel useranno **solo le interfacce**, mai le classi concrete.

### 2. Campi audit su `QtoAssignment`

Aggiungere a `QtoAssignment` (modello + tabella SQLite):

```csharp
string  CreatedBy       // Environment.UserName
DateTime CreatedAt
string? ModifiedBy      // null se mai modificato
DateTime? ModifiedAt
int     Version         // default 1, +1 ad ogni Update
AssignmentStatus Status // enum: Active | Deleted | Superseded
```

Migration SQLite inline (no tool esterno):

```sql
ALTER TABLE QtoAssignments ADD COLUMN CreatedBy TEXT NOT NULL DEFAULT '';
ALTER TABLE QtoAssignments ADD COLUMN CreatedAt TEXT NOT NULL DEFAULT '';
ALTER TABLE QtoAssignments ADD COLUMN ModifiedBy TEXT;
ALTER TABLE QtoAssignments ADD COLUMN ModifiedAt TEXT;
ALTER TABLE QtoAssignments ADD COLUMN Version INTEGER NOT NULL DEFAULT 1;
ALTER TABLE QtoAssignments ADD COLUMN Status TEXT NOT NULL DEFAULT 'Active';
```

### 3. ChangeLog

Nuova tabella SQLite nel database `.cme`:

```sql
CREATE TABLE IF NOT EXISTS ChangeLog (
    ChangeId    INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId   INTEGER NOT NULL,
    ElementUniqueId TEXT NOT NULL,
    PriceItemCode   TEXT NOT NULL,
    ChangeType      TEXT NOT NULL, -- Created|Updated|Deleted|ModelDiffAccepted
    OldValueJson    TEXT,
    NewValueJson    TEXT,
    UserId          TEXT NOT NULL,
    Timestamp       TEXT NOT NULL
);
```

Modello C#:

```csharp
public class ChangeLogEntry
{
    public int ChangeId { get; set; }
    public int SessionId { get; set; }
    public string ElementUniqueId { get; set; } = "";
    public string PriceItemCode { get; set; } = "";
    public string ChangeType { get; set; } = "";
    public string? OldValueJson { get; set; }
    public string? NewValueJson { get; set; }
    public string UserId { get; set; } = "";
    public DateTime Timestamp { get; set; }
}
```

### 4. IUserContext

```csharp
interface IUserContext
{
    string UserId { get; }
}

class WindowsUserContext : IUserContext
{
    public string UserId => Environment.UserName;
}
```

Registrato come singleton nel `QtoApplication`. In futuro sostituibile con Azure AD senza cambiare i consumer.

---

## Sprint 7 — UI Prezzario + Preferiti + MappingRules

### 1. Toggle Prezzario dal ribbon

Il `CatalogBrowserWindow` esistente viene istanziato **una sola volta** all'avvio (`Application_Started`) e poi mostrato/nascosto via `Show()`/`Hide()`. Il pulsante ribbon usa `ToggleButton` con stato checked/unchecked sincronizzato con la visibilità della finestra.

```
Ribbon Tab "CME"
  [■ CME]        → toggle DockablePane CME (già implementato)
  [★ Prezzario]  → toggle CatalogBrowserWindow
```

### 2. Sezione Preferiti nel CatalogBrowserWindow

Struttura UI (WPF, aggiunta al layout esistente):

```
┌─ PREZZARIO ──────────────────────────────────┐
│ [Listino attivo: Toscana 2025/1 ▼]           │
│ 🔍 Cerca...                    [Cerca]        │
│ ──────────────────────────────────────────── │
│ ⭐ PREFERITI        [Salva set ▼] [Carica set]│
│   ▸ A.02.001 – Muratura   € 42,00 /m²        │
│   ▸ B.01.003 – Intonaco   € 18,50 /m²        │
│   [+ Aggiungi dal listino]  [Rimuovi]        │
│ ──────────────────────────────────────────── │
│ 📋 TUTTE LE VOCI                             │
│   (TreeView esistente)                       │
│ ──────────────────────────────────────────── │
│ Param quantitativo: [Area ▼]                 │
│ [✓ ASSEGNA AGLI ELEMENTI SELEZIONATI]        │
└──────────────────────────────────────────────┘
```

### 3. File Preferiti

```
Globale:   %AppData%\CmePlugin\Favorites\default.json
Progetto:  <cartella del .cme>\favorites.json   ← sovrascrive globale se presente
```

Formato `FavoriteSet.json`:

```json
{
  "name": "Ristrutturazione civile",
  "createdAt": "2026-04-22T10:00:00Z",
  "items": [
    { "code": "A.02.001", "shortDesc": "Muratura mattoni", "unit": "m²", "unitPrice": 42.0 },
    { "code": "B.01.003", "shortDesc": "Intonaco civile",  "unit": "m²", "unitPrice": 18.5 }
  ]
}
```

Logica di risoluzione: il `FileFavoritesRepository` carica globale, poi merge/override con il file progetto se esiste.

### 4. MappingRules JSON

```
Globale:   %AppData%\CmePlugin\QTO_MappingRules.json
Progetto:  <cartella del .cme>\QTO_MappingRules.json   ← sovrascrive globale
```

Formato:

```json
{
  "version": 1,
  "rules": [
    {
      "revitCategory": "OST_Walls",
      "defaultParam": "Area",
      "allowedParams": ["Area", "Volume", "Length", "Count"],
      "hashParams": ["Area", "Volume"],
      "unitDisplay": "m²",
      "roundingDecimals": 2,
      "vuotoPerPieno": true
    },
    {
      "revitCategory": "OST_Floors",
      "defaultParam": "Area",
      "allowedParams": ["Area", "Volume", "Count"],
      "hashParams": ["Area", "Volume"],
      "unitDisplay": "m²",
      "roundingDecimals": 2,
      "vuotoPerPieno": false
    },
    {
      "revitCategory": "OST_StructuralFraming",
      "defaultParam": "Length",
      "allowedParams": ["Length", "Count"],
      "hashParams": ["Length"],
      "unitDisplay": "m",
      "roundingDecimals": 2,
      "vuotoPerPieno": false
    }
  ]
}
```

`hashParams` — lista di parametri inclusi nel calcolo dell'hash per il ModelDiff (Sprint 8).

Il dropdown "Param quantitativo" nel pannello Prezzario si preseleziona su `defaultParam` della categoria degli elementi attualmente selezionati in Revit. Se gli elementi selezionati appartengono a categorie diverse, il dropdown rimane libero.

### 5. Flusso assegnazione bidirezionale

**Modalità A — voce → elementi:**
1. Click voce EP nei Preferiti o nel TreeView → voce diventa "attiva" (evidenziata in blu)
2. Selezione elementi in Revit (filtri / manuale / selezione esistente)
3. Click `[ASSEGNA]` → `QuantityExtractor` + `IQtoRepository.InsertAssignment` + `IQtoRepository.AppendChangeLog`

**Modalità B — elementi → voce:**
1. Selezione elementi in Revit
2. Click voce EP nel pannello → diventa "attiva"
3. Click `[ASSEGNA]` → stessa operazione

**Regola abilitazione pulsante:**
```
[ASSEGNA] abilitato ⟺ (voce attiva != null) AND (elementi selezionati in Revit > 0)
```

La selezione attiva in Revit viene monitorata tramite `SelectionChangedEvent` già implementato in `SelectionService`.

---

## Sprint 8 — Model Diff Check

### 1. Snapshot per elemento (salvato ad ogni assegnazione)

Aggiungere a `QtoAssignmentEntry` (JSON nel `.cme`) e alla tabella SQLite:

```csharp
public class ElementSnapshot
{
    public int ElementId { get; set; }
    public string UniqueId { get; set; } = "";
    public string SnapshotHash { get; set; } = "";  // SHA256 troncato 12 hex
    public double SnapshotQty { get; set; }
    public List<string> AssignedEP { get; set; } = new();
    public DateTime LastUpdated { get; set; }
}
```

**Calcolo hash leggero** (SHA256, primi 12 caratteri hex):

```csharp
private string ComputeElementHash(Element elem, MappingRule rule)
{
    var sb = new StringBuilder();
    sb.Append(elem.UniqueId);
    foreach (var paramName in rule.HashParams)
    {
        var p = elem.LookupParameter(paramName)
             ?? elem.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
        sb.Append(p?.AsDouble().ToString("F6") ?? "null");
    }
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
    return Convert.ToHexString(bytes)[..12];
}
```

Compatibile entrambi i target (net48/net8): `SHA256.HashData` disponibile da .NET 5+ e .NET Framework 4.7.2+.

### 2. Dialog apertura CME esistente

Mostrato da `SessionManager.OpenExisting()` se il file `.cme` contiene snapshot:

```
┌─ Apri CME esistente ─────────────────────────┐
│ File: Edificio_A_v3.cme                      │
│ Ultima sessione: 18/04/2026 14:32            │
│                                              │
│ Vuoi verificare le modifiche al modello      │
│ Revit rispetto all'ultima sessione?          │
│                                              │
│  [✓ Sì, verifica modifiche]    [Salta]      │
└──────────────────────────────────────────────┘
```

### 3. ModelDiffService

```csharp
public class ModelDiffService
{
    public ModelDiffResult ComputeDiff(Document doc, IReadOnlyList<ElementSnapshot> snapshots, MappingRules rules);
}

public class ModelDiffResult
{
    public List<DiffEntry> Deleted  { get; } = new(); // ElementId non più nel modello
    public List<DiffEntry> Modified { get; } = new(); // Hash cambiato
    public List<Element>   Added    { get; } = new(); // Nel modello ma non nel CME
}

public class DiffEntry
{
    public ElementSnapshot Snapshot { get; set; }
    public Element? CurrentElement  { get; set; } // null se eliminato
    public double OldQty            { get; set; }
    public double NewQty            { get; set; }
    public string Delta             => $"{NewQty - OldQty:+0.##;-0.##;0}";
}
```

### 4. Pannello riconciliazione (WPF Window)

```
┌─ MODIFICHE MODELLO RILEVATE ──────────────────────────────────┐
│ Rispetto al 18/04/2026:                                       │
│                                                               │
│ 🔴 ELIMINATI (3)                                             │
│   Muro 112345  → A.02.001 Muratura   [Seleziona] [Accetta]  │
│   Muro 112346  → A.02.001 Muratura   [Seleziona] [Accetta]  │
│   Solaio 98712 → C.01.002 Solaio     [Seleziona] [Accetta]  │
│                                                               │
│ 🟡 MODIFICATI (2)                                            │
│   Muro 112350  12.4→15.2m² (+2.8)    [Seleziona] [Accetta]  │
│   Finestra 44210  posizione cambiata  [Seleziona] [Accetta]  │
│                                                               │
│ 🔵 NUOVI (5) — non ancora computati                         │
│   Muro 115001  8.3m²                 [Seleziona] [Ignora]   │
│   + altri 4…                                                 │
│                                                               │
│ [✓ Accetta TUTTO]   [✗ Ignora TUTTO]   [Chiudi]             │
└───────────────────────────────────────────────────────────────┘
```

Pulsante `[Seleziona]` → `SelectionService.SelectElements([elementId])` per evidenziare in Revit.

### 5. Applicazione modifiche

| Azione utente | Effetto CME | ChangeLog entry |
|---|---|---|
| Accetta eliminato | `Status = Deleted`, quantità EP ridotta | `ModelDiffAccepted / Deleted` |
| Accetta modificato | Quantità aggiornata, `Version++` | `ModelDiffAccepted / Updated` |
| Nuovo elemento | Aggiunto al pool "da computare" | — |
| Ignora | CME invariato, flag `⚠ dati non aggiornati` | — |

Tutte le azioni sono **reversibili fino al salvataggio esplicito** tramite `UndoStack` in memoria.

---

## Compatibilità cross-version

Tutto il codice nuovo usa solo API disponibili da Revit 2022+:
- `SpecTypeId`, `UnitTypeId`, `ForgeTypeId` — R2022+
- `SHA256.HashData` — .NET Framework 4.7.2+ e .NET 5+
- `System.Text.Json` — via NuGet su net48, built-in su net8

Nessun `ParameterType`, `DisplayUnitType`, `ElementId.IntegerValue` su net8.

---

## File e path di riferimento

| Artefatto | Path |
|---|---|
| Interfacce repository | `QtoRevitPlugin.Core/Data/IQtoRepository.cs` (nuovo) |
| ChangeLogEntry model | `QtoRevitPlugin.Core/Models/ChangeLogEntry.cs` (nuovo) |
| ElementSnapshot model | `QtoRevitPlugin.Core/Models/ElementSnapshot.cs` (nuovo) |
| SqliteQtoRepository | `QtoRevitPlugin.Core/Data/QtoRepository.cs` (esteso) |
| FileFavoritesRepository | `QtoRevitPlugin.Core/Data/FileFavoritesRepository.cs` (nuovo) |
| MappingRulesService | `QtoRevitPlugin.Core/Services/MappingRulesService.cs` (nuovo) |
| ModelDiffService | `QtoRevitPlugin/Services/ModelDiffService.cs` (nuovo) |
| ReconciliationWindow | `QtoRevitPlugin/UI/Views/ReconciliationWindow.xaml` (nuovo) |
| CatalogBrowserWindow | `QtoRevitPlugin/UI/CatalogBrowserWindow.xaml` (esteso) |
| IUserContext | `QtoRevitPlugin.Core/Services/IUserContext.cs` (nuovo) |
| FavoriteSet model | `QtoRevitPlugin.Core/Models/FavoriteSet.cs` (nuovo) |
| MappingRule model | `QtoRevitPlugin.Core/Models/MappingRule.cs` (nuovo) |
