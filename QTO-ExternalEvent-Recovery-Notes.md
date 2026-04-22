# QTO Plugin – Note tecniche: ExternalEvent & Recovery SQLite↔ES

**Data**: 22/04/2026
**Scope**: approfondimento tecnico supplementare a [QTO-Plugin-Documentazione-v3.md](QTO-Plugin-Documentazione-v3.md) §2.2 e [QTO-Implementazioni-v3.md](QTO-Implementazioni-v3.md). Questo documento copre due aree che richiedono decisioni architetturali esplicite prima di Sprint 0 e Sprint 1 rispettivamente.

---

## 1. Pattern `ExternalEvent` + Revit Threading Model

### 1.1 Il problema: API Revit single-threaded

La Revit API è **rigorosamente monothread**: tutte le letture/scritture sul `Document` devono avvenire sul thread Revit principale e solo dentro un "modal context" (comando attivo). Una WPF modeless vive sul proprio thread UI: se dal suo `ViewModel` si chiamasse `doc.GetElement(id)` direttamente, Revit lancia:

- `InvalidOperationException: Attempting to modify the document outside of a transaction`, oppure
- `Autodesk.Revit.Exceptions.InvalidOperationException: API call inside a non-modal context`

### 1.2 Ciclo di vita di `ExternalEvent`

```
┌─────────────────┐      (1) Create in OnStartup
│ IExternalApp    │ ────────────────────────────┐
└─────────────────┘                             ▼
                                  ┌──────────────────────────┐
   ┌──────────┐  (2) binding      │  ExternalEvent instance  │
   │ ViewModel│ ─────────────────►│  + IExternalEventHandler │
   └──────────┘                   └──────────────────────────┘
        │                                       │
        │  (3) set handler.Data                 │
        │  (4) externalEvent.Raise()            │
        ▼                                       ▼
   ┌──────────┐                      ┌──────────────────────┐
   │ UI thread│                      │ Revit thread (idle)  │
   │ prosegue │◄─────────────────────│ eventually calls     │
   │ (async)  │   (5) callback via   │ Handler.Execute(app) │
   └──────────┘       eventi WPF     └──────────────────────┘
```

### 1.3 Regole ferree

| Regola | Motivazione | Conseguenza se violata |
|---|---|---|
| Creare `ExternalEvent` **una sola volta** in `OnStartup` e cacharlo | Ogni istanza occupa uno slot nella queue Revit | Memory leak progressivo, Revit rallenta |
| `Raise()` è **idempotente**: chiamate ripetute prima di `Execute` sono collassate | Design Revit anti-flood | **Disabilitare bottone INSERISCI** mentre evento in volo, altrimenti perdita silente input |
| `Execute(UIApplication app)` gira **sul thread Revit** con API context completo | Unico punto dove aprire `Transaction` | Scritture dirette dal VM crashano casualmente |
| `handler.Data` come struct di input | Passaggio parametri VM→handler type-safe | Evitare variabili statiche/singleton per i payload |
| ViewModel non chiama **MAI** Revit API | Threading violation | Funziona il 95% delle volte, crasha il 5% in modo non debuggabile |

### 1.4 Raccomandazione: usare `Revit.Async` (NuGet già in lista `QTO-Plugin-Documentazione-v3.md` §1.2)

Senza wrapper (verboso):

```csharp
_writeHandler.TargetIds = ids;
_writeHandler.EpCode    = code;
_externalEvent.Raise();
// fine del VM: non sai quando finisce, devi sottoscrivere evento interno
```

Con `Revit.Async` (lineare, `await`-friendly):

```csharp
var result = await RevitTask.RunAsync(app =>
{
    var doc = app.ActiveUIDocument.Document;
    using var tx = new Transaction(doc, "Assegna QTO");
    tx.Start();
    foreach (var id in ids)
    {
        var elem = doc.GetElement(id);
        elem.LookupParameter("QTO_Codice")?.Set(code);
    }
    tx.Commit();
    return computedTotal;
});
// qui hai già il risultato, codice lineare
```

**Vantaggi**:
- Riduce di ~60% il boilerplate handler↔VM
- Elimina la classe di bug "handler.Data stantio tra `Raise()` consecutive"
- Compatibile col pattern MVVM `CommunityToolkit.Mvvm` (AsyncRelayCommand)

**Riferimento implementativo**: `RevitCortex v1.0.13` utilizza `Nice3point.Revit.Toolkit.dll` che fornisce lo stesso pattern (`RevitTask.RunAsync`). È un'opzione consolidata, adottare da Sprint 0.

### 1.5 Alternative storiche da NON usare

- **`UIApplication.Idling`**: era il pattern canonico prima di ExternalEvent (Revit 2012 e precedenti). Ancora disponibile ma oggi usato solo per monitoring passivo (es. riflettere selezione Revit → UI plug-in). Non adatto per scritture: Idling gira a ~10Hz, ad ogni fire la logica deve decidere "devo fare qualcosa?" → overhead inutile.
- **Transaction direct dal VM thread**: impossibile, lancia eccezione.
- **`UIApplication.ExecuteAddinCommand()`**: permette di invocare un `IExternalCommand` da codice, ma richiede comunque il modal context. Non risolve il problema della modeless WPF.

