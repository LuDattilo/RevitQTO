# Modulo Computi PriMus-Compliant + Export/Import XPWE · Design Spec

**Data:** 2026-04-24
**Stato:** Design da approvare
**Autore:** L. Dattilo + Claude (sessione brainstorming con analisi file XPWE reali)
**Supersede:** la sezione "Plan 5 — CmeEditorView" della spec `2026-04-24-tagging-refactor-cme-redazione-design.md`. Il resto di quella spec (Plan 1-4) resta valido e indipendente.

---

## 1. Problema

Il plugin QtoRevitPlugin oggi:
- non ha una struttura classificatoria PriMus-compliant (manca separazione Capitoli vs Categorie vs WBS)
- non ha entità `MeasurementRow`/`RGItem` per misurazioni con formule `n × L × L × H`
- il `PriceItem` ha un solo `UnitPrice`, mentre XPWE ne prevede 5 (`Prezzo1..5`)
- non esporta in XPWE (formato obbligatorio per committente/PA italiana)
- non importa da XPWE (flusso principale di approvvigionamento prezziari regionali)

Senza questo modulo il plugin **non è utilizzabile per un computo reale PA italiana** — la compliance PriMus/XPWE è requisito hard (Q1 = hard).

## 2. Visione

Un **modulo Computi** domain-driven che:

1. Adotta il modello dati PriMus come fonte di verità (3 alberi distinti: Capitoli/Categorie/WBS)
2. Importa ed esporta XPWE preservando la semantica PriMus (apribile in PriMus senza warning)
3. Consente flussi tipici del geometra italiano: partire da zero, partire da template, importare da prezziari regionali, esportare per PA
4. Si integra con il flusso Revit esistente: l'elemento Revit → `MeasurementRow` (VCItem) con auto-compilazione formule `n × L × L × H`

## 3. Vincoli di compliance XPWE

Lo schema XPWE è stato estratto analizzando due file reali:

- `test.XPWE` (TipoDocumento=0, 6kB) — prezziario puro (EPItem, zero VCItem)
- `CME_Sample.xpwe` (TipoDocumento=1, 1MB) — computo esportato da PriMus-DCF versione usBIM 54.00i (119 EPItem, 168 VCItem, 4491 RGItem, 6 SuperCapitoli, 5 SuperCategorie)

Regole di compliance:

- **`TipoDocumento`** discrimina: 0 = Prezziario, 1 = Computo
- **`Versione`** = 5.01 (test) / 5.04 (sample). Generiamo sempre 5.04.
- **`CopyRight`** = sempre `Copyright ACCA software S.p.A.`
- **`Fgs`** (flags documento) = preservare quello in import, default `2147614720` in nuovo
- **Date nel formato `DD/MM/YYYY`**, con `30/12/1899` = "non valorizzata" (Excel serial 0)
- **Decimali con `.`** non virgola (verificato su `<Quantita>597.00`)
- **ID numerici 1-based per item**, univoci all'interno del proprio tipo (`EPItem@ID`, `VCItem@ID`, `DGSuperCapitoliItem@ID`)
- **ID=0** negli attributi di riferimento (`<IDCap>0</IDCap>`) = "nessun riferimento"
- **Ordinamento** = ordine di serializzazione XML = ordine di presentazione in PriMus. Deterministico.

Validazione: file esportato deve essere apribile in PriMus senza warning bloccanti e ricostruire le stesse entità.

## 4. Modello dominio

### 4.1 Entità PriMus e mapping con XPWE

