# Mappa schermate applicazione CME

Questa mappa usa i nomi presenti nel codice XAML/C# per permettere richieste di modifica precise. Le schermate principali sono ospitate da `QtoDockablePane` e cambiate tramite il workflow in alto.

## Contenitore principale: QtoDockablePane

File: `QtoRevitPlugin/UI/Panes/QtoDockablePane.xaml`

### Header
- Logo testuale: `Q`
- Titolo: `CME Workspace`
- Sottotitolo: `Hub operativo per setup, listino, selezione e tagging`
- Box sessione:
  - `SessionTitle`
  - `ProjectSubtitle`
- Pulsante: `Sessione ▾`
- Menu sessione:
  - `Salva`
  - `Salva con nome…`
  - `Rinomina…`
  - `Chiudi computo`
  - `Elimina…`
  - `Impostazioni…`
- Badge: `REVIT 2025 · LIVE`

### KPI header
- `Progresso` con `ProgressText`
- `Importo` con `AmountText`
- `Contesto fase` con `PreviewPhaseContext`

### Navigazione
- Sezione: `Workflow`
- Testo guida: `Listino prima di Selezione...`
- Workflow primario:
  - `Home`
  - `Setup progetto`
  - `Listino`
  - `Selezione`
  - `Redazione CME`
  - `Verifica`
  - `Esporta`
- Sezione: `Strumenti secondari`
- Strumenti secondari:
  - `Health`
  - `Filtri Vista`
  - `Viste CME`
- Area contenuto: `ViewHost`
- Barra stato inferiore: `StatusMessage`

## 1. Home

File: `QtoRevitPlugin/UI/Views/HomeView.xaml`

### Card principale
- Titolo: `Home`
- Sottotitolo: `Avvio operativo del computo`
- Messaggi dinamici:
  - `HomePrimaryMessage`
  - `HomeSecondaryMessage`
- Pulsanti:
  - `Nuovo computo`
  - `Apri computo`
  - `Riprendi ultimo`

### KPI sessione
Visibili solo con sessione attiva.
- `ELEMENTI` con `TotalElements`
- `TAGGATI` con `ProgressText`
- `IMPORTO` con `AmountText`

### Box laterali
- `Ultimo computo` con `LastSessionHint`
- `Vincoli di flusso`
- `Stato AI`
  - icona da `AiStatus`
  - label `AiStatusLabel`
  - hint `AiStatusHint`

### Workflow interno
- Sezione: `WORKFLOW`
- Chip cliccabili generati da `HomeWorkflowSteps`
- Ogni chip mostra:
  - stato/glyph
  - ordine
  - label
  - hint

## 2. Setup Progetto

File: `QtoRevitPlugin/UI/Views/SetupView.xaml`

Schermata contenitore con titolo `Setup` e tab interne:
- `Informazioni`
- `Struttura Computo`
- `Capitoli (v12)`
- `Categorie (v12)`
- `WBS (v12)`
- `Nuovi Prezzi`

## 2.1 Informazioni Progetto

File: `QtoRevitPlugin/UI/Views/ProjectInfoView.xaml`

### Header
- Titolo: `Informazioni Progetto`

### Sezione metadati
- Label: `METADATI COMPUTO · INTESTAZIONE EXPORT XPWE / XLSX`
- Lista campi dinamica: `FieldRows`
- Per ogni campo:
  - label campo: `Label`
  - casella testo: `Value`
  - dropdown sorgente: `SelectedSource`

### Date e gara
- Label: `DATE E PARAMETRI GARA`
- Campi:
  - `Data computo`
  - `Data prezzi listino`
  - `Ribasso d'asta (%)`

### Footer
- Stato: `StatusMessage`
- Pulsanti:
  - `📋 Copia da altro CME…`
  - `💾 Salva`

## 2.2 Struttura Computo

File: `QtoRevitPlugin/UI/Views/ComputoStructureView.xaml`

### Toolbar CRUD
- `+ Super`
- `+ Cat`
- `+ Sub`
- `Rinomina`
- `Elimina`
- `↑` con tooltip `Sposta su`
- `↓` con tooltip `Sposta giù`

### Albero
- TreeView: `ChaptersTree`
- Origine dati: `Roots`
- Nodo visualizzato: `DisplayLabel`
- Drag & drop abilitato.

### Dettaglio nodo
- Sezione: `CODICE SOA (OG / OS) — D.LGS. 36/2023 ALL. II.12`
- Campo: `Nodo selezionato:`
- ComboBox SOA:
  - `AvailableSoa`
  - `SelectedNodeSoa`