### 1.6 Insight operativi

- **Golden rule**: il ViewModel non tocca **mai** la Revit API. Tutte le chiamate passano da un handler. Se ti trovi a fare `LookupParameter` nel VM, stai violando il threading model.
- **Un handler per operazione distinta**: `WriteQtoHandler`, `IsolateElementsHandler`, `OverrideColorHandler`, `NavigateToElementHandler`. Evitare un unico "GodHandler" con switch sul tipo operazione.
- **Handler devono essere stateless rispetto all'istanza**: lo stato sta in `handler.Data` settato dal VM prima di `Raise()`. Mai usare campi con semantica "persistente fra esecuzioni".

---

## 2. Recovery SQLite ↔ Extensible Storage

### 2.1 La tripletta di persistenza

Il plug-in tiene **tre copie** degli stessi dati, ciascuna con un ruolo preciso:

| Canale | Posizione | Autoritativo per | Problema tipico |
|---|---|---|---|
| **SQLite** | `%AppData%\QtoPlugin\db\{Nome}.db` | Prezzi, quantità calcolate, sessioni, NP, diff log | Perso se utente cambia PC o cancella `%AppData%` |
| **Extensible Storage** | Dentro il `.rvt` | Multi-EP per elemento (lista JSON), preset filtri | Limite ~2MB/schema, versioning critico |
| **Shared Parameters** | `QTO_Codice`, `QTO_DescrizioneBreve`, `QTO_Stato` sull'elemento | Ultima assegnazione visibile in schedule Revit | Può essere modificato manualmente da chiunque apra il modello |

**Filosofia**: scelta *ES-centric* → **il modello `.rvt` è la verità**, SQLite è cache performante. È la scelta corretta per BIM: il modello è l'artefatto contrattuale che viaggia, il DB è strumento del singolo utente.

### 2.2 Scenari di divergenza

| # | Scenario | Causa | Canale autoritativo |
|---|---|---|---|
| 1 | DB cancellato, nuovo PC | User change/reinstall | ES (modello) |
| 2 | Modello condiviso via Cloud/Worksharing | Collega apre prima di installare plug-in | ES + SP |
| 3 | Utente edita manualmente `QTO_Codice` | Edit in property palette Revit | SP (ultimo edit) |
| 4 | Ripristino backup `.rvt` a data precedente | Rollback utente | DB (se più recente) |
| 5 | Due utenti lavorano offline su copie del modello | Merge manuale | Conflitto esplicito |

### 2.3 Algoritmo di riconciliazione (Sprint 1)

```
1. All'evento DocumentOpened:
   a. leggi ES schema version dall'header (se esiste)
   b. leggi ultimo ModelSnapshotDate da SQLite.Sessions
   c. leggi doc.ProjectInformation param "QtoLastSync" (shared param speciale)

2. Classifica scenario:
   - ES.ts > DB.ts  → modello è più recente (scenario 1 o 2)
   - DB.ts > ES.ts  → DB è più recente (scenario 4)
   - abs(diff) < 5s → allineati, nessuna azione

3. Dialog utente:
   ┌──────────────────────────────────────────────────────┐
   │ ⚠ Discrepanza dati rilevata                          │
   │                                                      │
   │ Il modello contiene 847 assegnazioni QTO,            │
   │ ma il DB locale ne ha solo 142.                      │
   │                                                      │
   │ ○ Importa dati dal modello al DB (consigliato)       │
   │ ○ Esporta dati dal DB al modello (sovrascrivi)       │
   │ ○ Merge intelligente (per elemento: prende il più    │
   │   recente tra ES.AssignedAt e DB.AssignedAt)         │
   │                                                      │
   │ [Procedi] [Annulla e ignora] [Esporta report diff]   │
   └──────────────────────────────────────────────────────┘

4. Merge intelligente (opzione 3):
   per ogni UniqueId in (ES ∪ DB):
     ES.entry = readExtensibleStorage(elementId)
     DB.entry = queryQtoAssignments(uniqueId)
     winner   = max(ES.entry.ModifiedAt, DB.entry.ModifiedAt)
     applyToBoth(winner)
```

### 2.4 Shared Param `QtoLastSync` su `ProjectInformation` — il heartbeat

**Trick architetturale**: un singolo campo `DateTime` (come Text ISO-8601) su `ProjectInformation` che:
- Viaggia col modello ovunque (anche Cloud Worksharing)
- Aggiornato ad ogni `WriteQtoHandler.Execute()` completato con successo
- Serve da "heartbeat" per il reconciliation senza dover parsare tutti gli elementi

