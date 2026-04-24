# Tagging Refactor + CME Redazione · Design Spec

**Data:** 2026-04-24
**Stato:** Design approvato · pronto per plan
**Contesto sessione:** lavoro su SelectionView (filtri parametrici builtin-aware, custom columns); scoperto che il modello "Tagging" come scheda separata non corrisponde al workflow reale dell'utente.

---

## 1. Problema attuale

Oggi il plugin ha 3 schede distinte per il flusso di assegnazione:

- **Selezione** — filtra elementi Revit per categoria + parametri
- **Listino** — naviga catalogo EP + preferiti
- **Tagging** (oggi rende `MappingView`) — assegna EP a famiglie (Sorgente A) + formule Room (Sorgente B) + voci manuali (Sorgente C)

**Attriti osservati:**

1. L'utente si aspetta **un'azione diretta** "assegna voce → elementi" o "assegna elementi → voce", non una terza scheda di mediazione.
2. La scheda Tagging duplica lavoro: l'utente ha **già** filtrato in Selezione, ma in Tagging deve ri-selezionare la categoria/i parametri.
3. Non esiste una vista del **CME in redazione**: l'utente assegna ma non vede mai l'albero capitoli → voci → quantità che si sta costruendo.
4. La `MappingView` fa 3 cose eterogenee (Famiglie / Formule Room / Voci manuali). Le ultime due non sono "tagging elementi" ma gestione di sorgenti di quantità autonome — vivono in un posto sbagliato concettualmente.

## 2. Visione

**Due finestre operative indipendenti**, aperte in parallelo, che si parlano:

- **Listino** — è il **cosa**: scegli la voce EP (da catalogo o preferiti)
- **Selezione** — è il **a chi**: filtri elementi, isoli in vista, eventualmente clicchi manualmente in Revit

**Assegnazione simmetrica**: l'azione "assegna EP ↔ elementi" è esposta da entrambe le schede.

**Nuova scheda** al posto di Tagging:

- **Redazione CME** — vista **live** del computo in costruzione: albero capitoli → voci → quantità, aggiornata in tempo reale quando l'utente assegna. Supporta operazioni di editing sull'albero (rinomina capitolo, sposta voce, override quantità con sblocco esplicito).

**Sorgenti B e C** (formule Room, voci manuali) — trasferite dentro **Selezione** come due ulteriori tab o card, perché sono modi alternativi di definire "cosa taggare" (la Room con una formula, la voce manuale come quantità pre-definita).

## 3. Requisiti

### R1 — Simmetria di assegnazione

- **R1.1** — Da **Selezione**: bottone "Assegna EP corrente" prende la voce attiva dal Listino (single source of truth nella sessione) e tagga gli elementi sorgente correnti.
- **R1.2** — Da **Listino**: bottone "Applica a selezione corrente" prende gli elementi sorgente dalla Selezione e li tagga con la voce attualmente selezionata nel Listino.
- **R1.3** — La "voce EP corrente" è uno stato globale della sessione, visibile in entrambe le schede come breadcrumb (`Codice · Descrizione breve`).

### R2 — Sorgente elementi implicita (C2 = "a")

- **R2.1** — Quando l'utente clicca "Applica", il plugin usa **tutto ciò che è in Selezione** in questo ordine di priorità:
  1. **Selezione manuale Revit attiva** (elementi pickati in finestra tramite `UIDocument.Selection.GetElementIds()`), se non vuota
  2. Altrimenti **elementi filtrati** visibili nella tabella Selezione
- **R2.2** — La banda di preview mostra sempre **quanti elementi** verranno taggati e **con quale sorgente** (es. "3 elementi · sorgente: selezione Revit"), così l'utente non applica al sbagliato.
- **R2.3** — Il pick manuale in Revit (cliccare elementi nella finestra 3D/piano) è riconosciuto dal plugin senza dover riaprire la scheda Selezione — quindi la Selezione ha un listener su `UIApplication.ViewActivated` + polling di `UIDocument.Selection` o un `IUpdater` leggero. (Dettaglio implementativo nel plan.)

### R3 — Quantità per istanza

- **R3.1** — La card "QUANTITÀ PER ISTANZA" resta ed espone radio: Conteggio (cad) · Area (m²) · Volume (m³) · Lunghezza (m).
- **R3.2** — Default per categoria:
  - Walls → Area
  - Floors → Area
  - Columns → Conteggio (o Volume se strutturali, TBD — si conferma col runtime)
  - Rooms → Area
  - Default generico → Conteggio
