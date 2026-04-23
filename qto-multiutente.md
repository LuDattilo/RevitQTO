# Revit QTO Plugin – Architettura Multiutente (Roadmap)

## Scopo del documento

Questo documento definisce una possibile evoluzione **multiutente** del plug‑in Revit QTO in cui più persone lavorano sullo stesso contenuto (modello + CME) in momenti differenti, con tracciabilità completa di chi ha fatto le modifiche.

L’obiettivo è:
- permettere una futura migrazione verso un backend condiviso (on‑prem o Azure) senza riscrivere il codice;
- definire sin da ora i punti di estensione (interfacce, campi, file) da progettare in modi “cloud‑ready”.

---

## Scenario multiutente

Requisiti funzionali principali:

- **Uso non contemporaneo**: utenti diversi lavorano sullo stesso progetto in tempi diversi (no collaborazione realtime obbligatoria).
- **Tracciabilità**: per ogni assegnazione/variazione di computo si deve sapere:
  - chi ha effettuato la modifica (UserId);
  - quando (timestamp);
  - su quale elemento Revit e con quale voce di prezzario;
  - che cosa è cambiato (da → a).
- **Condivisione dei dati QTO**:
  - listini e Nuovi Prezzi (NP) comuni allo studio;
  - CME condiviso tra più computisti;
  - preferiti riutilizzabili tra progetti e utenti.

Revit rimane installato localmente su ogni macchina; è il **layer dati** che evolve da storage locale a storage centralizzato.

---

## Architettura attuale (sintesi)

- **Revit + Add‑in locale**
  - DockablePane CME e Prezzario lato client.
  - ExternalEvent per operazioni sul modello.
- **Storage locale**
  - `UserLibrary.db` (SQLite) per listini, mapping categorie/parametri, regole.
  - File `.cme` per le sessioni di computo.
  - Extensible Storage / DataStorage nel `.rvt` per snapshot di configurazione e assegnazioni.

Questa architettura è ottima per uso **single‑user** (o comunque per computi isolati), ma non garantisce condivisione e audit centralizzato.

---

## Architettura target multiutente (high‑level)

### 1. Backend centrale (API + DB)

Introdurre un servizio centrale che espone API verso il plug‑in:

- **API REST (es. ASP.NET Core)**
  - `/projects` – gestione progetti/QTO.
  - `/priceLists` – listini, edizioni, NP.
  - `/assignments` – assegnazioni elemento ↔ voce EP.
  - `/favorites` – set di preferiti per utente/progetto.
  - `/diff` – servizi ModelDiff / riconciliazione CME.

- **Database server (SQL Server / Postgres / Azure SQL)**
  - Tabelle chiave:
    - `Projects(ProjectId, Name, Status, CreatedBy, CreatedAt, ...)`;
    - `PriceLists(ListId, Name, Version, Source, ...)`;
    - `PriceItems(PriceItemId, ListId, Code, Description, Unit, UnitPrice, IsNP, ...)`;
    - `Assignments(AssignmentId, ProjectId, ElementUniqueId, PriceItemId, Quantity, Unit, UnitPrice, Total, CreatedBy, CreatedAt, ModifiedBy, ModifiedAt, Version, Status, GeometryHash, SnapshotQty, ...)`;
    - `Favorites(FavoriteSetId, OwnerUserId, ProjectId NULLABLE, Name, JsonDefinition, ...)`;
    - `ChangeLog(ChangeId, ProjectId, ElementUniqueId, PriceItemId, ChangeType, OldValueJson, NewValueJson, UserId, Timestamp)`.

Il plug‑in parla con l’API via HTTPS; non accede mai direttamente al DB.


### 2. Add‑in Revit (lato client)

L’architettura Revit rimane simile, ma il layer di storage è astratto:

- DockablePane CME e Prezzario come oggi.
- `QtoService` (logica applicativa) che usa un repository astratto:
  - `IQtoRepository` (CME, assignments, change log);
  - `IPriceListRepository` (listini, NP, items);
  - `IFavoritesRepository` (preferiti).
- Implementazioni concrete:
  - oggi: `SqliteQtoRepository` (e analoghi) → storage locale;
  - domani: `ApiQtoRepository` → chiamate verso il backend.

Il codice UI/WPF e la logica di calcolo rimangono sostanzialmente invariati; cambia solo il “motore” di persistenza.

---

## Tracciabilità delle modifiche (audit trail)

### 1. Campi da aggiungere alle assegnazioni

Alla struttura di assegnazione elemento ↔ voce EP (oggi salvata in ES/.cme) si aggiungono subito i seguenti campi:

- `CreatedBy` – identificativo utente (es. login Windows o codice computista).
- `CreatedAt` – timestamp creazione.
- `ModifiedBy` – identificativo utente che ha effettuato l’ultima modifica (nullable se mai modificato).
- `ModifiedAt` – timestamp ultima modifica.
- `Version` – numero di versione (int, default 1, incrementato ad ogni modifica).
- `Status` – stato logico (es. `Active`, `Deleted`, `Superseded`).

Strategie possibili per la gestione delle modifiche:

- **Update in place**: si aggiornano i campi `ModifiedBy/At` e si incrementa `Version`.
- **Versioning completo**: si crea una nuova riga con `Version++` e si marca la precedente come `Superseded`. Questa opzione è più adatta alla futura persistenza su DB centrale.