```
ComputoDocument (PweDocumento)
│   TipoDocumento (0=Prezziario, 1=Computo)
│   Versione (5.04)
│   DatiGenerali: Comune, Provincia, Oggetto, Committente, Impresa, ParteOpera
│   Fgs, PercPrezzi, Currency (implicito €)
│
├─ ChapterNodes (Capitoli, 3 livelli)
│  ├─ SpCap (DGSuperCapitoliItem) — PweDGSuperCapitoli
│  ├─ Cap (DGCapitoliItem) — PweDGCapitoli
│  └─ SbCap (DGSubCapitoliItem) — PweDGSubCapitoli
│  (livelli coerenti: SbCap.parent == Cap, Cap.parent == SpCap)
│
├─ CategoryNodes (Categorie, 3 livelli)
│  ├─ SpCat (DGSuperCategorieItem) — PweDGSuperCategorie
│  ├─ Cat (DGCategorieItem) — PweDGCategorie
│  └─ SbCat (DGSubCategorieItem) — PweDGSubCategorie
│
├─ WbsNodes (WBS, profondità libera)
│  ├─ WbsCapNode (WBSCAP) — riferita dal Prezziario (EPItem.CodiceWBSCAP)
│  └─ WbsComputoNode (WBS) — riferita dal Computo (VCItem.CodiceWBS)
│
├─ PriceItems (EPItem) — Elenco Prezzi
│  Fields: Tariffa, Articolo, DesRidotta, DesEstesa, UnMisura,
│          Prezzo1..5, IDSpCap, IDCap, IDSbCap, CodiceWBSCAP,
│          IncMDO, IncMAT, IncSIC, TipoRisorsa, Flags, CnfQt, AdrInternet, Data
│
└─ MeasurementRows (VCItem) — presenti solo se TipoDocumento=1
   Fields: IDEP → PriceItem, Quantita (computed), DataMis, Flags,
           IDSpCat, IDCat, IDSbCat, CodiceWBS
   │
   └─ MeasurementSubRows (RGItem) — righe formula di misura
      Fields: IDVV (Revit elementId o -N per manuale), Descrizione,
              PartiUguali, Lunghezza, Larghezza, HPeso, Quantita (derived), Flags
```

**Note semantiche chiave** (da file PriMus reale):

- `IDVV` negativi (`-2`, `-5`) = righe inserite manualmente dall'operatore
- `IDVV` positivi = riferimento a un oggetto sorgente (pensiamo Revit.ElementId — da confermare in fase di implementazione)
- `RGItem.Quantita = PartiUguali × Lunghezza × Larghezza × HPeso` (valori "0" o vuoto = fattore 1)
- `VCItem.Quantita = sum(RGItem.Quantita)` (aggregazione)
- Se `Lunghezza/Larghezza/HPeso` sono vuoti, `Quantita = PartiUguali`

### 4.2 Relazione con modello attuale del plugin

| Oggi | Modulo Computi |
|------|----------------|
| `WorkSession` (1 per progetto) | `ComputoDocument` — 1:1 con WorkSession ma con campi PriMus aggiuntivi |
| `PriceList` + `ProjectPriceListSnapshot` | `PriceList` resta come catalogo esterno; il `ComputoDocument` importa un sottoinsieme in `PriceItems` locali |
| `ComputoChapter` (ComputoStructureView) | `ChapterNode` (ma con 3 livelli fissi SpCap/Cap/SbCap) |
| `SoaCategory` | **INVARIATO** — è cosa diversa (categorie SOA contrattuali L36/2023). Non conflitta con `CategoryNode` di computo |
| `QtoAssignment` (1 per Revit element) | `MeasurementRow` (VCItem) + `MeasurementSubRow` (RGItem). La singola `QtoAssignment` attuale mappa su 1 `MeasurementSubRow` (`IDVV=elementId`) all'interno di un `MeasurementRow` (VCItem) |
| `ManualQuantityEntry` | `MeasurementSubRow` con `IDVV < 0` |
| `PriceItem.UnitPrice` singolo | `PriceItem.Prezzo1..5` (backward-compat: `UnitPrice` → `Prezzo1`) |

### 4.3 Flussi operativi supportati (Q = parti da zero / parti da template / entrambi)

**F1 — Nuovo documento da zero**
1. Utente crea `ComputoDocument` nuovo (Setup > Nuovo documento)
2. Struttura vuota: 0 ChapterNodes, 0 CategoryNodes, 0 PriceItems, 0 MeasurementRows
3. Importa un prezziario XPWE (TipoDocumento=0) → popola ChapterNodes + PriceItems
4. Oppure popola manualmente ChapterNodes + inserisce PriceItems uno alla volta
5. Inizia a creare MeasurementRows dal flusso Revit (Selezione + Tagging)

**F2 — Nuovo documento da template**
1. Utente carica un file XPWE con TipoDocumento=0 o 1 come **template**
2. Copia integrale in un nuovo `ComputoDocument` (nuovo ID, stesse classificazioni + PriceItems, MeasurementRows vuoti o duplicati)
3. Override dei dati generali (Comune, Oggetto, Committente)
4. Procede come F1 punto 5

