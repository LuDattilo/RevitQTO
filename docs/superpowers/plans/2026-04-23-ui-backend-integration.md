# Piano di integrazione UI Redesign ↔ Backend Sessione 23 aprile

**Data**: 2026-04-23 19:40+
**Branch sorgenti**:
- `main` (backend-only): 354 test passing, HEAD `c27694b`
- `origin/feature/cme-ui-redesign` (UI redesign): HEAD `227bbfc`, 6 commit divergenti da `265332c`

**Obiettivo**: unificare i due filoni in un'unica release consistente, senza regressioni funzionali e mantenendo la compatibilità AI (opzionale + graceful fallback) garantita da entrambi i plan.

---

## 1. Inventory — cosa è arrivato dove

### main (lavoro Core della sessione di oggi)

- **AI Integration (Fasi F1-F11)**: `QtoRevitPlugin.Core/AI/` completa — `IQtoAiProvider`, `OllamaAiProvider` (incluso `SemanticSearch`/`FindSemanticMismatches` completi), `NullAiProvider`, `AnomalyDetector`, `EmbeddingCache`, `CosineSimilarity`, factory, settings.
- **I8 Nuovi Prezzi**: `NuovoPrezzoCalculator` (D.Lgs. 36/2023 All. II.14) + repo CRUD `NuoviPrezzi`.
- **I13 Voci manuali**: repo CRUD `ManualItems` + `ManualItemsCsvParser` + `MultiSourceAggregator`.
- **I6 Preset regole**: `SelectionRulePreset` + `SelectionRulePresetService` (JSON+DB CRUD).
- **Hardening**: fix race condition `AddFavorite`, schema v10 `UserFavorites`, tracking "usato nel computo" `GetUsedEpCodes`, bottone "Copia da altro CME", fix DataGrid preferiti stretch, rebrand tema RevitCortex teal+slate.
- **354 test passing** (+100+ nuovi).

### feature/cme-ui-redesign (lavoro UI del secondo sviluppatore)

**Core (Sprint 11 addendum)**:
- `Models/SelectionComputationMode.cs` (enum NuovoEsistente / Demolizioni)
- `Models/WorkflowAvailability.cs` (state macchina task-first)
- `Models/FavoriteSet.cs` (modificato per doppio layer progetto/personali)
- `Models/CmeSettings.cs` (+ `LastSessionFilePath`)
- `Search/HybridSearchScope.cs` + `HybridSearchScopeResolver.cs` (ricerca ibrida Listino+Preferiti)
- `Services/WorkflowStateEvaluator.cs` (policy "cosa è abilitato adesso")
- `Data/FileFavoritesRepository.cs` (modificato: split progetto/personali)
- Tests `Sprint11/WorkflowStateEvaluatorTests.cs` (2 test), `HybridSearchScopeResolverTests.cs` (2 test), `Sprint7/FavoritesRepositoryTests.cs` (modificato, +42 righe)

**Plugin UI shell**:
- `HomeView.xaml` + `.cs` (landing task-first, nuovo)
- `DockablePaneViewModel.cs` (workflow state + home come root)
- `QtoDockablePane.xaml` + `.cs` (ridisegno shell)
- `QtoApplication.cs` (+17/-0, wiring minore)
- `Theme/QtoTheme.xaml` (+42 righe, nuovi tokens per home+card)

**Plugin UI Selezione (modulo 4 della tavola)**:
- `SelectionView.xaml` (+55/-0), `SelectionViewModel.cs` (+123/-0): fase nel pannello filtri, modalità computo
- `Services/SelectionService.cs` (+15/-0): aggiunte per modalità computo
- `PhaseFilterView.xaml` e VM: modificati (si trasforma da tab dedicato a embed dentro Selezione)

**Plugin UI Listino (modulo 3/4 della tavola)**:
- `SetupListinoView.xaml` (+94/-0), `SetupListinoView.xaml.cs` (+15/-0): tab ricerca ibrida / preferiti progetto / preferiti personali, selettore scope
- `SetupViewModel.cs` (+297/-0): gestione doppio preferiti, hybrid search scope resolver
- `SetupView.xaml` (+8/-0)

**Plugin UI Phase-bound refresh (modulo 7/8/9)**:
- `MappingView.xaml` + `.cs` + VM: refresh automatico al cambio fase
- `PreviewView.xaml` + `.cs`: idem
- `ComputoStructureViewModel.cs` (+11/-0): hook PhaseChanged
- `Services/SessionManager.cs` (+8/-0): evento PhaseChanged (soft switch)

