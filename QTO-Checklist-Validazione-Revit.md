# Checklist di validazione manuale in Revit — Modulo Computo (Pulse → CME)

Documento operativo per certificare in Revit 2025.4 il lavoro del branch
`claude/revit-cme-addin-repo-gu71y9` (riconciliazione Fase 0 + Port #1–#4 + mismatch AI +
viste Verifica/Quadro economico + estrazione avanzata nel flusso di assegnazione).

La logica di dominio è già compilata e coperta da **576 test xUnit verdi** su
`QtoRevitPlugin.Core`. Questa checklist valida ciò che i test non possono coprire: il layer
**WPF/Revit** (non compilabile fuori da Windows) e il comportamento end-to-end nel modello reale.

Legenda esito: ☐ da fare · ✅ ok · ⚠️ anomalia (annotare) · ⛔ bloccante.

---

## 0. Prerequisiti

| # | Azione | Risultato atteso | Esito |
|---|---|---|---|
| 0.1 | Aggiornare il branch e aprire la soluzione in Visual Studio | `git pull` su `claude/revit-cme-addin-repo-gu71y9`, `QtoRevitPlugin.sln` apre 3 progetti | ☐ |
| 0.2 | Verificare i percorsi Revit in `Directory.Build.props.user` | `RevitDir2025` punta all'installazione reale (RevitAPI/RevitAPIUI presenti) | ☐ |
| 0.3 | Avere un modello `.rvt` di prova con muri stratificati, pilastri, e almeno una fase | Elementi con `HOST_AREA_COMPUTED`/`HOST_VOLUME_COMPUTED` valorizzati | ☐ |
| 0.4 | Avere un listino importato (DCF/Excel) con codici usati nel modello | Voci EP ricercabili nel Listino | ☐ |

---

## 1. Compilazione (primo cancello)

Questo è il gate che qui non ho potuto attraversare: WPF è Windows-only. Compilare **prima** di
qualunque test funzionale.

| # | Azione | Risultato atteso | Esito |
|---|---|---|---|
| 1.1 | Build `QtoRevitPlugin.Core` (netstandard2.0) | 0 errori (già verificato in CI locale) | ☐ |
| 1.2 | Build `QtoRevitPlugin` target `net8.0-windows` (Revit 2025) | 0 errori | ☐ |
| 1.3 | Build `QtoRevitPlugin` target `net48` (Revit 2022–2024), se usato | 0 errori | ☐ |
| 1.4 | Deploy dell'add-in e avvio Revit 2025.4 | Scheda **CME** nel ribbon, pulsante "Avvia CME" apre il DockablePane | ☐ |

**File WPF nuovi/modificati da tenere d'occhio se la build segnala errori** (mandami il testo esatto):
`SelectionViewModel.cs`, `SelectionView.xaml`, `SessionManager.cs`, `HealthViewModel.cs`,
`ExportWizardViewModel.cs`, `VerificaView(.xaml/.cs)` + `VerificaViewModel.cs`,
`QuadroEconomicoView(.xaml/.cs)` + `QuadroEconomicoViewModel.cs`, `LayerComputoScanner.cs`,
`CategoryBackfillRunner.cs`, `BoolToVisibilityConverter.cs` (nuovo `InverseBoolConverter`).

---

## 2. Fase 0 — Riconciliazione dei modelli dati

Obiettivo: assegnando dalla nuova Selezione, **KPI, Export e Health** leggono lo stesso binario
(`ComputoDocument`/`MeasurementRow`), non più zero.