**F3 — Import prezziario regionale**
1. Utente ha file `.xpwe` di Regione Toscana 2024 (TipoDocumento=0)
2. Il plugin legge EPItem e ChapterNodes, li mostra in un **dialog di import** con checkbox per selezionare cosa importare
3. Solo le voci selezionate finiscono nel `ComputoDocument` attivo
4. Le voci portano con sé i `ChapterNodes` referenziati (SpCap/Cap/SbCap)

**F4 — Export computo**
1. Utente esporta `ComputoDocument` → file `.xpwe` con TipoDocumento=1
2. Validazione pre-export: tutte le `MeasurementRows` hanno `PriceItem` valido, nessun riferimento orfano a ChapterNode/CategoryNode/WbsNode, quantità non null
3. Costruzione `XpweDocumentModel` (modello intermedio deterministico)
4. Serializzazione XML con regole PriMus (`<Fgs>`, `<Versione>5.04</Versione>`, date `DD/MM/YYYY`)
5. Persistenza `XpweExportJob` (audit: path, timestamp, checksum)

**F5 — Export prezziario custom**
1. Utente ha creato nel computo EP nuovi ("Nuovi prezzi da PFTE" nel file sample)
2. Esporta questi come `.xpwe` TipoDocumento=0 (solo EPItem + ChapterNode, nessun VCItem)
3. File riusabile in futuri progetti come prezziario sorgente

**F6 — Integrazione Revit (cuore del plugin)**
1. Utente sta in Selezione → filtra elementi Revit per categoria
2. Da Listino sceglie voce EP → clicca "Applica"
3. Il plugin crea 1 `MeasurementRow` (VCItem) con `IDEP = priceItem.Id`
4. Per ogni elemento Revit selezionato crea 1 `MeasurementSubRow` (RGItem) con `IDVV = elementId`, `PartiUguali = 1`, `Lunghezza/Larghezza/HPeso = valori letti da Revit in base al QuantityMode` (Area → H=1, L=width, La=height, ecc.)
5. `MeasurementRow.Quantita` si ricalcola automatico come `SUM(SubRows.Quantita)`
6. Eventuale voce manuale (`IDVV < 0`) può essere aggiunta dall'utente in un editor RGItem

## 5. Schema DB

SQLite (coerente con lo stack attuale). Uso INTEGER PK (non UUID) per coerenza con il resto del plugin.