**Docs**:
- `docs/superpowers/specs/2026-04-23-cme-ui-phase-selection-design.md` (559 righe)
- `docs/superpowers/plans/2026-04-23-cme-ui-redesign.md` (828 righe)
- `docs/superpowers/mockups/2026-04-23-cme-ui-redesign-board.md` (205 righe)

**Stato test feature branch**: da verificare dopo merge. Il plan dichiara +4 test Sprint11 + 42 righe Sprint7 modificate.

---

## 2. Conflitti concreti misurati

Dry-run `git merge-tree main origin/feature/cme-ui-redesign`:

### A — File con conflitto "additivo disgiunto" (merge automatico quasi sicuro dopo inspection)

| File | Main aggiunge | Feature aggiunge | Strategia |
|---|---|---|---|
| `Core/Models/CmeSettings.cs` | 7 proprietà AI (AiEnabled, OllamaBaseUrl, EmbeddingModel, TextModel, 3 soglie) | 1 proprietà `LastSessionFilePath` | Unione manuale: entrambe restano |

### B — File UI in conflitto strutturale (richiedono human review)

| File | Main (oggi) | Feature (UI redesign) | Strategia |
|---|---|---|---|
| `Services/SelectionService.cs` | Filtri parametrici + `ParamOperator` + `ParamFilterRule` | Modalità computo (`SelectionComputationMode`) + API estesa | **Priorità feature** per la struttura; integriamo i 7 operatori di main come overload aggiuntivo |
| `Theme/QtoTheme.xaml` | Rebrand teal+slate RevitCortex | Nuovi tokens per home/card | Base main (rebrand) + merge dei nuovi tokens da feature |
| `UI/Views/SelectionView.xaml` | UI 3 sezioni con filtri parametrici | UI ridisegnata con Fase in cima + modalità | **Priorità feature** per il layout, re-inserire i filtri parametrici come section "Altri filtri" |
| `UI/ViewModels/SelectionViewModel.cs` | `ObservableCollection<ParamFilterRuleVm>` + Add/Remove + debounce | Stato fase + modalità + phase-bound refresh | Merge meccanico: entrambe le collezioni coesistono |
| `UI/Views/SetupListinoView.xaml` | Expander preferiti ridimensionabile + drag&drop + context menu + colonna Preferiti cliccabile + "Copia da altro CME" | Tab separate "Ricerca ibrida" / "Preferiti progetto" / "Preferiti personali" + selettore scope | **Conflitto di design**: richiede decisione |
| `UI/ViewModels/SetupViewModel.cs` | `Favorites` collection + Toggle/Add/Remove/UseInSearch + tracking "usato" + drag&drop handler | Hybrid search + doppio storage preferiti (progetto/personali) | Merge con rifactor: i preferiti diventano 2 collection, `IsFavoriteInLibrary` ridefinito come enum 3-stati |
| `UI/ViewModels/DockablePaneViewModel.cs` | Rename "Export" → "Esporta", cleanup placeholder | HomeView + `Views.First(v => v.Key == QtoViewKey.Home)` + WorkflowAvailability | **Priorità feature**: Home è la nuova root |
| `UI/Panes/QtoDockablePane.xaml.cs` | `ExportView` collegato (no più placeholder) + Home skip via flag | `case QtoViewKey.Home => new HomeView()` + workflow state update | Merge meccanico |
| `UI/ViewModels/ComputoStructureViewModel.cs` | Modifiche minori | Hook PhaseChanged | Merge facile |
| `Application/QtoApplication.cs` | Rimozione bottone Export dal ribbon | Wiring minore startup | Merge facile |

### C — File senza conflitto (auto-merge)

Tutto il resto Core di main (AI/, NP, Manual, Preset, CosineSim…) e tutto ciò che è "solo feature" (HomeView, SelectionComputationMode, WorkflowAvailability, Search/, WorkflowStateEvaluator) **non si sovrappone** → merge pulito.

---

## 3. Decisioni di design da prendere PRIMA di mergere

Questi sono i punti dove il tuo input è necessario per evitare regressioni visive/funzionali:

### D1 — SetupListinoView: drag&drop vs doppio preferiti

Main ha:
- colonna cliccabile "+/★" nei risultati
- doppio click su riga → toggle preferito
- drag&drop riga → Expander preferiti
- context menu tasto destro
- Expander preferiti ridimensionabile con GridSplitter
- Expander con header dinamico "★ I Miei Preferiti (N) · Rimuovi inutilizzati"