- Stato: `StatusMessage`

## 2.3 Capitoli (v12)

File: `QtoRevitPlugin/UI/Views/ChapterNodesView.xaml`

### Albero
- TreeView: `ChapterTree`
- Origine dati: `RootNodes`
- Nodo: `DisplayLabel`

### Inserimento
- Campo: `Codice:`
- Campo: `Descrizione:`
- Pulsanti:
  - `+ SuperCap`
  - `+ Cap`
  - `+ SubCap`
  - `Elimina`
  - `Aggiorna`
- Stato: `StatusMessage`

## 2.4 Categorie (v12)

File: `QtoRevitPlugin/UI/Views/CategoryNodesView.xaml`

### Albero
- TreeView: `CategoryTree`
- Origine dati: `RootNodes`
- Nodo: `DisplayLabel`

### Inserimento
- Campo: `Codice:`
- Campo: `Descrizione:`
- Pulsanti:
  - `+ SuperCat`
  - `+ Cat`
  - `+ SubCat`
  - `Elimina`
  - `Aggiorna`
- Stato: `StatusMessage`

## 2.5 WBS (v12)

File: `QtoRevitPlugin/UI/Views/WbsNodesView.xaml`

### Filtro tipo WBS
- Label: `WBS:`
- ComboBox: `Kinds` / `SelectedKind`

### Albero
- TreeView: `WbsTree`
- Origine dati: `RootNodes`
- Nodo: `DisplayLabel`

### Inserimento
- Campo: `Descrizione:`
- Pulsanti:
  - `+ Root`
  - `+ Figlio`
  - `Elimina`
  - `Aggiorna`
- Stato: `StatusMessage`

## 2.6 Nuovi Prezzi

File: `QtoRevitPlugin/UI/Views/NuoviPrezziView.xaml`

Schermata placeholder.
- Sezione: `NUOVI PREZZI (ANALISI PREZZI NP)`
- Card: `Sezione in preparazione`
- Descrizione campi database predisposti:
  - `Codice`
  - `Descrizione`
  - `Unità`
  - `PrezzoUnitario`
  - `Manodopera`
  - `Materiali`
  - `Noli`
  - `Trasporti`
  - `SpeseGenerali`
  - `UtileImpresa`
- Tracker: `Sprint 11+`

## 3. Listino

File: `QtoRevitPlugin/UI/Views/SetupListinoView.xaml`

### Header
- Testo: `Gestione listini prezzi · ricerca ibrida · preferiti progetto/personali`
- Pulsante: `⤢ Apri in finestra`

### Listini in libreria
- Sezione: `LISTINI IN LIBRERIA (PERSISTENTI · CONDIVISI TRA COMPUTI)`
- Pulsanti:
  - `+ Importa listino…`
  - `Sfoglia listino…`
  - `Elimina`
- Tabella: `PriceListsGrid`
- Colonne:
  - `Attivo`
  - `Nome`
  - `Sorgente`
  - `Regione`
  - `Voci`
  - `Prio`
  - `Importato`
- Menu contestuale tabella:
  - `Attiva/Disattiva listino`
  - `Sfoglia voci del listino…`
  - `🗑 Elimina dalla libreria…`
- Stato: `StatusMessage`

### Ricerca e preferiti
- Sezione: `RICERCA IBRIDA · PREFERITI PROGETTO · PREFERITI PERSONALI`
- Campo ricerca: `SearchBox`
- ComboBox scope: `AvailableScopes` / `SelectedScope`
- Badge livello ricerca: `LastSearchLevel`
- Tab:
  - `Ricerca ibrida`
  - `Preferiti progetto`
  - `Preferiti personali`

### Tab Ricerca ibrida
- Tabella: `SearchResultsGrid`
- Colonne:
  - `★`
  - `Codice`
  - `Descrizione`
  - `U.M.`
  - `Prezzo`
  - `P.2`
  - `MDO%`
  - `SIC%`
  - `Listino`
- Riga dettaglio:
  - `Code`
  - `UnitPriceFormatted`
  - `Unit`
  - `ListName`
  - `Description`
- Menu contestuale:
  - `★ Aggiungi a preferiti PROGETTO`
  - `★ Aggiungi a preferiti PERSONALI`
  - `Copia codice`

### Tab Preferiti progetto
- Descrizione: `Preferiti legati al file .cme aperto · trascina una voce qui per aggiungerla`
- Pulsante: `🗑 Rimuovi inutilizzati`
- Empty state:
  - `Nessun preferito progetto`