### 2. ChangeLog append‑only

Definire da subito un log append‑only delle modifiche:

- `ChangeId` – chiave univoca.
- `ProjectId` – progetto di riferimento.
- `ElementUniqueId` – elemento Revit.
- `PriceItemCode` / `PriceItemId` – voce EP coinvolta.
- `ChangeType` – tipo di operazione (`Created`, `Updated`, `Deleted`, `Reassigned`, `ModelDiffAccepted`, ...).
- `OldValueJson` – stato precedente (quantità, prezzo, totale, ecc.).
- `NewValueJson` – nuovo stato.
- `UserId` – utente che ha effettuato l’operazione.
- `Timestamp` – istante dell’operazione.

In fase **single‑user** questo log può essere:

- tenuto dentro il `.cme` come sezione JSON;
- oppure in una tabella dedicata nel `UserLibrary.db` locale.

In fase **multiutente** la stessa struttura viene portata nel DB centrale.

### 3. Identità utente

- **Subito**: usare come `UserId` il nome utente Windows (`Environment.UserName`) o un codice computista configurato nel plug‑in.
- **In futuro (Azure)**: sostituire `UserId` con l’identità Azure AD (UPN o GUID) ottenuta tramite autenticazione sul backend.

La struttura dei dati rimane identica; cambia solo la sorgente del valore.

---

## Evoluzione per fasi

### Fase 1 – “Cloud‑ready” in locale (da fare subito)

Obiettivi:

- non cambiare il flusso utente;
- rendere il codice pronto per un backend remoto;
- iniziare a tracciare chi fa cosa.

Attività:

1. **Introdurre il layer di astrazione storage**
   - Definire le interfacce `IQtoRepository`, `IPriceListRepository`, `IFavoritesRepository`.
   - Implementare `SqliteQtoRepository` (o analogo) che usa SQLite/file attuali.
   - Fare in modo che la UI/WPF usi solo servizi e repository astratti.

2. **Arricchire i DTO di assegnazione**
   - Aggiungere `CreatedBy/CreatedAt/ModifiedBy/ModifiedAt/Version/Status` a `QtoAssignment`.
   - Aggiornare la scrittura in ES/.cme per salvare questi campi.

3. **Implementare il ChangeLog locale**
   - Definire il modello `ChangeLogEntry`.
   - A ogni operazione significativa (crea/modifica/elimina assegnazione, accetta ModelDiff) scrivere una entry.

4. **Gestione `CurrentUser`**
   - Introdurre un servizio `IUserContext` che restituisce `UserId` corrente.
   - Implementazione base: nome utente Windows o valore configurato.

Queste modifiche sono necessarie comunque e ridurranno drasticamente il lavoro futuro.


### Fase 2 – Backend listini/NP condiviso (opzionale intermedio)

Obiettivi:

- condividere listini/NP/favoriti in studio senza toccare subito il CME.

Attività:

- Implementare un backend (on‑prem o Azure) per:
  - listini (XML/XPWE importati una volta);
  - price items (EP + NP);
  - set di preferiti.
- Implementare `ApiPriceListRepository` e `ApiFavoritesRepository`.
- Mantenere CME e ChangeLog ancora in locale.


### Fase 3 – CME centralizzato (multiutente vero)

Obiettivi:

- memorizzare CME, assegnazioni e ChangeLog in DB centrale.
- permettere a più utenti di lavorare in tempi diversi sullo stesso computo.

Attività:

- Implementare `ApiQtoRepository` che usa le API per leggere/scrivere assignments e ChangeLog.
- Mappare le strutture attuali (già arricchite di metadata) verso le tabelle del DB centrale.
- Lasciare eventualmente il `.cme` locale come formato di export/import o backup.

---

## Ruolo di Azure nella futura evoluzione

Quando si vorrà passare a un backend centralizzato, Azure fornisce componenti utili:

- **Azure App Service / Container Apps** per ospitare l’API REST.
- **Azure SQL Database** per il DB relazionale (listini, CME, ChangeLog, utenti).
- **Azure Storage (Blob)** per esportazioni (Excel, TSV PriMus, snapshot `.cme`).
- **Azure AD / Entra ID** per autenticazione degli utenti e gestione ruoli/permessi.

Dal punto di vista del plug‑in, la transizione consisterà principalmente nel cambiare le implementazioni dei repository (da SQLite locale a API Azure), riusando l’intera logica applicativa già predisposta.

---

## Sintesi per lo sviluppo attuale

Per minimizzare lavoro futuro è importante che, già **nella versione single‑user**, vengano fatte subito le seguenti scelte progettuali:

1. Usare **repository astratti** (`IQtoRepository`, ecc.) invece di accedere direttamente a SQLite/.cme in tutta la UI.
2. Arricchire **da ora** le entità di assegnazione con metadata di audit (`CreatedBy`, `CreatedAt`, `ModifiedBy`, `ModifiedAt`, `Version`, `Status`).
3. Introdurre un **ChangeLog append‑only**, anche se inizialmente solo locale.
4. Introdurre un servizio `IUserContext` per centralizzare la gestione dell’identità utente.

Con queste basi, l’aggiunta di un backend multiutente (on‑prem o Azure) richiederà principalmente lavoro sul lato server, lasciando quasi invariati UI e flussi utente del plug‑in.