Feature ha:
- tab separate "Ricerca ibrida" / "Preferiti progetto" / "Preferiti personali"
- selettore esplicito scope di ricerca

**Proposta**: integrare. Il layout feature diventa la struttura principale (3 tab). Dentro la tab "Ricerca ibrida" si applicano drag&drop + context menu + bottone ★ cell. Il target del drag diventa contestuale: drop su "Preferiti progetto" → salva in progetto, drop su "Preferiti personali" → salva in personali. Il tracking "usato nel computo" resta come colonna extra.

### D2 — Fase: un soft switch con dual-layer preferiti

Il plan feature dice "Il cambio fase è un soft switch — aggiorna le view phase-bound automaticamente senza conferma". Lo reggiamo via evento `SessionManager.PhaseChanged` (già nel feature branch).

**Proposta**: nessun conflitto — si unisce direttamente.

### D3 — SelectionView: merge filtri parametrici + modalità computo

Main ha 3 sezioni: "1 FASE / 2 CATEGORIA / 3 FILTRI PARAMETRICI". Feature ha: Fase + Modalità computo + Filtri secondari (categoria/famiglia/stati/opzioni).

**Proposta**:
- Sezione 1 (feature): "FASE REVIT ATTIVA" + "MODALITÀ COMPUTO"
- Sezione 2 (feature): "FILTRI DI SELEZIONE" (categoria, famiglia, stati elementi, altre opzioni)
- Sezione 3 (main): "FILTRI PARAMETRICI" (operatori avanzati, rimane come sezione aggiuntiva)

### D4 — NuoviPrezzi + ManualItems: collegamento UI

Il backend di main (`NuovoPrezzoCalculator`, repo CRUD, Aggregator) è pronto ma non collegato. Feature non lo ha toccato. **Proposta**: non collegare in questo merge; resta backlog da wiring UI in fasi successive. Se l'user vuole vederlo ora, creiamo placeholder view.

### D5 — AI status bar

Feature tavola mostra una HomeView senza AI status. Il plan feature dichiara "Nessun cambio ai contratti AI". **Proposta**: aggiungere in Home un footer discreto con badge AI status ("AI attiva · nomic-embed-text" o "AI non disponibile (Ollama offline)"). Zero blocco, cosmetic.

---

## 4. Strategia di merge — 4 fasi

### Fase 0 — preparazione (NESSUNA modifica al repo)

- [x] Leggere board MD, plan redesign, spec Fase
- [x] Dry-run merge per identificare conflitti reali
- [x] Questo piano scritto e committato su main

### Fase 1 — Merge clean Core (quasi-automatico, zero rischio UI)

Merge da `origin/feature/cme-ui-redesign` nel Core. I seguenti file vengono aggiunti senza toccare la UI:

- `Core/Models/SelectionComputationMode.cs`
- `Core/Models/WorkflowAvailability.cs`
- `Core/Search/HybridSearchScope.cs`
- `Core/Search/HybridSearchScopeResolver.cs`
- `Core/Services/WorkflowStateEvaluator.cs`
- `Core/Models/FavoriteSet.cs` (modificato, compatibile)
- `Core/Data/FileFavoritesRepository.cs` (modificato, compatibile)
- `Tests/Sprint11/*` + `Tests/Sprint7/FavoritesRepositoryTests.cs`

**Unico conflitto risolvibile**: `Core/Models/CmeSettings.cs` — aggiungere `LastSessionFilePath` in coda alle 7 proprietà AI già su main.

**Verifica**: `dotnet test` su Core con entrambi i sprint → 354 + 4 (Sprint11) = **358 test** attesi.

### Fase 2 — Merge UI shell + Home (cross-check rebrand + navigation)

Merge selettivo:
- `HomeView.xaml` + `.cs` (nuovo, zero conflitto)
- `DockablePaneViewModel.cs` (integrare workflow state + Home come root, mantenere rename "Export" → "Esporta")
- `QtoDockablePane.xaml` + `.cs` (merge: case Home + case Export di main)
- `Theme/QtoTheme.xaml` (base main rebrand teal+slate, aggiungere i 42 righe di feature come tokens home)
- `Application/QtoApplication.cs` (merge facile)

**Verifica**: build Plugin + avvio Revit → la Home compare come landing page, il rebrand teal sopravvive.

### Fase 3 — Merge UI Selezione + Fase (soft switch phase-bound)