| # | Azione | Risultato atteso | Esito |
|---|---|---|---|
| 2.1 | Nuovo computo `.cme`; Selezione → seleziona alcuni muri; scegli una voce nel Listino; **Assegna** | Banner verde "✓ N elementi assegnati"; la **Redazione CME** mostra la voce con le misure | ☐ |
| 2.2 | Tornare in **Home** e leggere i KPI header (elementi, importo) | KPI **diversi da zero**, coerenti con quanto assegnato (prima restavano a 0) | ☐ |
| 2.3 | Aprire **Esporta** → wizard → formato **XPWE** → esporta; riaprire il file in PriMus | Il computo contiene le voci assegnate dalla Selezione (prima l'export era vuoto) | ☐ |
| 2.4 | Export **Excel** e **PDF** | Righe e subtotali coerenti con la Redazione CME | ☐ |
| 2.5 | Aprire **Health** → "Esegui controllo" | Analizza le voci del modello Computi (nessun "0 assegnazioni" se hai assegnato) | ☐ |

> Nota: se KPI/Export restano a zero dopo un'assegnazione, è il sintomo storico dello stallo — segnalarlo come ⛔.

---

## 3. Verifica pre-consegna (Port #3)

Sostituisce lo stub della vecchia vista Verifica.

| # | Azione | Risultato atteso | Esito |
|---|---|---|---|
| 3.1 | Aprire **Verifica** → "Esegui verifica" (senza impostare percentuali) | Elenco **classi**: Coerenza interna / Coerenza UM = ESEGUITA; Percentuali/Completezza/Doppio conteggio/Riconciliazione = **N/A con motivo** | ☐ |
| 3.2 | Creare volutamente una voce con quantità 0 (o senza codice) e rieseguire | Rilievo `non_positive_quantity` / `voce_without_code` nella tabella, riga evidenziata | ☐ |
| 3.3 | Impostare **Magg. %** = 40 e **IVA %** = 15, rieseguire | Compaiono avvisi `markup_out_of_typical_band` e `vat_out_of_standard_set` (solo Warning, non bloccanti) | ☐ |
| 3.4 | Impostare Magg. = 24,3 e IVA = 22, rieseguire | Classe Percentuali senza rilievi | ☐ |
| 3.5 | Stesso codice EP con due UM diverse su due voci | Rilievo `unit_inconsistent` (ERRORE), contatore Errori = 1 | ☐ |
| 3.6 | Computo pulito | Empty state "pronto per la consegna" | ☐ |

---

## 4. Quadro economico (Port #1 + #2)

| # | Azione | Risultato atteso | Esito |
|---|---|---|---|
| 4.1 | Aprire **Quadro economico** → "Calcola" (IVA default 22, Magg. vuota) | Scala: Costo diretto → Imponibile (= diretto) → IVA → **TOTALE** valorizzato; riga Maggiorazione "nessuna maggiorazione" | ☐ |
| 4.2 | Impostare **Magg. %** = 24,3 → Calcola | Imponibile = diretto × 1,243; TOTALE ricalcolato; formato € con virgola decimale | ☐ |
| 4.3 | Svuotare **IVA %** → Calcola | Riga IVA "non calcolabile (IVA non impostata)"; TOTALE = "—"; card **Avvisi** segnala Livello 4 non pronto | ☐ |
| 4.4 | Con listino che usa codici CAM (es. prezzario regionale `..CAM..`) | Card CAM: importo voci CAM e **quota % sul totale**; voci non classificabili contate | ☐ |
| 4.5 | Con listino privo di marcatore CAM nel codice | Quota CAM "tutte le voci classificate"/0, **non** un falso "non-CAM" | ☐ |
| 4.6 | Con voci che portano `IncMDO` | Card manodopera: incidenza totale, **% sul totale lavori**, copertura "N/M voci con IncMDO" | ☐ |
| 4.7 | Voce con prezzo unitario mancante | Card **Avvisi**: "voce/i senza prezzo unitario risolto" | ☐ |

---

## 5. Estrazione avanzata — strati (Port #4)

Prerequisito di configurazione: i **materiali** degli strati devono portare i parametri il cui nome
è impostato nella card Assegna (default `Codice Prezzo`, `UM Voce`, `Densita`). Vedi §8.

| # | Azione | Risultato atteso | Esito |
|---|---|---|---|
| 5.1 | Selezione → spuntare **"Estrazione avanzata (strati)"** | Il ComboBox "Quantità per istanza" si **disabilita** | ☐ |
| 5.2 | Selezionare un muro stratificato con almeno 2 strati prezzati → **Assegna** | Banner "✓ N voci · M misure"; in Redazione CME **una voce per codice materiale**, non una per la voce EP | ☐ |
| 5.3 | Verificare le quantità di uno strato a **volume** (mc) | Quantità ≈ area faccia × spessore (m), non ~2× (niente `GetMaterialArea`) | ☐ |
| 5.4 | Strato a **peso** (kg) su un materiale **senza** parametro densità | Voce emessa ma **flaggata** "da completare a mano" (Quantità 0, nota) | ☐ |
| 5.5 | Elemento con solo materiali **senza** `Codice Prezzo` | Contato come "senza strati (percorso diretto)" nel messaggio, non assegnato per strati | ☐ |
| 5.6 | Codice materiale non presente in listino né UserLibrary | Riportato in "codici non trovati (…)", **non** assegnato (nessuna quantità inventata) | ☐ |
| 5.7 | Assegnare gli stessi due muri due volte allo stesso codice | Le misure si **sommano** sotto la stessa voce (identità per IDVV) | ☐ |

---

## 6. Voci derivate — armatura / casseforme (Port #4)

Prerequisito: file **`QTO_DerivedRules.json`** configurato (vedi §8). Senza regole, la funzione è
un no-op (comportamento voluto, H7).

| # | Azione | Risultato atteso | Esito |
|---|---|---|---|
| 6.1 | In estrazione avanzata, spuntare **"+ voci derivate"** | Checkbox abilitata solo se "Estrazione avanzata" è attiva | ☐ |
| 6.2 | Selezionare pilastri con regola armatura (coeff. da parametro) → Assegna | Voce derivata "armatura" aggiunta con quantità = volume × coeff.; messaggio "N derivate" | ☐ |
| 6.3 | Pilastro **senza** il parametro coefficiente | Voce derivata **flaggata** "da completare a mano" (mai coefficiente inventato) | ☐ |
| 6.4 | Selezionare **insieme** pilastri **e** le loro armature modellate (categoria anti-doppio) | Derivata armatura **soppressa**; messaggio "N derivate soppresse (anti-doppio)" | ☐ |
| 6.5 | Regola casseforme con `OverestimateBias` | Voce derivata con nota di sovrastima ("verificare") | ☐ |
| 6.6 | Verificare che la derivata **si somma** alla riga base (UM diversa), non la sostituisce | Coesistono voce base (strato/diretta) e voce derivata | ☐ |

> ⚠️ Punto critico locale-dipendente: `AntiDoubleCategory` nel JSON deve combaciare **esattamente** con il nome categoria Revit come appare nel tuo Revit italiano (es. "Armature strutturali"). Se non combacia, il gate non scatta (test 6.4 fallisce).

---

## 7. Mismatch semantico AI + backfill categoria/famiglia (v13)

| # | Azione | Risultato atteso | Esito |
|---|---|---|---|
| 7.1 | Con AI (Ollama) attiva, assegnare e poi Health → "Esegui controllo" | Sezione "Mismatch semantici (AI)" popolata (usa categoria+famiglia per elemento) | ☐ |
| 7.2 | Aprire un `.cme` **creato prima** di questa build (schema v12) | All'apertura, backfill **silenzioso** delle sotto-righe legacy (log `CategoryBackfill: N/M`) | ☐ |
| 7.3 | Dopo il backfill, rieseguire Health su quel computo | Il mismatch semantico ora ha il contesto (non più degradato) | ☐ |
| 7.4 | Backfill idempotente: riaprire lo stesso `.cme` | Nessun ri-lavoro (log 0 aggiornate o task non schedulato) | ☐ |

---

## 8. Configurazione richiesta (da impostare per la messa in esercizio)

| Voce | Dove | Valore da impostare | Esito |
|---|---|---|---|
| Parametri Material (strati) | Card Assegna EP (proprietà VM) | Nomi reali dei parametri condivisi sul Material per **codice prezzo**, **UM voce**, **densità** | ☐ |
| Regole derivate | `%AppData%\CmePlugin\QTO_DerivedRules.json` (globale) o accanto al `.cme` (locale, prevale) | Categoria (nome Revit) → codice, UM, base, coefficiente (fisso o parametro), `AntiDoubleCategory` (nome Revit) | ☐ |
| Aliquota IVA / maggiorazione | Toolbar Verifica e Quadro economico | Coerenti col progetto (IVA 4/10/22; SG+utile tipico 13–27%) | ☐ |

> Per generare un `QTO_DerivedRules.json` di esempio da adattare: `DerivedRulesService.WriteExampleIfMissing()`
> (armatura kg/mc su pilastri con gate anti-doppio, casseforme mq con bias di sovrastima).

---

## 9. Migrazione schema v13 (verifica di non-regressione dati)

| # | Azione | Risultato atteso | Esito |
|---|---|---|---|
| 9.1 | Aprire un `.cme` v12 esistente | Migrazione automatica a v13 (colonne `Category`/`FamilyName` su `MeasurementSubRows`); nessuna perdita dati | ☐ |
| 9.2 | Salvare e riaprire | `SchemaInfo` a versione 13, computo integro | ☐ |
| 9.3 | Creare un nuovo `.cme` | Nasce direttamente a v13 | ☐ |

---

## Annotazioni

Riportare qui le anomalie (numero riga checklist, comportamento osservato, screenshot/log). I file
di log utili: `AssignEpLogger` (assegnazione, backfill, estrazione), `CrashLogger` (eccezioni).
