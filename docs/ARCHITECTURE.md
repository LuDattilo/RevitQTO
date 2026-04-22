# RevitQTO — Architettura dati a 2 livelli

> Versione: 22 aprile 2026 · Stato: baseline Sprint 4

## Principio cardine

**Separare listini riusabili (dati di riferimento) da decisioni progettuali (assegnazioni QTO).**
I primi vivono globali per-utente; i secondi vivono nel `.rvt` e nel `.cme`.

## Due livelli di storage

```
┌─────────────────────────────────────────────────────────────┐
│  LIVELLO 1 — UserLibrary globale (per-utente)               │
│  %AppData%\QtoPlugin\UserLibrary.db                         │
│  ├── PriceLists     (listini importati)                     │
│  ├── PriceItems     (voci EP)                               │
│  ├── PriceItems_FTS (indice full-text)                      │
│  └── NuoviPrezzi    (NP di repertorio utente)               │
│                                                             │
│  Importi un listino UNA volta → disponibile in ogni computo │
└─────────────────────────────────────────────────────────────┘
                              │
                              │ PriceList.PublicId (GUID)
                              ▼
┌─────────────────────────────────────────────────────────────┐
│  LIVELLO 2 — Per progetto                                   │
│                                                             │
│  A) File .cme (SQLite sibling al .rvt)                      │
│     ├── Sessions       (metadata sessione)                  │
│     ├── QtoAssignments (assegnazioni EP→elementId)          │
│     ├── ManualItems    (Sorgente C voci manuali)            │
│     └── RoomMappings   (Sorgente B formule NCalc)           │
│                                                             │
│  B) .rvt Extensible Storage (per portabilità cross-PC)      │
│     ├── ES su ProjectInformation:                           │
│     │   └── ProjectPriceListSnapshot (JSON)                 │
│     │       • ListPublicId (ref a UserLibrary)              │
│     │       • UsedItems: solo voci effettivamente usate     │
│     │       • ~25KB tipico (30-50 voci × 500 byte)          │
│     └── ES su Element (schema v1):                          │
│         └── QtoElementData                                  │
│             • AssignedEpCodes: IList<string>                │
│             • Source (RevitElement/Room/Manual)             │
│             • LastTagged, ExclusionReason                   │
└─────────────────────────────────────────────────────────────┘
```

## Scenari di utilizzo

### Scenario 1 — Progettista singolo su un PC
- Importa il listino regionale in UserLibrary → lo riusa per tutti i progetti
- Il .rvt contiene solo assegnazioni; la ricerca/sfoglia passa dalla UserLibrary
- Nessuno snapshot necessario

### Scenario 2 — Collaborazione cross-PC (es. consegna a DL)
- Progettista A: .rvt + UserLibrary sul suo PC
- DL su PC B: apre il .rvt senza UserLibrary
- Il plugin legge `ProjectPriceListSnapshot` dal DataStorage → vede le voci usate
- Banner: "Listino completo non disponibile — import listino per aggiungere nuove voci"
- DL può comunque: visualizzare computo, rigenerare report, fare export
- Se il DL importa il proprio listino con stesso `PublicId` → tutto torna seamless

### Scenario 3 — Cambio PC / reinstall
- Utente ricrea UserLibrary da zero → ri-importa listini
- I .rvt esistenti hanno snapshot locali → restano leggibili
- Re-import del listino con stesso `PublicId` → ricostruisce il mapping completo

## Componenti software

| Componente | Responsabilità | Sprint |
|---|---|---|
| `UserLibraryManager` | Singleton owner della UserLibrary.db | 4 ✅ |
| `SessionManager` | Owner del .cme corrente | 1 ✅ |
| `QtoRepository` | CRUD SQLite (riusato per entrambi i DB) | 1-4 ✅ |
| `ExtensibleStorageRepo` | Read/Write `QtoElementData` su ES per-elemento | 3 ✅ |
| `ProjectSnapshotService` | Write/Read `ProjectPriceListSnapshot` su DataStorage | **5 (pending)** |
| `SharedParameterManager` | Binding `QTO_Codice`/etc. su elementi | 3 ✅ |
| `PriceItemSearchService` | Ricerca 3-livelli (Exact/FTS5/Fuzzy) sulla UserLibrary | 2 ✅ |

## Quando si popola lo snapshot

Sprint 5 Tagging:

```csharp
// Pseudo-codice — Sprint 5 TaggingHandler.ExecuteInsertAssignment()
using var tx = new Transaction(doc, "CME — Assegna EP");
tx.Start();

// 1. Scrivi ES per-elemento
_esRepo.Write(element, new QtoElementData { AssignedEpCodes = {code} });

// 2. Upsert nel ProjectPriceListSnapshot
var snapshot = _snapshotService.ReadOrCreate(doc);
snapshot.UsedItems.UpsertByCode(priceItem);  // idempotente
snapshot.SnapshotUpdatedAt = DateTime.UtcNow;
_snapshotService.Write(doc, snapshot);

tx.Commit();
```

## Rischi noti

- **Snapshot dimension bound**: se progetto usa >120 voci distinte, serializzazione JSON supera ~64KB (limite ES per-Entity).
  Mitigazione: split su più DataStorage con chunking, o comprimi con gzip prima della write.
- **PublicId collision**: listini importati indipendentemente da 2 utenti avranno GUID diversi anche se stesso file sorgente.
  Mitigazione futura: derivare GUID da hash del file + versione.