- Tabella: `ProjectFavoritesGrid`
- Colonne:
  - `Usato`
  - `Codice`
  - `Descrizione`
  - `U.M.`
  - `Prezzo`
  - `Listino`
  - `Aggiunto`
- Menu contestuale:
  - `Usa nella ricerca`
  - `Copia codice`
  - `⧉ Duplica voce (personalizzabile)`
  - `→ Sposta in PERSONALI`
  - `🗑 Rimuovi dai preferiti`
  - `↻ Aggiorna stato 'Usato'`

### Tab Preferiti personali
- Descrizione: `Preferiti globali utente (AppData) · condivisi tra tutti i computi`
- Pulsante: `🗑 Rimuovi inutilizzati`
- Empty state:
  - `Nessun preferito personale`
- Tabella: `PersonalFavoritesGrid`
- Colonne:
  - `Usato`
  - `Codice`
  - `Descrizione`
  - `U.M.`
  - `Prezzo`
  - `Listino`
  - `Aggiunto`
- Menu contestuale:
  - `Usa nella ricerca`
  - `Copia codice`
  - `⧉ Duplica voce (personalizzabile)`
  - `→ Sposta in PROGETTO`
  - `🗑 Rimuovi dai preferiti`
  - `↻ Aggiorna stato 'Usato'`

### Pannello dettaglio listino
- Campi:
  - `Code`
  - `SourceLabel`
  - `UnitPriceFormatted`
  - `HierarchyPath`
  - `Description`
- Stato ricerca: `SearchStatus`
- Footer: `Listino persistente tra computi · preferiti PROGETTO nel .cme · preferiti PERSONALI in AppData.`

## 4. Selezione

File: `QtoRevitPlugin/UI/Views/SelectionView.xaml`

### Header
- Titolo: `Selezione Elementi`
- Pulsante: `⤢ Apri in finestra`

### Filtro 1: fase Revit
- Sezione: `1 — FASE REVIT`
- ComboBox fase:
  - `AvailablePhases`
  - `SelectedPhase`
- Sezione: `Modalità computo`
- ComboBox:
  - `ComputationModes`
  - `ComputationMode`
- Testo contesto: `ActivePhaseName`

### Filtro 2: categoria
- Sezione: `2 — CATEGORIA`
- Campo: `Categoria:`
- ComboBox:
  - `Categories`
  - `SelectedCategory`
- Campo: `Cerca nome:`
- TextBox: `NameQuery`

### Filtro 3: filtri parametrici
- Sezione: `3 — FILTRI PARAMETRICI`
- Pulsante: `+ Aggiungi filtro`
- Lista: `ParamRules`
- Per ogni filtro:
  - AND/OR: `LogicLabel`
  - parametro: `ParameterName`
  - operatore: `OperatorLabel`
  - valore: `Value`
  - pulsante rimuovi: `×`

### Toolbar azioni vista
- `Isola in vista`
- `Nascondi`
- `Reset vista`
- `Aggiorna`

### Card Assegna EP
- Sezione: `ASSEGNA VOCE EP`
- Campi:
  - `ActiveEpCode`
  - `ActiveEpDescription`
  - `Quantità per istanza:`
  - ComboBox `QuantityModeOptions` / `QuantityMode`
  - Preview: `AssignPreview`
- Pulsante dinamico: `AssignButtonText`
- Banner esito:
  - `LastAssignSummary`

### Tabella elementi
- Tabella: `GridElements`
- Colonne:
  - `Id`
  - `Famiglia`
  - `Tipo`
  - `Livello`
  - `Fase creaz.`
  - `Fase demol.`
- Stato inferiore: `StatusMessage`

## 5. Redazione CME

File: `QtoRevitPlugin/UI/Views/CmeEditorView.xaml`

### Header
- Titolo: `Redazione CME`
- Pulsante: `⤢ Apri in finestra`

### Colonna sinistra: navigatore
- Sezione: `NAVIGATORE`
- Radio:
  - `Per Capitoli`
  - `Per Categorie`
  - `Per WBS`
  - `Lineare (tutto)`
- TreeView:
  - `NavTree`
  - nodo `Label`

### Colonna centrale: tabella voci
- Tabella: `VisibleRows`
- Colonne:
  - `Codice`
  - `Descrizione`
  - `UM`
  - `Qta`
  - `Prezzo`
  - `Importo`