```sql
-- Documenti
CREATE TABLE ComputoDocuments (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  WorkSessionId INTEGER NOT NULL REFERENCES WorkSessions(Id) ON DELETE CASCADE,
  TipoDocumento INTEGER NOT NULL,              -- 0=Prezziario, 1=Computo
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
  UpdatedAt TEXT NOT NULL,
  UNIQUE(WorkSessionId)
);

-- Capitoli (3 livelli fissi)
CREATE TABLE ChapterNodes (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  DocumentId INTEGER NOT NULL REFERENCES ComputoDocuments(Id) ON DELETE CASCADE,
  Level TEXT NOT NULL CHECK(Level IN ('SpCap','Cap','SbCap')),
  Codice TEXT NOT NULL,
  DesSintetica TEXT NOT NULL,
  DesEstesa TEXT,
  DataInit TEXT,                               -- DD/MM/YYYY o NULL
  Durata INTEGER DEFAULT 0,
  CodFase TEXT,
  Percentuale REAL DEFAULT 0,
  ParentId INTEGER REFERENCES ChapterNodes(Id) ON DELETE CASCADE,
  SortOrder INTEGER NOT NULL,
  IsActive INTEGER NOT NULL DEFAULT 1
);
CREATE INDEX ix_chapternodes_doc ON ChapterNodes(DocumentId);
CREATE INDEX ix_chapternodes_parent ON ChapterNodes(ParentId);

-- Categorie (3 livelli fissi)
CREATE TABLE CategoryNodes (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  DocumentId INTEGER NOT NULL REFERENCES ComputoDocuments(Id) ON DELETE CASCADE,
  Level TEXT NOT NULL CHECK(Level IN ('SpCat','Cat','SbCat')),
  Codice TEXT NOT NULL,
  DesSintetica TEXT NOT NULL,
  DesEstesa TEXT,
  DataInit TEXT,
  Durata INTEGER DEFAULT 0,
  CodFase TEXT,
  Percentuale REAL DEFAULT 0,
  ParentId INTEGER REFERENCES CategoryNodes(Id) ON DELETE CASCADE,
  SortOrder INTEGER NOT NULL,
  IsActive INTEGER NOT NULL DEFAULT 1
);

-- WBS a profondità libera, 2 "dimensioni" (WbsCap per EP, WbsComputo per VC)
CREATE TABLE WbsNodes (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  DocumentId INTEGER NOT NULL REFERENCES ComputoDocuments(Id) ON DELETE CASCADE,
  Kind TEXT NOT NULL CHECK(Kind IN ('WbsCap','WbsComputo')),
  Codice TEXT NOT NULL,                        -- path completo "1.2.3"
  DesSintetica TEXT NOT NULL,
  ParentId INTEGER REFERENCES WbsNodes(Id) ON DELETE CASCADE,
  Level INTEGER NOT NULL,                      -- calcolato, 1-based
  SortOrder INTEGER NOT NULL,
  IsActive INTEGER NOT NULL DEFAULT 1
);

-- Voci Elenco Prezzi
CREATE TABLE PriceItems (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  DocumentId INTEGER NOT NULL REFERENCES ComputoDocuments(Id) ON DELETE CASCADE,
  Tariffa TEXT,
  Articolo TEXT,
  DesRidotta TEXT NOT NULL,
  DesEstesa TEXT,
  DesBreve TEXT,
  UnMisura TEXT NOT NULL,
  Prezzo1 REAL NOT NULL DEFAULT 0,
  Prezzo2 REAL NOT NULL DEFAULT 0,
  Prezzo3 REAL NOT NULL DEFAULT 0,
  Prezzo4 REAL NOT NULL DEFAULT 0,
  Prezzo5 REAL NOT NULL DEFAULT 0,
  CnfQt TEXT,
  SpCapId INTEGER REFERENCES ChapterNodes(Id),
  CapId INTEGER REFERENCES ChapterNodes(Id),
  SbCapId INTEGER REFERENCES ChapterNodes(Id),
  WbsCapNodeId INTEGER REFERENCES WbsNodes(Id),
  Data TEXT,                                   -- DD/MM/YYYY
  IncMDO REAL DEFAULT 0,
  IncMAT REAL DEFAULT 0,
  IncSIC REAL DEFAULT 0,
  TipoRisorsa INTEGER DEFAULT 0,
  Flags INTEGER DEFAULT 512,
  AdrInternet TEXT,
  SortOrder INTEGER NOT NULL,
  IsActive INTEGER NOT NULL DEFAULT 1
);

-- Voci Computo (VCItem)
CREATE TABLE MeasurementRows (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  DocumentId INTEGER NOT NULL REFERENCES ComputoDocuments(Id) ON DELETE CASCADE,
  PriceItemId INTEGER NOT NULL REFERENCES PriceItems(Id) ON DELETE RESTRICT,
  Quantita REAL NOT NULL DEFAULT 0,            -- cache di SUM(SubRows.Quantita)
  DataMis TEXT,                                -- DD/MM/YYYY
  Flags INTEGER DEFAULT 0,
  SpCatId INTEGER REFERENCES CategoryNodes(Id),
  CatId INTEGER REFERENCES CategoryNodes(Id),
  SbCatId INTEGER REFERENCES CategoryNodes(Id),
  WbsComputoNodeId INTEGER REFERENCES WbsNodes(Id),
  SortOrder INTEGER NOT NULL
);
CREATE INDEX ix_measurementrows_doc ON MeasurementRows(DocumentId);
CREATE INDEX ix_measurementrows_pi ON MeasurementRows(PriceItemId);

-- Righe di misura (RGItem)
CREATE TABLE MeasurementSubRows (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  MeasurementRowId INTEGER NOT NULL REFERENCES MeasurementRows(Id) ON DELETE CASCADE,
  IDVV INTEGER NOT NULL,                       -- >0 = Revit elementId, <0 = manuale
  Descrizione TEXT,
  PartiUguali REAL NOT NULL DEFAULT 1,
  Lunghezza REAL,                              -- NULL = fattore 1
  Larghezza REAL,
  HPeso REAL,
  Quantita REAL NOT NULL,                      -- = PartiUguali × (Lunghezza ?? 1) × (Larghezza ?? 1) × (HPeso ?? 1)
  Flags INTEGER DEFAULT 0,
  SortOrder INTEGER NOT NULL
);
CREATE INDEX ix_subrows_row ON MeasurementSubRows(MeasurementRowId);
CREATE INDEX ix_subrows_idvv ON MeasurementSubRows(IDVV);   -- per lookup "quale computo tagga questo Revit element"

-- Job di export XPWE (audit)
CREATE TABLE XpweExportJobs (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  DocumentId INTEGER NOT NULL REFERENCES ComputoDocuments(Id),
  ExportedAt TEXT NOT NULL,
  TipoDocumento INTEGER NOT NULL,
  XpweVersion TEXT NOT NULL,
  FilePath TEXT,
  FileChecksum TEXT,
  ValidationReport TEXT                        -- JSON con errors/warnings
);
```