Merge:
- `Services/SelectionService.cs` (base feature, aggiungere indietro filtri parametrici di main come overload)
- `UI/ViewModels/SelectionViewModel.cs` (unione delle due collezioni + evento PhaseChanged)
- `UI/Views/SelectionView.xaml` (layout feature + sezione 3 "Filtri parametrici" di main)
- `UI/ViewModels/ComputoStructureViewModel.cs` (hook PhaseChanged)
- `UI/Views/MappingView.xaml.cs`, `PreviewView.xaml.cs` (refresh phase-bound)
- `Services/SessionManager.cs` (evento PhaseChanged)
- Eliminare `QtoViewKey.Phase` dal DockablePane (la Fase non è più tab)

**Verifica**: cambio fase in Selezione → refresh Tagging/Preview/Struttura Computo senza conferma modale.

### Fase 4 — Merge UI Listino ibrido + doppi preferiti (decisione D1)

Merge (dopo conferma D1):
- `UI/ViewModels/SetupViewModel.cs` — merge 3-way:
  - Base feature: 3 collection (`HybridSearchResults`, `ProjectFavorites`, `PersonalFavorites`) + `HybridSearchScopeResolver`
  - Overlay main: `IsFavoriteInLibrary` diventa enum 3-stati (None/Project/Personal), `ToggleFavoriteCommand` diventa parametrizzato per scope, `RemoveUnusedFavoritesCommand` separato per progetto vs personali, drag&drop target contestuale
- `UI/Views/SetupListinoView.xaml` — layout feature (3 tab) + feature UX main (bottone cell, drag&drop, context menu, splitter, "Copia da altro CME")
- `UI/Views/SetupListinoView.xaml.cs` — merge handlers

**Verifica**: UX listino completa come da mockup + drag&drop verso tab preferiti.

---

## 5. Risk assessment

| Rischio | Probabilità | Impatto | Mitigazione |
|---|---|---|---|
| Test Sprint11 falliscono dopo merge | bassa | medio | I test sono già passati sul feature branch; integrazione Core è additiva |
| Regressione visiva rebrand teal perso | bassa | medio | Fase 2 rifa il merge XAML con base main theme |
| SetupViewModel monolitico (~1000 righe post-merge) | media | basso | Estrazione di `FavoritesSectionViewModel` nel backlog (già flaggato) |
| Fase tab ancora referenziata da qualche XAML | bassa | basso | Grep preventivo su `QtoViewKey.Phase` prima del merge |
| Drag&drop target contestuale per 2 tab preferiti | media | basso | Testabile con unit test su `GetDropTargetScope(source, target)` |
| AI F12-F17 ancora da wire-upare | nulla | nullo | Backend dormiente, non impatta questo merge |

---

## 6. Ordine di esecuzione raccomandato

1. **Questo commit + push** — questo piano su `main` perché il secondo sviluppatore (o io in ralph loop) parta da qui.
2. **Branch locale `merge/ui-redesign`** da `main` per fare il merge senza toccare direttamente main.
3. **Fase 1** (Core, 15 min) — merge automatico con 1 fix manuale su `CmeSettings.cs`. Run full test suite.
4. **Fase 2** (Home+Shell, 30 min) — merge UI shell. Verifica avvio Revit + presenza Home.
5. **Fase 3** (Selezione+Fase, 45 min) — merge phase-bound. Verifica cambio fase propagato.
6. **Fase 4** (Listino ibrido, 1h) — merge Listino con decisione D1 integrata.
7. **PR finale su GitHub** `merge/ui-redesign` → `main`.
8. **Tag release** `v1.1.0-ui-redesign`.

---

## 7. Ralph loop / multi-agent

Per accelerare Fasi 3-4 (le più chirurgiche) uso agenti specializzati in parallelo quando possibile:

- **Agent 1 (spec reviewer)**: dispatch per ogni file in conflitto → legge i diff di entrambi i lati + produce un "merge plan file-by-file" deterministico
- **Agent 2 (implementer)**: esegue il merge file-by-file seguendo il plan
- **Agent 3 (test runner + validator)**: dopo ogni fase esegue `dotnet test` + build cross-target + diff screenshot mockup vs render

Il ralph loop scatta ogni volta che la build fallisce o i test regrediscono.

---

## 8. Stato di attesa

- [ ] **D1** — SetupListinoView: conferma integrazione drag&drop dentro tab ibride (proposta sopra)
- [ ] **D5** — AI status bar in Home: si/no?
- [ ] Conferma timing: merge ora o dopo review del piano?

Se nessuna obiezione, procedo con Fase 1 immediatamente.
