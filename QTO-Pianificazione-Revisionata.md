# QTO Revit Plugin – Pianificazione Revisionata
### Data revisione: 22 aprile 2026
### Stato: **Baseline approvata**
### Riferimenti: `QTO-Plugin-Documentazione-v3.md` · `QTO-Implementazioni-v3.md` · `QTO-AI-Integration.md` · `2026-04-21-ai-integrations-design.md`

---

## 1. Executive Summary delle variazioni

Questo documento consolida i quattro documenti di specifica esistenti in un piano unico. Le variazioni rispetto al piano v3.0 (20 settimane):

| Variazione | Da | A |
|---|---|---|
| AI integration | MCP server embedded | **Ollama locale** (IQtoAiProvider / NullAiProvider) |
| Sorgente B | Non prevista | **Room/Space con formula NCalc** (zoccolini, tinteggiature, pavimenti) |
| Sorgente C | Non prevista | **Voci manuali** (ponteggi, smaltimento, opere provvisionali) |
| Export primario | xlsx + tsv | **XPWE** (PriMus-ready, gerarchia preservata) + xlsx + tsv |
| Filtri Revit | Non previsti | **FilterManager** (filtri vista nativi: Taggati / Mancanti / Anomalie) |
| Batch tagging | Non previsto | **"Assegna a tutta la famiglia"** in TaggingView |
| Stima totale | 20 settimane / 10 sprint | **24 settimane / 11 sprint** |

---

## 2. Mappa completa delle feature

### 2.1 Tre sorgenti di quantità

| Sorgente | Tipo | Meccanismo | Sprint |
|---|---|---|---|
| **A – Famiglie Revit** | Elementi modellati | `FilteredElementCollector` + `ElementPhaseStatusFilter` + parametri geometrici (Area, Volume, Lunghezza, Conteggio) | 2 |
| **B – Room/Space** | Locali architettonici | `FilteredElementCollector` `OST_Rooms` (Area > 0) + formula NCalc con variabili PERIMETER, AREA, HEIGHT, DOOR_WIDTH_SUM, DOOR_AREA_SUM, WINDOW_AREA_SUM | 2 |
| **C – Voci manuali** | Lavorazioni non modellabili | Input UI diretto — flag `"sorgente: manuale"` nel DB e nell'export | 2 |

> **Vincolo critico Sorgente B**: `Room.Height` non è un `BuiltInParameter` in Revit. Il parametro `QTO_AltezzaLocale` (configurabile nel SetupView) è la fonte primaria; fallback a 2.70m.

### 2.2 Feature complete per area