- **R3.3** — La banda ANTEPRIMA mostra: numero istanze · media · totale · importo (prezzo × totale) se la voce EP ha un prezzo unitario.

### R4 — Scheda "Redazione CME" (sostituisce Tagging)

- **R4.1** — Stesso slot nel menu di `QtoViewKey.Tagging` (si rinomina la key → `CmeEditor` o `Redazione`, label "Redazione CME").
- **R4.2** — Mostra **albero gerarchico**: Capitoli → Voci → Aggregazioni di assegnazioni. Ogni nodo voce mostra: codice, descrizione, quantità totale, importo.
- **R4.3** — **Live update**: quando l'utente assegna in Selezione o Listino, il nodo corrispondente si aggiorna immediatamente (push via `SessionManager.AssignmentsChanged`).
- **R4.4** — Operazioni di editing sull'albero:
  - Rinomina capitolo (inline edit)
  - Sposta voce tra capitoli (drag&drop riuso `GongSolutions.WPF.DragDrop` come già fatto per ExportTemplate)
  - Sblocca override quantità per voce (toggle → la quantità diventa editabile, il plugin ricorda che è override)
  - Rimuovi assegnazione (cancella il tag da uno o più elementi)

### R5 — Sorgenti A, B, C → Selezione (tutti i tab)

- **R5.1** — La `MappingView` oggi esistente viene **smembrata**. Tutti e tre i tab migrano in Selezione (la Sorgente A resta perché offre vista aggregata FamilyType→count, utile quando gli elementi sono molti e si vuole pensare per tipo):
  - Tab 0 "Famiglie aggregate" (Sorgente A) → spostato in Selezione come nuovo tab
  - Tab 1 "Locali" (Sorgente B) → spostato in Selezione come nuovo tab "Formule Room"
  - Tab 2 "Voci manuali" (Sorgente C) → spostato in Selezione come nuovo tab "Voci manuali"
- **R5.2** — Selezione diventa una scheda con **4 tab**:
  - Tab 0 "Elementi" (istanze singole, quello attuale con filtri parametrici e colonne custom)
  - Tab 1 "Famiglie/Tipi" (aggregato Sorgente A)
  - Tab 2 "Formule Room" (Sorgente B)
  - Tab 3 "Voci manuali" (Sorgente C)
- **R5.3** — L'azione "Assegna" dal Listino rispetta il tab attivo in Selezione:
  - Tab 0 → assegna agli elementi (istanze)
  - Tab 1 → assegna a tutti gli elementi del FamilyType selezionato (comodità: una click per tutti i 200 muri di quel tipo)
  - Tab 2 → crea/aggiorna la formula Room
  - Tab 3 → crea la voce manuale con la voce EP attiva

### R6 — Bug grafico (contrasto)

- **R6.1** — Le label dei radio nella card "QUANTITÀ PER ISTANZA" sono illeggibili (grigio chiaro su sfondo chiaro). Correggere con `Foreground="{DynamicResource InkBrush}"`.
- **R6.2** — La banda ANTEPRIMA (sfondo teal) ha testo semi-trasparente. Correggere forzando `Foreground="White"` + font semibold.
- **R6.3** — Fix da applicare nello stesso commit del refactor? **No** — commit separato dedicato, precede il refactor (per poter testare il refactor con UI leggibile).

## 4. Architettura

### 4.1 Stato globale condiviso

`SessionManager` aggiunge:

- `ActiveEpCode` (string?) — la voce EP selezionata nel Listino, letta da Selezione
- `ActiveSelectionSourceIds` (IReadOnlyList<int>) — gli Id correnti in Selezione (filtered o manual pick)
- Eventi: `ActiveEpChanged`, `ActiveSelectionChanged`, `AssignmentsChanged`

### 4.2 Comando di assegnazione (simmetria)

Il `AssignEpCommandRunner` esistente viene incapsulato in un **`AssignmentCoordinator`** — servizio che:

- Prende `ActiveEpCode` + `ActiveSelectionSourceIds` + `QuantityMode` (radio)
- Esegue il runner in transazione Revit
- Notifica `SessionManager.AssignmentsChanged` al termine
- Restituisce il risultato per la banda ANTEPRIMA

### 4.3 Navigation

`QtoViewKey.Tagging` → rinominato in `QtoViewKey.CmeEditor` (breaking change minore, impatta 6 file secondo la grep).

### 4.4 File impattati (stima alta)