- Riga dettaglio:
  - `SubRows`
  - `Descrizione`
  - `Formula`

### Colonna destra: quadro economico
- Sezione: `QUADRO ECONOMICO`
- Totale: `Totale netto`
- Elenchi:
  - `PER CAPITOLO`
  - `PER CATEGORIA`
- Per ogni riga:
  - `Label`
  - `TotaleFormatted`
  - `PercentualeFormatted`

### Footer
- Stato: `StatusMessage`
- Pulsante: `Aggiorna`

## 6. Verifica

Nel menu principale la voce `Verifica` apre `PreviewView`.

File: `QtoRevitPlugin/UI/Views/PreviewView.xaml`

### Header
- Titolo: `Preview Live`
- Contesto: `PreviewPhaseContext`

### Switch tab
- `Selezione Corrente`
- `Riepilogo`

### Tab Selezione Corrente
- Sezione: `NESSUN ELEMENTO SELEZIONATO`
- Box: `DISPONIBILE DAL`
- Testo: `Sprint 5 — Tagging`

### Tab Riepilogo
- Sezione: `AVANZAMENTO SESSIONE`
- ProgressBar: `TaggedPercent`
- Testo progresso: `ProgressText`
- KPI:
  - `ELEMENTI TOTALI` con `TotalElements`
  - `IMPORTO PARZIALE` con `AmountText`

## 7. Esporta

File: `QtoRevitPlugin/UI/Views/ExportView.xaml`

### Header
- Titolo: `Esporta Computo`

### Card principale
- Titolo: `Chiusura del Computo Metrico Estimativo`
- Formati citati:
  - `XPWE`
  - `Excel`
  - `PDF`
  - `CSV`
- Pulsante:
  - `↗ Apri Wizard di Esportazione`
- Nota:
  - `Richiede una sessione .cme attiva con almeno una voce assegnata.`

## 7.1 Wizard Esportazione

File: `QtoRevitPlugin/UI/Views/ExportWizardWindow.xaml`

### Formato export
- Sezione: `Formato Export`
- ListBox: `AvailableExporters`

### Intestazione
- Sezione: `Intestazione`
- Campi:
  - `Template grafico:`
  - `Titolo:`
  - `Committente:`
  - `Direttore Lavori:`

### Opzioni
- Sezione: `Opzioni`
- CheckBox:
  - `Includi campi audit (Version, CreatedBy, AuditStatus) — CSV`
  - `Includi Superseded + Deleted (storico completo)`
- Logo PDF:
  - `Logo (PDF):`
  - TextBox `CompanyLogoPath`
  - pulsante `Sfoglia...`
- Stato: `StatusMessage`
- Pulsanti:
  - `Esporta`
  - `Chiudi`

## 8. Health

File: `QtoRevitPlugin/UI/Views/HealthView.xaml`

### Header
- Titolo: `Health Check`

### Toolbar
- Stato: `StatusMessage`
- Spinner testuale quando `IsRunning`
- Pulsante: `Esegui controllo`

### KPI report
Visibili quando `HasReport`.
- `ASSEGNAZIONI`
- `ANOMALIE QTÀ`
- `MISMATCH AI`
- `AI`

### Empty state
- `Nessun problema rilevato. Il computo è coerente.`

### Anomalie quantità
- Sezione: `ANOMALIE QUANTITÀ`
- Tabella: `AnomaliesGrid`
- Colonne:
  - `Severità`
  - `EP`
  - `UniqueId`
  - `Qtà`
  - `Media`
  - `Z-score`
  - `Messaggio`

### Mismatch semantici AI
- Sezione: `MISMATCH SEMANTICI (AI)`
- Tabella: `MismatchesGrid`
- Colonne:
  - `Similarità`
  - `Categoria · Famiglia`
  - `EP assegnato`
  - `Descrizione EP`
  - `Alternativa`

## 9. Filtri Vista

Nel menu principale è una `PlaceholderView`.

Titolo mostrato: `Filtri Vista Nativi`

Descrizione:
- `3 ParameterFilterElement persistenti: CME_Taggati (verde), CME_Mancanti (rosso), CME_Anomalie (grigio halftone).`
- Applicabili a vista corrente, template o set di viste.

## 10. Viste CME

Nel menu principale è una `PlaceholderView`.

Titolo mostrato: `Viste CME Dedicate`

Descrizione:
- Vista 3D isometrica CME
- piante 2D per livello
- 3 schedule nativi:
  - `Assegnazioni`
  - `Mancanti`
  - `Nuovi Prezzi`