**Migrazione schema** (versione DB attuale → +11 o successive):

- `V12__add_computo_document.sql` crea ComputoDocument
- `V13__add_chapter_category_wbs_nodes.sql`
- `V14__extend_priceitems.sql` aggiunge Prezzo2..5, IDSpCap/Cap/SbCap, CodiceWBSCAP, IncMDO/MAT/SIC, Flags, CnfQt, AdrInternet; `UnitPrice` esistente si propaga a `Prezzo1`
- `V15__add_measurement_rows_subrows.sql`
- `V16__migrate_qto_assignments_to_measurement_rows.sql` porta i QtoAssignment esistenti dentro MeasurementRow+SubRow

## 6. Architettura software

```
QtoRevitPlugin.Core/
├─ Models/Computi/          (entità pure C#)
│  ├─ ComputoDocument.cs
│  ├─ ChapterNode.cs
│  ├─ CategoryNode.cs
│  ├─ WbsNode.cs
│  ├─ PriceItem.cs          (estensione dell'esistente)
│  ├─ MeasurementRow.cs
│  └─ MeasurementSubRow.cs
│
├─ Xpwe/                    (modello intermedio + serializer)
│  ├─ XpweDocumentModel.cs  (snapshot deterministico pronto per export)
│  ├─ XpweSerializer.cs     (scrive XML secondo schema PriMus)
│  ├─ XpweDeserializer.cs   (legge XML, ricostruisce entità dominio)
│  └─ XpweValidator.cs      (verifiche pre-export/post-import)
│
└─ Services/
   ├─ ComputoDocumentService.cs  (CRUD documento)
   ├─ ChapterService.cs
   ├─ CategoryService.cs
   ├─ WbsService.cs
   ├─ PriceItemService.cs
   ├─ MeasurementService.cs     (crea VCItem/RGItem dal flusso Revit)
   └─ XpweExportService.cs / XpweImportService.cs
```

Regole di separazione:

- Il **Domain Layer** (Core/Models/Computi) non conosce Revit API, non conosce SQLite, non conosce XPWE
- Il **Persistence Layer** (SQLite, Dapper) è dentro `QtoRevitPlugin/Data`
- Il **Exchange Layer** (XPWE) passa **sempre** da `XpweDocumentModel` — mai seriliazza direttamente dalle entità di dominio
- Il **Presentation Layer** (WPF ViewModels) chiama i service, mai direttamente i repository

## 7. UI — integrazione con refactor Tagging

Il refactor Tagging (spec del 24/04) definiva 4 tab di Selezione + una scheda "Redazione CME" (Plan 5).

**Con il Modulo Computi la scheda "Redazione CME" diventa più ricca** — è la UI del Modulo Computi vera e propria:

### 7.1 Tab "Redazione CME" (sostituisce Tagging)

Layout a 3 colonne:

```
┌────────────────────┬──────────────────────┬─────────────────────┐
│ Navigatore         │ Riepilogo            │ Quadro Economico    │
│ (TreeView)         │ (DataGrid)           │ (Summary)           │
│                    │                      │                     │
│ ◯ Per Capitoli     │ [Filtro tree-based]  │ Netto: € 1.234.567  │
│   └ SpCap 01       │                      │ IVA:   € 271.605    │
│     └ Cap 01.01    │ Codice Voce | Qta    │ Lordo: € 1.506.172  │
│       └ SbCap ...  │ ...                  │                     │
│                    │                      │ Per SpCap:          │
│ ◯ Per Categorie    │                      │ 01 xxx  € 450k  32% │
│   └ SpCat 01       │                      │ 02 yyy  € 320k  23% │
│                    │                      │                     │
│ ◯ Per WBS          │                      │ Per SpCat:          │
│                    │                      │ ...                 │
│ ◯ Lineare          │                      │                     │
└────────────────────┴──────────────────────┴─────────────────────┘
```

- **Navigatore**: 4 modalità di vista (per Capitoli / per Categorie / per WBS / lineare). L'utente sceglie con radio.
- **Riepilogo**: tabella delle MeasurementRows (VCItem) filtrate dal nodo selezionato nel navigatore. Editing inline delle quantità (sblocco override).
- **Quadro Economico**: totali rollup + percentuali di incidenza. Aggiornamento live.

### 7.2 Setup

Nuovi sotto-tab dentro Setup (accanto a "Informazioni", "Listino", "Struttura Computo", "Nuovi Prezzi"):