**Modifiche**:
- `QtoRevitPlugin/UI/ViewModels/DockablePaneViewModel.cs` — rinomina view key, aggiorna label
- `QtoRevitPlugin/UI/Panes/QtoDockablePane.xaml.cs` — switch case `CmeEditor` → `CmeEditorView` (nuova)
- `QtoRevitPlugin/UI/Views/SelectionView.xaml(.cs)` — aggiungi `TabControl` con 3 tab, card "Assegna EP corrente", card "Quantità per istanza", banda preview
- `QtoRevitPlugin/UI/ViewModels/SelectionViewModel.cs` — integra `ActiveTabIndex`, espone sorgente elementi, listener sessione
- `QtoRevitPlugin/Services/SessionManager.cs` — nuovi eventi + stato `ActiveEpCode` / `ActiveSelectionSourceIds`

**Nuovi**:
- `QtoRevitPlugin/UI/Views/CmeEditorView.xaml(.cs)` — albero capitoli/voci live
- `QtoRevitPlugin/UI/ViewModels/CmeEditorViewModel.cs` — aggrega assegnazioni da DB, espone tree
- `QtoRevitPlugin/Services/AssignmentCoordinator.cs` — orchestratore simmetrico
- `QtoRevitPlugin/Services/RevitSelectionWatcher.cs` — listener pick manuale Revit

**Da rimuovere/migrare**:
- `QtoRevitPlugin/UI/Views/MappingView.xaml(.cs)` — i contenuti dei 3 tab (Famiglie aggregate, Locali, Voci manuali) vengono trasferiti nei nuovi tab di Selezione. Il file viene cancellato dopo la migrazione.
- `QtoRevitPlugin/UI/ViewModels/MappingViewModel.cs` — smembrare in 3 ViewModel focalizzati: `AggregatedFamiliesViewModel` (A), `RoomFormulasViewModel` (B), `ManualItemsViewModel` (C). Ognuno diventa il DataContext del rispettivo tab.

### 4.5 Persistenza

Nessuna nuova tabella. Si continua a usare:
- `Assignments` (già esistente)
- `RoomMappings` (già esistente)
- `ManualItems` (già esistente)
- `UserFavorites` (già esistente)

Tutti i dati stanno già nel DB; il refactor tocca solo la UI e il wiring.

## 5. Scope NON incluso

- Persistenza "ultima voce EP attiva" tra sessioni → no, `ActiveEpCode` è solo runtime
- Undo/redo delle assegnazioni → no, usa il meccanismo Revit `Transaction`
- Multi-selezione di voci EP contemporanee → no, una sola voce attiva per volta
- Reorder drag&drop delle colonne custom in Selezione → già fatto (CanUserReorderColumns)
- Persistenza del preset colonne custom → no, in-memory sessione UI

## 6. Piano di decomposizione

Il refactor è grosso; si spezza in **5 sotto-progetti indipendenti**, ognuno con il proprio plan:

1. **Fix grafico contrasto** (R6) — standalone, bassissimo rischio, si committa da solo per primo.
2. **SessionManager eventi + ActiveEp + AssignmentCoordinator** (R1.3, 4.1, 4.2) — fondamenta senza UI.
3. **Selezione multi-tab + migrazione Room/Manuali** (R5) — UI grande ma isolata sulla scheda Selezione.
4. **Bottoni "Applica" simmetrici + RevitSelectionWatcher** (R1.1, R1.2, R2) — collega i pezzi.
5. **CmeEditorView** (R4) — nuova scheda, ultima in ordine.

Ogni sotto-progetto **produce software funzionante**. Dopo (2) l'app continua a girare senza UI nuova. Dopo (3) la Selezione ha 3 tab ma l'azione simmetrica ancora non c'è. Dopo (4) l'utente ha il flusso completo senza CME view. Dopo (5) è tutto integrato.

## 7. Decisioni bloccate (per futuro plan)

- **C1** ✓ — pick manuale in Revit = sorgente elementi supportata via `UIDocument.Selection`
- **C2** ✓ — sorgente implicita a cascata (manual pick → filtrati)
- **C3** ✓ — CmeEditor con editing completo (rinomina capitolo, sposta voce, override quantità)
- Nome finale scheda CME: "Redazione CME" (label) / `QtoViewKey.CmeEditor` (enum)
- Sorgente A (preview Famiglie aggregate) → **spostata** in Selezione come tab 1 (non eliminata): offre vista aggregata per tipo utile quando le istanze sono molte
- Eventi cross-scheda → esposti da `SessionManager` (pattern già in uso nel progetto), no event aggregator dedicato

---

**Prossimo passo:** scrivere il **plan del sotto-progetto 1** (fix grafico contrasto) come primo commit isolato, poi procedere in ordine.