## Schermate legacy / non raggiunte dal menu principale

## Mapping Sorgenti

File: `QtoRevitPlugin/UI/Views/MappingView.xaml`

Nota: il code-behind indica che `MappingView` è legacy e non è più raggiungibile dal menu principale; `Tagging` ora apre `CmeEditorView`.

### Tab Famiglie
- Header: `SORGENTE A · Famiglie Revit → Voce EP`
- Filtro: `Categoria Revit:`
- Pulsanti:
  - `Aggiorna`
  - `Assegna EP…`
- Tabella: `GridFamilies`
- Colonne:
  - `Famiglia`
  - `Tipo`
  - `N° istanze`
  - `EP assegnato`
  - `Prezzo stimato`

### Tab Locali
- Header: `SORGENTE B · Room / MEP Space → formula NCalc`
- Pulsanti:
  - `+ Nuova formula…`
  - `Modifica`
  - `Elimina`
- Tabella: `GridRoomMappings`
- Colonne:
  - `Codice EP`
  - `Descrizione`
  - `UM`
  - `Formula`
  - `Target`
  - `Filtro nome`
- Editor formula:
  - `EDITOR FORMULA`
  - `Codice EP:`
  - `Descrizione:`
  - `UM:`
  - pulsanti `Test formula…`, `Annulla`, `Salva`

### Tab Voci manuali
- Header: `SORGENTE C · Voci manuali`
- Pulsanti:
  - `+ Nuova voce manuale…`
  - `Modifica`
  - `Duplica`
  - `Elimina`
- Tabella: `GridManualItems`
- Colonne:
  - `Codice EP`
  - `Descrizione`
  - `UM`
  - `Quantità`
  - `Prezzo Unit.`
  - `Totale`
  - `Note`
- Editor voce manuale:
  - `EDITOR VOCE MANUALE`
  - `Cerca nella UserLibrary…`
  - `Codice EP:`
  - `Descrizione:`
  - `UM:`
  - `Quantità:`
  - `Prezzo Unit.:`
  - `Note:`
  - `Totale riga`
  - pulsanti `Annulla`, `Salva`
- Footer:
  - `TOTALE VOCI MANUALI`

## Fasi Revit

File: `QtoRevitPlugin/UI/Views/PhaseFilterView.xaml`

Nota: la view esiste nel codice ma non è nel workflow principale attuale.

### Header
- Titolo: `Fasi Revit`
- Pulsante: `⤢ Apri in finestra`

### Istruzioni
- Banner con testo di selezione fase corrente.

### Fasi del documento
- Sezione: `FASI DEL DOCUMENTO`
- Pulsante: `Calcola conteggio elementi`
- Empty state:
  - `Apri un progetto Revit per leggere le fasi.`
- Lista `Phases`, per ogni fase:
  - radio button
  - `Name`
  - `Sequence`
  - `PhaseId`
  - `ElementCountLabel`

### Footer
- Stato: `StatusMessage`
- Pulsante: `✓ Conferma fase`

## Dialog e finestre accessorie

### AddSharedParameterDialog

File: `QtoRevitPlugin/UI/Views/AddSharedParameterDialog.xaml`

- Titolo: `Crea un parametro condiviso`
- Campi:
  - `Nome parametro`
  - `Descrizione (opzionale)`
- Sezione: `File Shared Parameters`
- Radio:
  - `File SP del progetto corrente`
  - `File CME dedicato (%AppData%\QtoPlugin\CME_SharedParameters.txt)`
- Stato: `StatusBlock`
- Pulsanti:
  - `Annulla`
  - `Crea e aggiungi`

### CatalogBrowserWindow

File: `QtoRevitPlugin/UI/Views/CatalogBrowserWindow.xaml`

- Titolo: `SFOGLIA LISTINO`
- Descrizione: `Finestra dedicata a ricerca voci, preferiti e dettaglio — sincronizzata col pannello principale`
- Pulsante: `Chiudi`

### ChapterEditorPopup

File: `QtoRevitPlugin/UI/Views/ChapterEditorPopup.xaml`

- Campi:
  - `Codice:`
  - `Nome:`
- Pulsanti:
  - `OK`
  - `Annulla`

### SettingsDialog

File: `QtoRevitPlugin/UI/Views/SettingsDialog.xaml`

Da analizzare nel dettaglio in una seconda passata se vuoi modificare le impostazioni: la finestra è aperta dal menu `Sessione ▾ > Impostazioni…`.