- **Struttura Computo** (rinomina + potenziamento del tab esistente): ora gestisce **3 alberi** (Capitoli / Categorie / WBS) con drag&drop, rinumerazione massiva
- **Dati Generali**: Comune, Provincia, Oggetto, Committente, Impresa, ParteOpera (oggi sono in `ProjectInfo`, restano lì ma vengono anche copiati su `ComputoDocument` all'export)

### 7.3 Dialog import/export XPWE

- Menu File → **Importa prezziario XPWE…** → dialog con preview (elenco EPItem con checkbox) → import nel documento attivo
- Menu File → **Esporta XPWE…** → dialog con 2 radio (TipoDocumento 0 o 1) + validazione live + pulsante Esporta

## 8. Requisiti funzionali (consolidati)

### RF1 — Gestione Capitoli
- Definire SpCap/Cap/SbCap con codice, titolo, descrizione estesa, data inizio, percentuale
- Drag&drop riordino
- Rinomina e rinumera in cascata (shift dei codici figli quando si inserisce un capitolo intermedio)
- Assegnare capitoli alle voci EP (drag dalle voci al nodo o via menu contestuale)
- Filtrare viste per capitolo

### RF2 — Gestione Categorie
- Definire SpCat/Cat/SbCat (stessa semantica dei Capitoli ma su VCItem)
- Assegnazione massiva a più VCItem selezionati
- Raggruppamento dinamico del computo per categoria

### RF3 — Gestione WBS
- 2 WBS distinti: WbsCap (sul prezziario, visibile in EPItem.CodiceWBSCAP) e WbsComputo (sul computo, VCItem.CodiceWBS)
- Profondità libera
- Path codice automatico ("1.2.3.4") derivato dalla gerarchia
- Esportato come stringa path mantenendo parent-child

### RF4 — Gestione Elenco Prezzi
- CRUD voci (creazione, modifica, duplica, disattiva)
- 5 tariffe (Prezzo1..5) con etichette configurabili (default: "Lordo", "Netto", "Manodopera", "Riserva1", "Riserva2")
- Associa capitoli 0-3 livelli coerenti
- Associa WbsCap opzionale
- Filtro per capitolo/codice/descrizione
- Import da XPWE prezziario (TipoDocumento=0)

### RF5 — Gestione Computo
- Crea MeasurementRow richiamando una PriceItem
- Crea MeasurementSubRow con `IDVV = elementId` dal flusso Revit (auto-compilazione `PartiUguali/Lunghezza/Larghezza/HPeso` in base al QuantityMode)
- Crea MeasurementSubRow manuale con `IDVV < 0` (editor inline)
- Quantita del MeasurementRow si ricalcola automaticamente
- Assegna categorie 0-3 livelli
- Assegna WbsComputo opzionale
- Duplica, sposta, riordina, elimina righe

### RF6 — Template
- Salva documento come template (.xpwe TipoDocumento=0 o 1)
- Crea nuovo documento duplicando template

### RF7 — Export XPWE
- Export TipoDocumento=0 (solo prezziario custom esportabile) — vedi F5
- Export TipoDocumento=1 (computo completo) — vedi F4
- Validazione pre-export bloccante: riferimenti validi, quantità non null, sort order deterministici
- Audit: XpweExportJob persistito

### RF8 — Import XPWE
- Import TipoDocumento=0 → popola PriceItems + ChapterNodes nel documento attivo
- Import TipoDocumento=1 → clona come nuovo documento (template F2) o merge selettivo nel documento attivo
- Validazione post-import: nessun dato perso, warning su campi non mappabili

### RF9 — Integrazione Revit
- Dalla Selezione (tab 0 Elementi): cliccando "Applica EP corrente" il plugin crea MeasurementRow+SubRow come spec in 4.3 F6
- Dal Listino: cliccando "Applica a selezione corrente" → stesso effetto
- Dal menu contestuale Revit: cliccando su un elemento, se ha già MeasurementSubRow con quell'elementId, mostra il VCItem di appartenenza e la quantità calcolata

## 9. Requisiti non funzionali

### RNF1 — Integrità dati
- FK su tutti i riferimenti, protette a livello DB
- Cascade delete coerenti (rimuovere ChapterNode non deve orfanare PriceItem — richiede pre-check con dialog "sposta a / elimina")
- Constraint CHECK su Level (solo 'SpCap'/'Cap'/'SbCap' su ChapterNodes)

### RNF2 — Stabilità export
- Export deterministico: stesso input → stesso XML byte-per-byte
- Ordinamento per SortOrder (non ID), tie-break deterministico (Codice, poi Id)

### RNF3 — Performance
- Un documento tipico (dal sample): 119 EP, 168 VC, 4491 RG. DB SQLite gestisce senza problemi
- Rollup live del Quadro Economico: query aggregate SQL con GROUP BY
- Cache `MeasurementRow.Quantita` invece di ricalcolare ogni volta

### RNF4 — Auditabilità
- Ogni export tracciato in `XpweExportJobs`
- Modifiche strutturali (rimozione nodo, rinumerazione) loggate su `ChangeLogEntry` esistente

## 10. Piano di decomposizione — 7 sotto-progetti

Ogni sotto-progetto produce software funzionante e ha il suo plan.

| # | Nome | Dipende da | Output tangibile |
|---|------|------------|------------------|
| **C-0** | Schema DB PriMus | - | Migrazioni V12-V16, tabelle create, zero UI |
| **C-1** | XpweDeserializer + test | C-0 | Import file .xpwe in ComputoDocument. Comando nascosto in Setup per prova |
| **C-2** | Domain services (Chapter/Category/Wbs/PriceItem) | C-0 | CRUD programmatico, unit test, no UI |
| **C-3** | UI Setup → Strutture (3 alberi) | C-2 | Setup tab "Struttura Computo" potenziato con 3 tab interni |
| **C-4** | UI Elenco Prezzi potenziato (5 tariffe, capitoli, WbsCap) | C-2, C-3 | Setup tab Listino con editor completo |
| **C-5** | UI Redazione CME (3 colonne: navigatore / tabella / quadro economico) | C-2 | Nuova scheda "Redazione CME" nel menu |
| **C-6** | MeasurementService + integrazione Revit | C-5, refactor Tagging Plan 4 | "Applica EP corrente" crea MeasurementRow+SubRow con dati Revit |
| **C-7** | XpweSerializer + UI export | C-6 | Menu File → Esporta XPWE con dialog |

**Ordine di esecuzione consigliato**: C-0 → C-1 → C-2 → (C-3 // C-4 in parallelo) → C-5 → C-6 → C-7

Tempistica stimata (solo sviluppo, no test interoperabilità PriMus):
- C-0: 2 ore
- C-1: 6-8 ore (parsing XPWE con edge cases)
- C-2: 4-5 ore
- C-3: 6-8 ore
- C-4: 4-6 ore
- C-5: 10-15 ore (UI a 3 colonne è complessa)
- C-6: 6-8 ore
- C-7: 8-10 ore (validation + deterministic serialization)

**Totale: 46-62 ore di sviluppo**, da spalmare su 3-5 settimane.

## 11. Relazione con il refactor Tagging

Questo spec SUPERSEDE il **Plan 5 - CmeEditorView** del refactor Tagging. Il plan 5 originale prevedeva "un albero capitoli→voci→quantità live". Ora diventa il **sotto-progetto C-5** di questo spec, più ricco.

I Plan 1-4 del refactor Tagging **restano validi e indipendenti**:
- Plan 1 — Fix contrasto PickEpDialog
- Plan 2 — SessionManager eventi + ActiveEp
- Plan 3 — Selezione multi-tab (migrazione MappingView)
- Plan 4 — Bottoni simmetrici + RevitSelectionWatcher

**Ordine di esecuzione globale** consigliato:
1. Plan 1 Tagging (fix contrasto) — standalone, 10 min
2. Plan 2 Tagging (SessionManager) — infrastruttura
3. C-0 (schema DB) — infrastruttura parallela
4. Plan 3 Tagging (Selezione multi-tab) — UI
5. C-1, C-2 (import XPWE + domain services) — può partire in parallelo a Plan 3
6. Plan 4 Tagging (bottoni simmetrici) — prerequisito per C-6
7. C-3, C-4 (UI Strutture + Listino) — UI
8. C-5 (Redazione CME) — sostituisce il Plan 5
9. C-6 (MeasurementService Revit integration) — cuore funzionale
10. C-7 (Export XPWE) — chiude il cerchio

## 12. Scope NON incluso

- Localizzazione multi-lingua (tutto in IT, target PA italiana)
- Revisioni/versioning del documento (solo Last-Write-Wins, no branching)
- Multi-utente concorrente (singolo progettista alla volta)
- Analisi prezzi dettagliata (scomposizione in risorse MDO/MAT/ATT) — si rappresentano solo le incidenze aggregate (IncMDO/IncMAT/IncSIC) come nel file sample
- Prezziari DEI e regionali **come catalogo esterno** (l'import XPWE copre il caso d'uso)
- Reportistica PDF customizzabile (uso il modulo PDF esistente, adattandolo al nuovo modello)

## 13. Rischi identificati

- **R1 — XPWE reverse-engineered**: lo schema non è documentato pubblicamente, ho estratto dal file. Campi opzionali potrebbero comparire in file PriMus più complessi. Mitigazione: `XpweDeserializer` tollerante (campi unknown → attributi extra sul modello intermedio, preservati nel round-trip).
- **R2 — Migrazione dati esistenti**: il `V16__migrate_qto_assignments_to_measurement_rows.sql` deve mappare tutti gli assignments attuali senza perdere dati. Mitigazione: prima testare su DB di sviluppo, backup automatico pre-migrazione.
- **R3 — Performance UI con 4491 RGItem**: il sample ha 4491 righe di misura. La DataGrid WPF rischia lag con tutte visibili. Mitigazione: virtualizzazione (`VirtualizingStackPanel`) + paging nel navigatore.
- **R4 — Interoperabilità reale con PriMus**: l'unica verifica definitiva è aprire il file esportato in PriMus vero. Non abbiamo PriMus installato. Mitigazione: chiedere all'utente di testare file esportati + confronto byte-per-byte col file sample rigenerato.

## 14. Decisioni bloccate

- ✓ Q1 compliance XPWE = **hard requirement**
- ✓ Q2 WBS multi-livello = **serve**
- ✓ Q3 Revit element come sorgente MeasurementRow = **essenziale**
- ✓ Qa `measurement_row` = VCItem, con N RGItem figli = **confermato**
- ✓ Qc CategoryNodes runtime-defined per progetto = **confermato** (no standard SOA)
- ✓ Workflow: **entrambi** (da zero + da template)
- ✓ Export: **entrambi i TipoDocumento** (0 e 1)
- ✓ RGItem = **tabella separata** `MeasurementSubRows` (scelta del team tecnico)
- Eventi cross-scheda via `SessionManager` (da spec Tagging refactor)

## 15. Prossimi passi immediati

1. **Review spec da parte dell'utente** — correzione eventuali fraintendimenti
2. **Commit spec** dopo approvazione
3. **Scrittura Plan C-0** (schema DB) come primo lavoro di implementazione, dopo che:
   - Plan 1 Tagging è eseguito (fix contrasto, 10 min)
   - Bug "0 elementi" Selezione è risolto (debug diagnostic in corso)