```csharp
// In WriteQtoHandler.Execute, dopo tx.Commit():
var projInfo = doc.ProjectInformation;
projInfo.LookupParameter("QtoLastSync")?.Set(
    DateTime.UtcNow.ToString("o"));  // ISO 8601 UTC
```

Costo: 1 Shared Param. Risparmio: evitare scansione full del modello per detectare "è cambiato qualcosa?".

### 2.5 Scenario 3 (edit manuale) — policy esplicita

Il più insidioso: utente edita `QTO_Codice` a mano da property palette Revit. Stato risultante:
- ES ha ancora la lista `[{Code:"A.02.001", ...}, {Code:"B.01.003", ...}]`
- Shared Param dice `"A.03.015"`
- DB dice `"A.02.001"`

**Policy consigliata**:

> Se `QTO_Codice` (Shared Param) non matcha **nessuna** entry della lista ES `QtoAssignments`, considera l'edit manuale come "nuova assegnazione non confermata":
> - La lista ES resta invariata
> - Viene aggiunto stato `CONFLITTO` visibile in HealthCheck
> - Proposta tri-opzionale: `[Conferma nuovo codice] [Ripristina lista] [Aggiungi come terza entry]`

Questa policy evita la perdita accidentale della cronologia multi-EP se qualcuno edita a mano senza usare la UI del plug-in.

### 2.6 Prestazioni: lettura bulk ES

Leggere ES elemento per elemento su 30.000 elementi richiede ~8–12 secondi (misurato su modelli simili). Mitigazioni:

**Approccio A — filtro C# in-memory**:
```csharp
var schema = Schema.Lookup(QtoAssignmentsSchemaGuid);
var elementsWithEs = new FilteredElementCollector(doc)
    .WhereElementIsNotElementType()
    .Cast<Element>()
    .Where(e => e.GetEntity(schema) is Entity ent && ent.IsValid())
    .ToList();
// singola passata FEC, poi filtro managed code (veloce)
```

**Approccio B — cache di sessione**:
- Leggi tutti gli ES una volta all'apertura del plug-in
- Mantieni dizionario `Dictionary<string, QtoAssignmentList>` per `UniqueId`
- Invalida puntualmente su ogni scrittura tramite handler
- Trade-off: RAM (~10MB per 30k elementi) vs tempo

**Approccio C — ES scritto in batch**:
- Mai riscrivere tutti gli elementi in un colpo solo (→ Revit "Not Responding")
- Scritture incrementali solo per elementi toccati
- Se serve migrazione schema: chunk di 500 elementi in transazioni separate

### 2.7 Versioning schema ES

Lo schema ES deve essere versionato tramite GUID distinti:

```csharp
// Schema v1 (iniziale)
public static readonly Guid SchemaGuidV1 = new("A4B2C1D0-E5F6-7890-ABCD-EF1234567890");
// Schema v2 (aggiunge campo Formula per Sorgente B)
public static readonly Guid SchemaGuidV2 = new("B5C3D2E1-F607-8901-BCDE-F23456789012");
```

**Policy di migrazione**:
1. Al DocumentOpened, cerca schema v2 → se presente, usa quello
2. Se solo v1 presente: leggi v1, costruisci oggetti in-memory, scrivi v2 con campi nuovi nulli
3. Mai cancellare v1 immediatamente: lascia 1 versione plug-in di coesistenza per rollback

### 2.8 Insight operativi

- **ES è append-only nella pratica**: modificare un campo `IList<string>` richiede `leggi → deserializza → modifica → serializza → riscrivi`. Su 30k elementi bulk mandi il modello in "Not Responding". **Strategia**: scritture incrementali per elementi toccati, mai bulk rewrite.
- **`QtoLastSync` come heartbeat**: singolo Shared Param costa poco, risparmia enormi complicazioni di reconciliation. Priorità alta per Sprint 1.
- **Scelta ES-centric**: il modello `.rvt` è autoritativo. Se un utente perde il DB locale, tutto si ricostruisce dal modello. Il contrario non è vero — il DB senza ES è inutile.
- **Tracciabilità audit**: ogni riscrittura ES deve aggiornare `ModifiedAt` nel JSON entry. Soft delete (`IsDeleted=true`) invece di rimozione fisica, per permettere ricostruzione cronologia in fase di verifica contrattuale.

---

## 3. Riferimenti

- [QTO-Plugin-Documentazione-v3.md §2.2](QTO-Plugin-Documentazione-v3.md) — Threading Model
- [QTO-Implementazioni-v3.md §I1, §I2](QTO-Implementazioni-v3.md) — Shared Params e ES duale
- RevitCortex v1.0.13 (`Nice3point.Revit.Toolkit.dll`) — riferimento implementativo `RevitTask.RunAsync`
- Building Coder – External Events: https://jeremytammik.github.io/tbc/a/0743_external_event.htm
- archi-lab.net – Extensible Storage: https://archi-lab.net/what-why-and-how-of-the-extensible-storage/
- GitHub – Revit.Async (wrapper ExternalEvent): https://github.com/KennanChan/Revit.Async
