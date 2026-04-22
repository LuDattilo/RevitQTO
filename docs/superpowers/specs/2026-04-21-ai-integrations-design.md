# Revit QTO Plugin — AI Integrations Design Spec
**Data:** 2026-04-21  
**Autore:** Luigi Dattilo / GPA Ingegneria Srl  
**Stato:** Draft — approvato per implementazione

---

## 1. Contesto e obiettivo

Il plugin Revit QTO (descritto in `Revit-QTO-Plugin-Doc.md`) copre il workflow base di Quantity Take-Off per appalti pubblici italiani. Questo documento specifica le integrazioni AI e le estensioni funzionali emerse dalla sessione di brainstorming del 2026-04-21.

**Obiettivo:** rendere il plugin più automatizzato e guidato, mantenendo il QTO deterministico, tracciabile e formalmente valido (DM 312/2021).

**Principio fondamentale:** le funzionalità AI sono **addizionali e non sostitutive**. Il plugin funziona completamente senza MCP server. L'AI potenzia le fasi di mapping e validazione quando disponibile.

---

## 2. Architettura — MCP Server embedded in C#

### 2.1 Scelta architetturale

Il plugin espone un **MCP Server embedded** (C#, SDK `ModelContextProtocol` NuGet) avviato all'interno dello stesso processo Revit. Claude Desktop e Claude Code CLI si connettono via stdio/SSE come client MCP.

Nessun processo companion separato. Nessuna dipendenza esterna oltre all'SDK MCP.

### 2.2 Stack layers

```
Claude Desktop / Claude Code CLI
        ↕  MCP Protocol (stdio / SSE)
MCP Server Layer  [C# — nel plugin, opzionale]
  Resources: revit://model/family-types, price-list, untagged, health, mappings
  Tools (read):  get_family_types, search_price_list, run_health_check, get_qto_summary
  Tools (write ⚠): apply_mapping_batch, tag_elements_by_family,
                   create_qto_filters, create_filter_by_code, export_qto
        ↕  ExternalEvent (thread-safe)
Revit Plugin Core  [C# esistente — invariato]
  Core Engine: Parser / QuantityExtractor / RoomExtractor / HealthCheck / Export
  Data Layer:  Extensible Storage / Shared Parameters
        ↕  Revit API
Revit Model (.rvt)
```

### 2.3 Human Approval Gate

Tutti i tools MCP di scrittura (⚠) non agiscono direttamente sul modello. Il flusso è:

```
Claude prepara l'azione
    → UI WPF mostra dialog con anteprima (quanti elementi, quali codici)
    → Utente clicca Conferma
    → ExternalEvent esegue la scrittura nel modello
```

Nessuna scrittura automatica. Il modello è sempre sotto controllo umano.

### 2.4 Ciclo di vita MCP nel ribbon

Il ribbon Revit aggiunge un gruppo "AI":
- **Toggle "MCP Server"** — avvia/ferma il server su `localhost`. Stato persistente in `user.config`.
- **Label stato** — "AI Connessa" (verde) / "In attesa" (grigio) / "Off" (disabilitato)
- **Pulsante "Configura"** — porta, log chiamate MCP
- **Pulsante "Copia config Claude"** — copia negli appunti il JSON per `claude_desktop_config.json`

---

## 3. Tre sorgenti di quantità (estensione al design originale)

Il documento tecnico originale prevedeva una sola sorgente (famiglie Revit). Questa spec aggiunge due sorgenti ulteriori per coprire le lavorazioni non modellate.

### 3.1 Sorgente A — Famiglie Revit (esistente)

Mapping: `Family Type → Voce listino + parametro geometrico (Area / Volume / Lunghezza / Conteggio)`

Estrazione via `FilteredElementCollector` + `ElementMultiCategoryFilter`. Richiede che l'elemento abbia `QTO_Codice` assegnato (via tagging).

### 3.2 Sorgente B — Room/Space con formula (NUOVA)

Mapping: `Voce listino → Formula su Room`

Variabili disponibili nella formula (NCalc):
| Variabile | Fonte Revit | Note |
|---|---|---|
| `PERIMETER` | `ROOM_PERIMETER` (BuiltInParameter) | Perimetro lordo — sottrarre porte manualmente |
| `AREA` | `ROOM_AREA` (BuiltInParameter) | Area netta configurabile (Wall Finish / Wall Center) |
| `HEIGHT` | Parametro configurabile per progetto | `Room.Height` NON esiste nell'API — va impostato dall'utente |
| `DOOR_WIDTH_SUM` | Somma `Width` di `room.Doors` | Per detrarre zoccolini |
| `DOOR_AREA_SUM` | Somma `Width × Height` di `room.Doors` | Per detrarre tinteggiature |
| `WINDOW_AREA_SUM` | Somma `Width × Height` di `room.Windows` | Per detrarre tinteggiature |

**Vincolo critico:** `Room.Height` non è un BuiltInParameter in Revit. Il plugin deve prevedere un parametro di progetto configurabile (es. "QTO_AltezzaLocale") o un'altezza fissa impostata nel SetupView.

**Esempi di formule standard italiane:**
```
Zoccolini (m):        PERIMETER - DOOR_WIDTH_SUM
Tinteggiatura (m²):   (PERIMETER - DOOR_WIDTH_SUM) * HEIGHT - WINDOW_AREA_SUM
Pavimentazione (m²):  AREA
Controsoffitto (m²):  AREA
Intonaco pareti (m²): PERIMETER * HEIGHT - DOOR_AREA_SUM - WINDOW_AREA_SUM
```

**Filtraggio Room:** solo Room con `Area > 0` (esclude "Not Placed" e "Not Enclosed"). Room con area = 0 vengono segnalate nell'Health Check come "Locali non bounded".

**Rooms vs Spaces:** il plugin gestisce `OST_Rooms` (architettura). Il supporto `OST_Spaces` (MEP) è rinviato a una versione successiva.

### 3.3 Sorgente C — Voci manuali (NUOVA)

Per lavorazioni non modellabili e non derivabili dal modello (es. ponteggi a corpo, smaltimento, opere provvisionali). L'utente inserisce direttamente la quantità nella UI. La voce entra nel calcolo e nell'export con flag `"sorgente: manuale"`.

---

## 4. Struttura gerarchica PriMus e formato XPWE

### 4.1 Gerarchia del listino

PriMus usa una struttura a 4 livelli:
```
SuperCapitolo → Capitolo → Sottocapitolo → Voce elementare
```

Il formato codice è `FAMIGLIA.CAPITOLO.VOCE.ARTICOLO` (es. `A.02.003.a`). La struttura varia per prezzario regionale ma segue le Linee Guida MIMS 2022.

### 4.2 Vincolo di export

Il plugin deve **preservare la gerarchia** dal momento del caricamento del listino. L'export non deve appiattire le voci. Se una voce è in `Capitolo A → Sottocapitolo A.02 → Voce A.02.003`, deve uscire nell'XPWE con quella struttura.

### 4.3 Formato di export

| Formato | Scopo | Note |
|---|---|---|
| **XPWE** (priorità) | Import diretto in PriMus con gerarchia | Standard XML aperto, supportato da tutti i software italiani di computo |
| **.xlsx** | Revisione interna, archivio | ClosedXML, formattazione professionale |
| **.tsv** | Compatibilità altri software | Testo tabulato |

Il DCF nativo PriMus è **binario proprietario non documentato** — non va generato.

### 4.4 Struttura XPWE da generare

```xml
<PrimusXML version="2.0">
  <SuperCapitolo codice="A" descrizione="OPERE STRUTTURALI">
    <Capitolo codice="A.01" descrizione="Scavi e movimenti di terra">
      <Voce codice="A.01.001" um="mc" prezzoUnitario="12.50"
            quantita="45.20" importo="565.00">
        Scavo a sezione obbligata fino a 2m
        <Misure>
          <ElementId>123456</ElementId>
          <ElementId>789012</ElementId>
        </Misure>
      </Voce>
    </Capitolo>
  </SuperCapitolo>
</PrimusXML>
```

---

## 5. Flusso di lavoro completo (5 fasi)

```
FASE 1 · Setup listino
  01. Carica listino (.dcf/.xlsx/.csv) → struttura gerarchica letta e preservata
  ⚠  La gerarchia Cap/SottoCap viene mantenuta in memoria per l'export XPWE

FASE 2 · Mapping (tre tipi)
  02a. Family Type → Voce + parametro geometrico  [mapping famiglie]
  02b. Voce → Formula su Room                     [mapping locali — NUOVO]
  02c. Voce → Quantità manuale                    [voci non modellate — NUOVO]
  [AI] Claude suggerisce mapping via MCP → utente conferma

FASE 3 · Estrazione quantità
  03a. FilteredElementCollector per Family Types → Area/Volume/Lungh./Conteggio
  03b. FilteredElementCollector per Room (Area>0) → formula NCalc applicata
  ⚠  Room.Height non disponibile API → altezza da parametro di progetto configurabile
  03c. Voci manuali passate direttamente al calcolo

FASE 4 · Calcolo
  04. Aggregazione quantità per codice voce (tutte e tre le sorgenti)
  05. Qtà × Prezzo unitario (opzionale: formula NCalc con %sicurezza)
  [AI] Claude segnala anomalie in linguaggio naturale

FASE 5 · Verifica & Export
  06. Health Check: codici mancanti · quantità zero · codici non in listino ·
      locali non bounded · categorie non mappate
  Loop: se errori → torna a tagging/mapping → rilancia check
  07. Export: XPWE (PriMus) + .xlsx + .tsv
      Ogni riga porta ElementId per audit trail
```

---

## 6. UI — principi e modifiche alle view esistenti

### 6.1 Principi

- Un'azione principale visibile per schermata
- I suggerimenti AI appaiono come pre-selezione discreta (dropdown pre-compilato + nota in piccolo), non come badge separati
- Colori solo per stato funzionale: ✓ verde = completato, rosso = errore/mancante
- Stile visivo: WPF nativo Revit (sfondo grigio chiaro, controlli standard, titolbar blu Revit)
- La conferma batch è sempre in fondo, non in cima

### 6.2 MappingView — modifiche

- Riga per ogni Family Type con dropdown voce listino + selezione parametro geometrico
- **Nuovo tab "Locali"**: stessa struttura ma con campo formula NCalc + variabili disponibili
- **Nuovo tab "Voci manuali"**: tabella voce / quantità / U.M.
- Se MCP attivo: dropdown pre-compilato dall'AI con nota discreta "Suggerita dall'AI"
- Pulsante batch "Conferma tutti i suggerimenti" in fondo alla lista

### 6.3 TaggingView — modifiche

- Invariata per tagging manuale elemento per elemento
- Aggiunto: pulsante "Assegna a tutta la famiglia (N elem.)" — visibile **solo** quando la selezione è omogenea (stesso Family Type)
- Click sul pulsante batch → dialog anteprima con lista elementi e codice → Conferma → ExternalEvent

### 6.4 SetupView — modifiche

- Aggiunto campo "Altezza locali di riferimento" (configurazione per Room formula)
- Aggiunto sezione "MCP Server" con toggle, stato, pulsante "Copia config Claude"

### 6.5 Filtri Revit — nuovo pannello

Accessibile dal ribbon con un click. Crea filtri di vista nativi Revit:
- `QTO_Taggati` — verde — criterio: `QTO_Codice` non vuoto
- `QTO_Mancanti` — rosso — criterio: `QTO_Codice` vuoto
- `QTO_Anomalie` — arancio — criterio: codice non presente nel listino caricato
- Filtro per codice specifico (es. tutti gli elementi `B.01.011`) — inserimento manuale del codice

I filtri sono filtri Revit nativi, editabili da Viste → Filtri. Vengono proposti al primo avvio su un nuovo documento.

---

## 7. MCP Tools catalog

### Resources (read-only)
| URI | Contenuto |
|---|---|
| `revit://model/family-types` | Family Types nel modello: nome, categoria, param. geometrici disponibili, conteggio istanze |
| `revit://model/price-list` | Voci listino con gerarchia: codice, descrizione, U.M., prezzo, Cap/SottoCap |
| `revit://model/untagged-elements` | Elementi senza QTO_Codice, raggruppati per Family Type |
| `revit://model/mappings` | Mapping configurati (famiglie + locali + manuali) |
| `revit://model/health` | Ultimo report Health Check |

### Tools di lettura
| Tool | Descrizione |
|---|---|
| `search_price_list(query)` | Ricerca semantica nel listino |
| `get_elements_by_family(family_type_id)` | Lista istanze con ElementId e quantità |
| `run_health_check()` | Esegue le regole di validazione |
| `get_qto_summary()` | Totali per codice voce: qtà, importo, % completamento |
| `get_view_filters()` | Filtri di vista attivi nel documento |

### Tools di scrittura (⚠ Human Approval Gate)
| Tool | Azione |
|---|---|
| `apply_mapping_batch(mappings[])` | Salva mapping Family Type → codice in Extensible Storage |
| `tag_elements_by_family(family_type_id, qto_code)` | Scrive QTO_Codice su tutte le istanze del tipo |
| `create_qto_filters()` | Crea i 3 filtri standard (Taggati/Mancanti/Anomalie) |
| `create_filter_by_code(code, color)` | Crea filtro per codice specifico |
| `navigate_to_element(element_id)` | Seleziona e zooma sull'elemento in Revit |
| `export_qto(format, path)` | Genera XPWE / xlsx / tsv |

---

## 8. Nuovi componenti da sviluppare

Rispetto al documento tecnico originale, i componenti aggiuntivi sono:

| Componente | Percorso | Descrizione |
|---|---|---|
| `RoomExtractor.cs` | `Core/Extraction/` | Estrae quantità da Room/Space con formula NCalc |
| `RoomMappingConfig.cs` | `Models/` | Configurazione formula per voce derivata da locale |
| `ManualQuantityEntry.cs` | `Models/` | Voce a quantità manuale |
| `XpweExporter.cs` | `Core/Export/` | Genera file XPWE con gerarchia Cap/SottoCap |
| `QtoMcpServer.cs` | `MCP/` | MCP server embedded, gestione lifecycle |
| `McpToolHandlers.cs` | `MCP/` | Implementazione tools read/write |
| `McpApprovalHandler.cs` | `ExternalEvents/` | ExternalEvent per azioni MCP con gate UI |
| `FilterManager.cs` | `Core/` | Crea e gestisce filtri di vista Revit |
| `RoomMappingView.xaml` | `UI/Views/` | Tab "Locali" nel MappingView |
| `ManualEntryView.xaml` | `UI/Views/` | Tab "Voci manuali" nel MappingView |
| `McpStatusControl.xaml` | `UI/` | Controllo ribbon per stato MCP |

---

## 9. Rischi aggiuntivi

| Rischio | Probabilità | Impatto | Mitigazione |
|---|---|---|---|
| `Room.Height` non disponibile come BuiltInParameter | Certa (documentata) | Alta | Parametro configurabile per progetto nel SetupView; fallback a valore fisso (2.70m) |
| Room "Not Enclosed" in modelli non finiti | Alta | Media | Filtro `Area > 0`; Health Check segnala le room non bounded |
| Struttura XML XPWE non documentata pubblicamente da ACCA | Alta | Alta | Analisi empirica di file XPWE esportati da PriMus su prezzari reali prima di implementare il writer |
| Thread collision MCP server + Revit API | Media | Alta | Tutti i write via ExternalEvent; MCP server su thread separato |
| Prezzari regionali con struttura gerarchica diversa | Media | Media | Parser gerarchico flessibile; validazione struttura al caricamento |

---

## 10. Aggiornamento sprint planning

Agli sprint originali (Sprint 0–6) si aggiungono:

**Sprint 2 esteso** — Aggiungere `RoomExtractor` + `RoomMappingConfig` + tab "Locali" nel MappingView (+3 giorni)

**Sprint 5 esteso** — Aggiungere `XpweExporter` con gerarchia + voci manuali + `FilterManager` (+3 giorni)

**Sprint 7 (nuovo)** — MCP Server: `QtoMcpServer`, tools catalog, approval gate UI, ribbon status control, configurazione Claude Desktop (2 settimane / ~8 giorni)

**Stima totale aggiornata:** ~17 settimane (da 14)

---

## 11. Riferimenti aggiuntivi

- ACCA Forum DCF: https://forum.acca.it
- The Building Coder — Room API: https://jeremytammik.github.io/tbc/a/
- Linee Guida Prezzari Regionali MIMS 2022: https://www.mit.gov.it/nfsmitgov/files/media/notizia/2022-08/Allegato%20A%20LG%20PREZZARI%20REGIONALI.pdf
- ModelContextProtocol NuGet (SDK C#): https://www.nuget.org/packages/ModelContextProtocol
- LeenO — XPWE format: https://leeno.org/vi-racconto-di-un-xpwe_export/