**Persistenza e sessioni**
- SQLite database completo — un file per `.rvt` in `%AppData%\QtoPlugin\db\`
- SessionManager con AutoSave (flush immediato ad ogni INSERISCI + timer 5 min), recovery, fork
- Extensible Storage per configurazioni per-progetto e persistenza multi-EP per elemento
- Shared Parameters automatici: `QTO_Codice`, `QTO_DescrizioneBreve`, `QTO_Stato`

**Listino e ricerca**
- Multi-listino simultaneo con colonna `Priority` per gestione conflitti codice
- Parser DCF / Excel / CSV con importazione asincrona (500 voci/batch, progress bar)
- FTS5 full-text search < 10ms su 30.000+ voci
- Ricerca a 3 livelli: codice esatto → FTS5 → fuzzy (Levenshtein, solo se FTS5 < 3 risultati)
- Navigazione per capitoli/sottocapitoli (albero laterale)
- Badge colorato per listino di provenienza

**Workflow Revit**
- `PhaseFilterView` obbligatorio (Nuova costruzione / Demolizioni / Esistente) — step 0
- `SelectionView` con FilterBuilder stile Revit + ricerca inline + preset filtri (JSON locale + ES)
- Esclusione elementi: manuale (checkbox) + regole globali (SetupView)
- `MappingView` con 3 tab: Famiglie | Locali | Voci manuali
- `TaggingView` con multi-EP, pannello assegnazioni esistenti, batch "Assegna a tutta la famiglia"
- `MeasurementRulesEngine`: vuoto per pieno, deduzioni aperture, soglie configurabili per prezzario
- `ModelDiffService`: rilevamento nuovi/rimossi elementi tra sessioni, dialog notifica, delta isolamento
- `DockablePane` live preview: 2 tab (Selezione Corrente / Riepilogo Complessivo), status bar
- `FilterManager`: filtri vista nativi Revit — `QTO_Taggati` (verde) / `QTO_Mancanti` (rosso) / `QTO_Anomalie` (arancio) / per codice specifico

**Nuovi Prezzi**
- `NpEngine` con formula `CT = Manodopera + Materiali + Noli + Trasporti` → `NP = CT × (1 + SG%) × (1 + Utile%)`
- Campo `RibassoAsta` opzionale (Parere MIT 3545/2025)
- Workflow: Bozza → Concordato (contraddittorio DL/Impresa) → Approvato (RUP)
- NP inseriti nel DB come `PriceItems` con `IsNP = 1` (ricercabili come qualsiasi voce)

**Validazione**
- `HealthCheckEngine`: 6 stati (Computato / Parziale / Non computato / Multi-EP / Escluso manuale / Escluso filtro)
- Room "Not Enclosed" detection (Area = 0) segnalata come "Locali non bounded"
- `AnomalyDetector` z-score — funziona **sempre**, senza AI (z > 2.5 = anomalia, z > 3.5 = alta gravità)
- Mismatch semantico categoria/EP — layer aggiuntivo AI (solo se Ollama disponibile)

**Export**
- **XPWE** (priorità): SuperCapitolo → Capitolo → Voce con gerarchia preservata dal caricamento listino; `<Misure>/<ElementId>` per audit trail; import diretto in PriMus
- **Excel (.xlsx)**: NP evidenziati con colore distinto, foglio separato "Analisi Nuovi Prezzi"
- **TSV**: compatibilità PriMus / importazione SA
- **Delta report**: solo elementi aggiunti/modificati dall'ultima export

**AI (opzionale — graceful degradation)**
- `IQtoAiProvider` come unico punto di accesso; `NullAiProvider` quando Ollama non disponibile
- Smart Mapping: embedding cosine similarity da `nomic-embed-text` o `mxbai-embed-large`
- Ricerca semantica: fallback automatico se FTS5 restituisce < 5 risultati
- Generazione `ShortDesc` EP al caricamento listino (una tantum, risultati nel DB)
- `EmbeddingCache` SQLite: vettori calcolati una sola volta per listino, rigenerati solo al cambio versione
- Pannello "Suggerimenti AI" nella TaggingView (3 voci con score, soggette a conferma)
- Modelli consigliati: `nomic-embed-text` (~274 MB) + `llama3.2:3b` (~2 GB)

---

## 3. Architettura aggiornata

```
┌──────────────────────────────────────────────────────────────┐
│  UI Layer (WPF + MVVM)                                       │
│  SetupView · PhaseFilterView · MappingView [3 tab]           │
│  SelectionView · TaggingView · HealthCheckView               │
│  ExportView · NpView · SessionView                           │
│  DockablePane (Anteprima Live — sempre visibile)             │
├──────────────────────────────────────────────────────────────┤
│  Application Services Layer                                  │
│  QtoCommandOrchestrator · ExternalEventHandlers              │
│  AutoSaveService · SessionManager · ModelDiffService         │
├──────────────────────────────────────────────────────────────┤
│  Core Engine (C# / Revit API)                                │
│  PriceListParser · CategoryMapper · FormulaEngine (NCalc)    │
│  QuantityExtractor (Sorgente A)                              │
│  RoomExtractor (Sorgente B)           ← NEW                  │
│  MeasurementRulesEngine · ExclusionRulesEngine               │
│  HealthCheckEngine · AnomalyDetector (z-score)               │
│  NpEngine                                                    │
│  ExportEngine (xlsx + tsv) · XpweExporter ← NEW             │
│  FilterManager                        ← NEW                  │
├──────────────────────────────────────────────────────────────┤
│  AI Layer (opzionale)                                        │
│  IQtoAiProvider ◄─ OllamaAiProvider / NullAiProvider        │
│  OllamaEmbeddingProvider · OllamaTextModelProvider           │
│  EmbeddingCacheService · SmartMappingService                 │
│  QtoAiFactory (DI)                                           │
├──────────────────────────────────────────────────────────────┤
│  Data Layer                                                  │
│  QtoRepository (SQLite) · ExtensibleStorageRepo              │
│  SharedParameterManager · FileRepository                     │
└──────────────────────────────────────────────────────────────┘
```

---

## 4. Struttura progetto Visual Studio — file aggiornati

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
│   │   ├── DcfParser.cs              ← XML .dcf / .xpwe ACCA PriMus (gerarchia)
│   │   ├── ExcelParser.cs
│   │   └── CsvParser.cs
│   ├── Mapping/
│   │   ├── CategoryMapper.cs
│   │   └── FormulaEngine.cs          ← NCalc wrapper
│   ├── Extraction/
│   │   ├── QuantityExtractor.cs      ← Sorgente A: famiglie Revit
│   │   └── RoomExtractor.cs          ← Sorgente B: Room/Space + NCalc  [NEW]
│   ├── Rules/
│   │   ├── MeasurementRulesEngine.cs
│   │   └── ExclusionRulesEngine.cs
│   ├── Validation/
│   │   └── HealthCheckEngine.cs
│   ├── NuoviPrezzi/
│   │   └── NpEngine.cs
│   ├── Diff/
│   │   └── ModelDiffService.cs
│   ├── Export/
│   │   ├── ExportEngine.cs           ← xlsx + tsv + delta report
│   │   └── XpweExporter.cs           ← XPWE con gerarchia preservata  [NEW]
│   └── FilterManager.cs              ← filtri vista nativi Revit       [NEW]
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
│   ├── RoomMappingConfig.cs          ← config formula voce da Room    [NEW]
│   ├── ManualQuantityEntry.cs        ← voce a quantità manuale        [NEW]
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
│   ├── SmartMappingService.cs
│   ├── AnomalyDetector.cs            ← z-score, no AI                 [NEW]
│   ├── EmbeddingCacheService.cs      ← cache SQLite vettori           [NEW]
│   ├── QtoAiFactory.cs               ← DI factory                     [NEW]
│   └── AI/
│       ├── IQtoAiProvider.cs                                           [NEW]
│       ├── IEmbeddingProvider.cs                                       [NEW]
│       ├── ITextModelProvider.cs                                       [NEW]
│       ├── NullAiProvider.cs                                           [NEW]
│       ├── OllamaAiProvider.cs                                         [NEW]
│       ├── OllamaEmbeddingProvider.cs                                  [NEW]
│       └── OllamaTextModelProvider.cs                                  [NEW]
├── UI/
│   ├── Panes/
│   │   └── QtoPreviewPane.xaml(.cs)  ← DockablePane
│   ├── Views/
│   │   ├── SetupView.xaml(.cs)       ← listini, regole, esclusioni, altezza locali
│   │   ├── PhaseFilterView.xaml(.cs) ← step 0 obbligatorio
│   │   ├── MappingView.xaml(.cs)     ← 3 tab: Famiglie|Locali|Manuali [NEW]
│   │   │   ├── RoomMappingView.xaml(.cs)   ← tab "Locali"             [NEW]
│   │   │   └── ManualEntryView.xaml(.cs)   ← tab "Voci manuali"       [NEW]
│   │   ├── SelectionView.xaml(.cs)   ← FilterBuilder + preset
│   │   ├── TaggingView.xaml(.cs)     ← assegnazione + batch famiglia
│   │   ├── HealthCheckView.xaml(.cs)
│   │   ├── ExportView.xaml(.cs)
│   │   ├── NpView.xaml(.cs)
│   │   └── SessionView.xaml(.cs)
│   └── ViewModels/                   ← uno per ogni View
└── QtoRevitPlugin.addin
```

---

## 5. Workflow operativo completo

```
STEP 0 · Filtro Fase (PhaseFilterView) — OBBLIGATORIO ad ogni apertura sessione
  ☑ Nuova costruzione  → ElementOnPhaseStatus.New
  ☑ Demolizioni        → ElementOnPhaseStatus.Demolished
  ☐ Esistente          → escluso dal computo (solo visualizzazione)
  Mappatura automatica: fase Demolizioni → capitolo Demolizioni nel listino

STEP 1 · Setup (SetupView)
  01. Carica listino/i (.dcf / .xlsx / .csv) — gerarchia preservata per export XPWE
  02. Configura regole di misurazione (preset per prezzario + override per progetto)
  03. Configura regole esclusione globale
  04. Imposta "Altezza locali" (QTO_AltezzaLocale o valore fisso fallback 2.70m)

STEP 2 · Mapping (MappingView — 3 tab)
  [Tab A – Famiglie]     Family Type → Voce EP + parametro geometrico
  [Tab B – Locali]       Voce EP → formula NCalc (PERIMETER, AREA, HEIGHT, ...)
  [Tab C – Voci manuali] Voce EP → quantità inserita manualmente
  [AI] Se Ollama disponibile: dropdown pre-compilato "Suggerita dall'AI"

STEP 3 · Selezione (SelectionView)
  03. FilterBuilder stile Revit + ricerca inline
  04. Salva/carica preset filtri (JSON locale + ES nel .rvt)
  05. Esclusione elementi: manuale o per regola globale

STEP 4 · Tagging (TaggingView)
  05. Ricerca FTS5 multi-listino (< 10ms)
  06. Assegnazione singola + multi-EP
  07. Batch "Assegna a tutta la famiglia (N elem.)" — solo selezione omogenea
  08. Pannello "Assegnazioni Esistenti" con lista multi-EP per elemento
  [AI] Pannello "Suggerimenti AI" — 3 voci con score cosine, soggette a conferma

STEP 5 · Calcolo (deterministico, automatico)
  09. Sorgente A: quantità geometriche × MeasurementRulesEngine (deduzioni aperture)
  10. Sorgente B: formule Room con HEIGHT configurabile
  11. Sorgente C: quantità manuali passate direttamente
  12. Aggregazione per codice voce + formula NCalc prezzo opzionale

STEP 6 · Verifica (HealthCheckView)
  13. 6 stati computazione + Room "Not Enclosed"
  14. AnomalyDetector z-score (sempre attivo, senza AI)
  15. Mismatch semantico categoria/EP (solo se Ollama disponibile)
  Loop: errori → torna a step 3/4 → rilancia check

STEP 7 · Export (ExportView)
  16. XPWE — SuperCapitolo → Capitolo → Voce + Misure/ElementId (PriMus-ready)
  17. Excel (.xlsx) — NP evidenziati + foglio "Analisi Nuovi Prezzi"
  18. TSV — compatibilità SA
  19. Delta report — solo elementi modificati dall'ultima export

STEP 8 · Filtri Vista Revit (FilterManager, attivabile da ribbon)
  20. Crea filtri nativi Revit: QTO_Taggati (verde) / QTO_Mancanti (rosso) /
      QTO_Anomalie (arancio) / per codice specifico (input manuale)
```

DockablePane e ModelDiffService sono **trasversali** a tutto il flusso.

---

## 6. Sprint Plan — 11 sprint · 24 settimane

### Parametri
- **Team**: 1 sviluppatore senior C#/Revit API (80% del tempo)
- **Sprint**: 2 settimane (sprint 2 e 4 da 3 settimane per maggiore ampiezza)
- **Metodologia**: Scrum semplificato con sprint review

---

### Sprint 0 — Fondamenta (settimane 1–2)

| Task | Sforzo |
|---|---|
| Setup VS multi-target `net48;net8.0-windows` | 1 gg |
| Ribbon + `IExternalApplication` + addin manifest | 1 gg |
| Struttura cartelle, modelli dati base | 1 gg |
| Template MVVM, navigazione view tramite Frame WPF | 1 gg |
| Unit test project + CI/CD base (build multi-versione, artifact per versione Revit) | 2 gg |
| **Deliverable** | Addin caricabile in Revit con ribbon e finestra WPF vuota |

---

### Sprint 1 — Database e Sessioni (settimane 3–4)

| Task | Sforzo |
|---|---|
| Schema SQLite completo (tutte le tabelle incluse `EmbeddingCache`) | 3 gg |
| `SessionManager`: creazione, resume, fork sessione | 2 gg |
| `AutoSaveService`: flush immediato ad ogni INSERISCI + timer 5 min | 1 gg |
| Recovery automatico al riapertura documento | 1 gg |
| Dialog "Riprendi Sessione" con lista sessioni per il `.rvt` | 1 gg |
| **Deliverable** | Sessioni persistenti con AutoSave e recovery |

---

### Sprint 2 — Parsing, FTS5 e Sorgenti B+C (settimane 5–7) · *3 settimane*

| Task | Sforzo |
|---|---|
| `DcfParser`: XML con gerarchia SuperCapitolo/Capitolo/Sottocapitolo preservata | 2 gg |
| `ExcelParser` + `CsvParser` | 1 gg |
| Importazione asincrona: 500 voci/batch, FTS5 rebuild ogni 2000 | 1 gg |
| FTS5 full-text search + ricerca a 3 livelli (esatto → FTS5 → Levenshtein) | 2 gg |
| Multi-listino UI: priorità, badge listino, navigazione capitoli | 2 gg |
| **`RoomExtractor.cs`**: `OST_Rooms` + filtro `Area > 0` + variabili NCalc | 2 gg |
| Gestione `Room.Height`: param `QTO_AltezzaLocale` + fallback 2.70m | 1 gg |
| **`RoomMappingConfig.cs`** + **`ManualQuantityEntry.cs`** (modelli dati) | 1 gg |
| **Deliverable** | Listini con FTS5; estrazione Room e voci manuali pronte |

---

### Sprint 3 — Extensible Storage e Shared Parameters (settimane 8–9)

| Task | Sforzo |
|---|---|
| `ExtensibleStorageRepo`: schema, read/write, versioning GUID | 2 gg |
| Schema multi-EP per elemento (`IList<string>` JSON) | 1 gg |
| `SharedParameterManager`: creazione automatica file `.txt` + binding multi-categoria | 2 gg |
| Supporto `ForgeTypeId` vs `DisplayUnitType` (Revit 2022–2026) | 1 gg |
| Test round-trip salva/ricarica in `.rvt` workshared | 2 gg |
| **Deliverable** | Configurazione persistente nel `.rvt`, parametri condivisi pronti |

---

### Sprint 4 — Fase, Selezione e MappingView (settimane 10–12) · *3 settimane*

| Task | Sforzo |
|---|---|
| `PhaseFilterView`: lettura fasi dinamiche, `ElementPhaseStatusFilter` | 2 gg |
| Mappatura Fase → comportamento listino (cap. Demolizioni auto-aperto) | 1 gg |
| `SelectionView`: `FilterBuilder` con `ParameterFilterUtilities`, ricerca inline | 3 gg |
| Comandi selezione: Isola, Nascondi, Togli Isolamento, **INSERISCI** | 1 gg |
| Esclusione elementi: manuale (checkbox) + regole globali (SetupView) | 1 gg |
| Preset filtri: JSON locale (`%AppData%\QtoPlugin\rules\`) + DataStorage ES | 1 gg |
| **`MappingView`** con 3 tab: Famiglie / Locali / Voci manuali | 3 gg |
| Configurazione "Altezza locali" nel SetupView | 0.5 gg |
| **Deliverable** | Filtro fase, selezione avanzata, mapping completo per tutte e 3 le sorgenti |

---

### Sprint 5 — TaggingView e Batch (settimane 13–14)

| Task | Sforzo |
|---|---|
| `TaggingView`: ricerca FTS5 multi-listino integrata | 1 gg |
| Pannello "Assegnazioni Esistenti" con lista multi-EP | 1 gg |
| Scrittura bidirezionale (`QTO_Codice`, `QTO_DescrizioneBreve`, `QTO_Stato`) in transazione unica | 1 gg |
| **Pulsante "Assegna a tutta la famiglia (N elem.)"**: verifica selezione omogenea → dialog anteprima → ExternalEvent | 2 gg |
| Color coding 6 colori: verde / rosso / arancione / giallo / blu / grigio halftone | 2 gg |
| `NavigateToElementHandler`: selezione + zoom sull'elemento | 1 gg |
| **Deliverable** | Tagging manuale e batch completo, multi-EP, color coding |

---

### Sprint 6 — Regole di Misurazione e Health Check (settimane 15–16)

| Task | Sforzo |
|---|---|
| `MeasurementRulesEngine`: vuoto per pieno, deduzioni aperture, 10 categorie default | 3 gg |
| Preset per prezzario regionale + override per progetto (salvato in ES) | 1 gg |
| `ExclusionRulesEngine`: pre-filtro su regole globali | 1 gg |
| `HealthCheckView`: 6 stati con icone, doppio click → zoom elemento | 2 gg |
| Nota esplicativa nella preview: `"45,2 m² lordi − 3,1 m² (2 aperture) = 42,1 m² netti"` | 1 gg |
| **Deliverable** | Regole di misurazione operative; Health Check con navigazione |

---

### Sprint 7 — ModelDiff e DockablePane (settimane 17–18)

| Task | Sforzo |
|---|---|
| `ModelDiffService`: confronto UniqueId DB vs `FilteredElementCollector` | 2 gg |
| Dialog notifica modifiche: liste aggiunti/rimossi con importo annullato | 1 gg |
| "Mostra nuovi elementi": isola in vista 3D + carica `SelectionView` pre-filtrata | 1 gg |
| `ModelDiffLog.Resolved = 1` al tagging o esclusione esplicita | 1 gg |
| `DockablePane`: registrazione + 2 tab (Selezione Corrente / Riepilogo Complessivo) | 2 gg |
| Status bar: `[💾 17:32] | Sessione | 142/318 (44,7%) | € 48.320 | [● Sync ✓]` | 1 gg |
| **Deliverable** | Rilevamento modifiche tra sessioni, preview live sempre visibile |

---

### Sprint 8 — Nuovi Prezzi (settimane 19–20)

| Task | Sforzo |
|---|---|
| `NpEngine`: formula CT + SG + utile + ribasso asta opzionale | 2 gg |
| `NpView`: form analisi prezzi componenti, cambio stato | 2 gg |
| Workflow Bozza → Concordato → Approvato con log | 1 gg |
| Inserimento NP approvati nel DB come `PriceItems` con `IsNP = 1` | 1 gg |
| Export NP: voci evidenziate in Excel + foglio "Analisi Nuovi Prezzi" | 1 gg |
| **Deliverable** | NP completo con workflow, analisi prezzi, export dedicato |

---

### Sprint 9 — Export Completo e FilterManager (settimane 21–22)

| Task | Sforzo |
|---|---|
| Analisi empirica file XPWE da PriMus (su prezzario reale) — **prima** di implementare | 1 gg |
| **`XpweExporter.cs`**: SuperCapitolo → Capitolo → Voce + `<Misure>/<ElementId>` | 3 gg |
| `ExportEngine` Excel completo: NP evidenziati, delta report | 1.5 gg |
| Export TSV compatibile SA | 0.5 gg |
| **`FilterManager.cs`**: crea filtri vista nativi Revit (Taggati / Mancanti / Anomalie / per codice) | 2 gg |
| `SessionView`: save con nome, fork, chiudi/riapri, comando Esporta | 1 gg |
| **Deliverable** | Export XPWE PriMus-ready con gerarchia; filtri vista nativi Revit; sessioni complete |

---

### Sprint 10 — AI Ollama e Installer (settimane 23–24)

| Task | Sforzo |
|---|---|
| Interfacce: `IQtoAiProvider`, `IEmbeddingProvider`, `ITextModelProvider` | 0.5 gg |
| `NullAiProvider` (prima implementazione da creare, prima di Ollama) | 0.5 gg |
| Schema `EmbeddingCache` nel DB + `EmbeddingCacheService` | 1 gg |
| `OllamaEmbeddingProvider` (HTTP client, serializzazione float[]) | 1 gg |
| `OllamaTextModelProvider` | 0.5 gg |
| `OllamaAiProvider`: Smart Mapping (cosine similarity) + ricerca semantica | 1.5 gg |
| `AnomalyDetector` z-score → Health Check (sempre attivo, senza Ollama) | 1 gg |
| Mismatch semantico categoria/EP → Health Check AI layer | 1 gg |
| Generazione `ShortDesc` EP all'import listino | 0.5 gg |
| UI: pannello "Suggerimenti AI" in TaggingView (3 voci + score) | 1 gg |
| Badge AI status in DockablePane + check disponibilità all'avvio | 0.5 gg |
| Settings: toggle AI + URL Ollama + modelli + soglie cosine | 0.5 gg |
| Test e calibrazione soglie cosine (0.65 smart mapping / 0.45 mismatch) | 1 gg |
| Installer WiX/NSIS multi-versione Revit 2022–2026 | 2 gg |
| **Deliverable** | v1.0 production-ready con AI opzionale e installer |

---

## 7. Gantt

```
Settimane:   1  2  3  4  5  6  7  8  9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24
Sprint  0:   ████
Sprint  1:         ████
Sprint  2:               ██████
Sprint  3:                           ████
Sprint  4:                                 ██████
Sprint  5:                                            ████
Sprint  6:                                                  ████
Sprint  7:                                                        ████
Sprint  8:                                                              ████
Sprint  9:                                                                    ████
Sprint 10:                                                                          ████
```

| Sprint | Contenuto | Settimane |
|---|---|---|
| 0 | Setup, ribbon, MVVM, CI/CD | 1–2 |
| 1 | DB SQLite, SessionManager, AutoSave, Recovery | 3–4 |
| 2 | Parser, FTS5, multi-listino, **RoomExtractor**, **ManualQuantityEntry** | 5–7 |
| 3 | Extensible Storage, Shared Parameters, ForgeTypeId | 8–9 |
| 4 | PhaseFilterView, SelectionView, **MappingView 3 tab** | 10–12 |
| 5 | TaggingView, multi-EP, **batch famiglia**, color coding | 13–14 |
| 6 | MeasurementRulesEngine, ExclusionRules, HealthCheckView | 15–16 |
| 7 | ModelDiffService, DockablePane live preview | 17–18 |
| 8 | NpEngine, workflow NP, export NP | 19–20 |
| 9 | **XpweExporter**, **FilterManager**, Export completo, Sessioni | 21–22 |
| 10 | AI Ollama, AnomalyDetector, installer multi-versione | 23–24 |

---

## 8. Tabella rischi aggiornata

| Rischio | Prob. | Impatto | Mitigazione |
|---|---|---|---|
| Struttura XML `.dcf` non documentata ufficialmente | Alta | Media | Analisi empirica file campione prezzario Toscana; forum ACCA |
| Struttura XML XPWE non documentata pubblicamente | Alta | Alta | Analisi empirica file XPWE da PriMus **prima** di scrivere `XpweExporter` (attività obbligatoria Sprint 9) |
| `Room.Height` non disponibile come `BuiltInParameter` | Certa | Alta | `QTO_AltezzaLocale` configurabile nel SetupView; fallback fisso 2.70m documentato all'utente |
| Room "Not Enclosed" su modelli non finiti | Alta | Media | Filtro `Area > 0`; Health Check segnala locali non bounded |
| Breaking changes API Revit 2026+ | Media | Alta | Conditional compilation `#if REVIT2025_OR_LATER` + test per ogni versione target |
| Prestazioni su modelli > 50.000 elementi | Media | Alta | FEC con filtri rapidi, lazy loading, paginazione ResultGrid |
| ES schema conflict tra versioni plug-in | Bassa | Alta | Schema versioning GUID distinti + migrazione automatica |
| Thread violation Revit API da WPF | Media | Alta | Tutti i write via ExternalEvent, nessuna Revit API dal thread WPF |
| UniqueId instabile dopo detach/relink | Bassa | Media | Fallback su `ElementId` + hash famiglia+tipo+posizione |
| Prezzari regionali con struttura gerarchica diversa | Media | Media | Parser gerarchico flessibile; validazione struttura al caricamento |
| Ollama non installato sull'host utente | Alta | Bassa | `NullAiProvider` garantisce degradazione silenziosa; AI sempre opzionale |

---

## 9. Conformità normativa

- **ISO 19650 / UNI PdR 74:2019**: parametri QTO nel Piano di Gestione Informativa come parametri documentati
- **D.Lgs. 36/2023 art. 5, All. II.14**: NP con analisi prezzi strutturata (CT + SG + utile)
- **D.Lgs. 36/2023 art. 120**: export NP per perizie di variante e atti di sottomissione
- **Parere MIT n. 3545/2025**: ribasso d'asta sui NP come campo opzionale
- **DM 312/2021 Tabella R.7**: struttura export Excel compatibile con Quadro Economico / CME
- **Tracciabilità audit trail**: soft delete + `ModifiedAt` su ogni assegnazione; `ElementId` Revit in ogni riga export XPWE e xlsx

---

## 10. Decisioni architetturali consolidate

| Decisione | Scelta | Motivazione |
|---|---|---|
| AI backend | Ollama locale (`IQtoAiProvider`) | Privacy by default; nessun dato esce dalla macchina; graceful degradation via NullAiProvider |
| MCP server | **Abbandonato** | Complessità non giustificata per il caso d'uso; Ollama copre tutti gli use case AI necessari |
| Export primario | XPWE (gerarchia preservata) | Import diretto in PriMus senza perdita struttura capitoli; xlsx per archivio interno |
| Persistenza | SQLite + Extensible Storage (duale) | SQLite per sessioni/ricerca; ES per dati per-elemento e compatibilità workshared |
| Sorgente B altezza | `QTO_AltezzaLocale` + fallback 2.70m | `Room.Height` non è un BuiltInParameter; parametro configurabile è la soluzione standard |
| AI azione | Mai automatica — sempre soggetta a conferma | Modello Revit sotto controllo umano in ogni fase |

---

## 11. Riferimenti documenti sorgente

| Documento | Ruolo | Note |
|---|---|---|
| `Revit-QTO-Plugin-Doc.md` | Documentazione originale v1 | Riferimento storico; sostituita da v3 |
| `docs/superpowers/specs/2026-04-21-ai-integrations-design.md` | AI design spec | MCP server **abbandonato**; Sorgente B+C, XPWE, FilterManager **integrati** in questo piano |
| `QTO-Plugin-Documentazione-v3.md` | Documentazione tecnica v3.0 | Baseline tecnica con codice C# e schema SQLite |
| `QTO-Implementazioni-v3.md` | Implementazioni aggiuntive v2.0 (I1–I10) | Specifiche di dettaglio per I1–I10 |
| `QTO-AI-Integration.md` | Modulo AI con Ollama | Baseline AI: interfacce, embedding cache, smart mapping |
