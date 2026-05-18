# Test manuali — RevitCortex `Verbosity`

> Obiettivo: misurare cosa cambia nelle risposte dei tool MCP di RevitCortex al variare del setting `Verbosity` in `~/.revitcortex/settings.json`.

## Setup

- Setting file: `%USERPROFILE%\.revitcortex\settings.json`
- Campo: `"Verbosity"`
- Valori supportati: `Minimal`, `Standard`, `Full` (3 livelli, confermati dall'utente)
- Valore corrente al momento del test: `Minimal`
- Audit log: `%USERPROFILE%\.revitcortex\audit.jsonl` (campo `response_bytes` = lunghezza risposta in byte)

### Procedura per cambiare livello

1. Chiudere Revit (il server MCP è caricato in-process — il setting si rilegge solo al restart).
2. Editare `settings.json` e salvare il nuovo valore di `Verbosity`.
3. Riaprire Revit con il progetto di test (Snowdon Towers sample, fase New Construction).
4. Aprire questa chat / un client MCP e verificare con `say_hello` che il server risponda.
5. Eseguire la batteria di tool sotto e annotare `response_bytes` dall'`audit.jsonl`.

### Modello da usare

- **File**: `~\Documents\Snowdon Towers Sample.rvt`
- **Vista attiva**: una FloorPlan con elementi visibili (NON una Sheet, altrimenti `color_elements` e `get_current_view_elements` falliscono).
- **Selezione iniziale**: nessuna.

---

## Batteria di tool (uguale per tutti i livelli)

Eseguire **in ordine** e nelle **stesse condizioni** (stesso file, stessa vista, stessa selezione) per ogni livello di verbosity. Annotare `response_bytes` dell'`audit.jsonl` e — quando rilevante — l'aspetto qualitativo della risposta.

| # | Tool | Argomenti | Cosa osservare |
|---|------|-----------|----------------|
| 1 | `say_hello` | nessuno | Baseline minima. `response_bytes` deve essere ~62 a `Minimal`. |
| 2 | `get_project_info` | nessuno | Quante sezioni/campi tornano (levels, phases, isWorkshared, units…). |
| 3 | `get_current_view_info` | nessuno | Dettaglio sulla view attiva (dettaglio range, scala, template…). |
| 4 | `get_current_view_elements` | nessuno | Numero di elementi vs solo conteggio aggregato. |
| 5 | `analyze_model_statistics` | nessuno | Profondità delle statistiche (per categoria? per workset? warnings?). |
| 6 | `get_warnings` | nessuno | Se a `Minimal` torna solo il count e a `Detailed` la lista completa. |
| 7 | `ai_element_filter` | `{"data":{"filterCategory":"OST_StructuralFraming","includeInstances":true,"maxElements":5}}` | Verbosità del payload per ogni elemento. |
| 8 | `get_element_parameters` | un singolo `elementId` di un Muro | A `Minimal` solo nome+valore? A `Detailed` anche storage type, isShared, GUID? |
| 9 | `check_model_health` | nessuno | A `Diagnostic` dovrebbe stampare anche stack/timing interni. |

---

## Tabella di raccolta dati

Compilare una riga per livello, una colonna per tool. Se il tool fallisce o cambia formato, annotarlo.

| Tool \ Verbosity | Minimal | Standard | Full |
|------------------|---------|--------|------|
| `say_hello` (bytes / ms) | 112 / 6 | 112 / 45 | 112 / 50 |
| `get_project_info` (bytes / ms) | 1.510 / 65 | 1.510 / 8 | 1.510 / 11 |
| `get_current_view_info` (bytes / ms) | 192 / 6 (Cover) | 185 / 2 (L1) | 185 / 2 (L1) |
| `get_current_view_elements` (bytes / ms) | 5.210 / 34 (14 elem) | 28.124 / 17 (50 elem) | 28.124 / 15 (50 elem) |
| `analyze_model_statistics` (bytes / ms) | 2.150 / 1.063 | 2.150 / 793 | 2.150 / 918 |
| `get_warnings` (bytes / ms) | 12.473 / 54 | 12.473 / 3 | 12.473 / 2 |
| `ai_element_filter` OST_StructuralFraming (bytes / ms) | 116 / 53 | 116 / 21 | 116 / 29 |
| `ai_element_filter` OST_Walls max=1 (bytes / ms) | 707 / 32 | 708 / 15 | 708 / 14 |
| `get_element_parameters` 1 muro (bytes / ms) | 13.243 / 64 | 13.243 / 3 | 13.243 / 3 |
| `check_model_health` (bytes / ms) | 472 / 155 | 472 / 63 | 472 / 74 |

### Osservazioni baseline `Minimal` (2026-04-26)

- **Vista attiva era una `DrawingSheet` (`Cover`)**: per i prossimi giri attivare una FloorPlan con elementi modello visibili, altrimenti `get_current_view_elements` riporta solo cartiglio/testi/help-buttons (rumore).
- **Modello in uso**: `Snowdon Towers Sample Architectural` (workshared, fasi: Legends/Existing/New Construction). Nessun elemento `OST_StructuralFraming` → usare `OST_Walls` (1.132 istanze) come categoria di test.
- **Risposte già molto verbose anche a `Minimal`**:
  - `get_element_parameters` torna **TUTTI** i parametri (75 sul muro testato), inclusi: `storageType`, `isShared`, `isReadOnly`, `hasValue`, `groupName` (nome qualificato `autodesk.parameter.group:...`). 13 KB per 1 elemento.
  - `get_warnings` torna **descrizione completa** + tutti gli `failingElementIds` per ogni warning. 12 KB per 49 warnings.
  - `analyze_model_statistics` con `compact: true` (default) torna comunque top-20 categorie + breakdown per tutti i 18 livelli.
- **Ipotesi forte**: `Verbosity: Minimal` nel `settings.json` **NON sta filtrando granché** sul lato risposta MCP — sembra controllare al massimo la verbosità dei log interni. Da verificare ai livelli successivi se la differenza esiste davvero.
- **Performance**: `analyze_model_statistics` è il più costoso (1 sec). Gli altri sotto i 100 ms.

### Osservazioni `Standard` (2026-04-26)

- **Vista attiva**: `L1` (FloorPlan, scale 1:96, Coarse) con 1.470 elementi totali, 499 nel filtro.
- **Risultato netto**: a input identico, **byte di risposta identici a `Minimal`** per 8 tool su 9. L'unica differenza è in `get_current_view_elements` (5.210 → 28.124) ma è dovuta SOLO al cambio di vista (Cover/Sheet 14 elem → FloorPlan 50 elem), non al setting verbosity. `get_current_view_info` cambia di 7 byte per lo stesso motivo (Cover vs L1).
- **Schema dati identico**: `get_element_parameters` sullo stesso muro `619340` torna **byte-per-byte la stessa risposta** (13.243). Nessun campo aggiuntivo, stessa profondità di metadati.
- **Warnings, project info, model health**: risposte identiche bit-per-bit.
- **Performance**: i `duration_ms` calano molto al secondo giro (`get_warnings` 54→3, `get_element_parameters` 64→3, `check_model_health` 155→63). Caching interno del server, non legato a verbosity.

**Conclusione provvisoria**: `Verbosity: Minimal` ↔ `Standard` **NON modifica il payload MCP**. Probabilmente controlla solo il logging su console/file del server. Da confermare osservando se a `Detailed`/`Diagnostic` la situazione cambia.

### Osservazioni `Full` (2026-04-26)

- **Vista attiva**: `L1` (FloorPlan) — stessa di `Standard`.
- **Risultato**: byte di risposta **IDENTICI** a `Standard` per tutti e 10 i tool. Nessun campo aggiunto, nessun metadato extra, nessuna nuova sezione.
  - `get_element_parameters` muro `619340` → 13.243 byte (uguale)
  - `get_current_view_elements` → 28.124 byte (uguale)
  - `get_warnings` → 12.473 byte (uguale)
  - tutti gli altri → uguali
- **Performance**: variazioni di `duration_ms` minime e dominate da rumore/caching (es. `say_hello` 45→50 ms, `analyze_model_statistics` 793→918 ms). Nessuna correlazione con il livello.

---

## Conclusione finale (2026-04-26)

**Il setting `Verbosity` (`Minimal` / `Standard` / `Full`) NON ha alcun effetto osservabile sul payload MCP restituito dai tool RevitCortex.**

Evidenza:
- 10 tool testati con input identico ai 3 livelli → 10/10 risposte byte-per-byte identiche tra `Standard` e `Full`; tra `Minimal` e `Standard` differenze solo dove la vista attiva è cambiata (Cover/Sheet → L1/FloorPlan).
- Nessun campo aggiuntivo, nessun metadato (`timing`, `internalIds`, stacktrace) compare a livelli più alti.
- Nessuna riga aggiuntiva in `~/.revitcortex/logs/` durante i test.
- `duration_ms` variano per caching del server, non per il livello.

**Implicazioni pratiche:**
- Il consumo di token verso il modello LLM **non cambia** modificando questo setting.
- Per ridurre i byte/token consumati, l'unica leva attuale sono i **parametri specifici dei tool** (es. `compact: true` su `analyze_model_statistics`, `maxElements`, `fields` su `get_current_view_elements`, `compact: true` + `includeTypeParameters: false` su `get_element_parameters`), non il setting globale `Verbosity`.
- Il setting è probabilmente legato al solo logging interno del server (file/console), non al payload MCP. Per conferma definitiva andrebbe ispezionato il sorgente di `RevitCortex.Server.dll` (oggi non disponibile pubblicamente).

**Raccomandazione default**: lasciare `Verbosity: "Standard"` (default ragionevole) — non porta benefici né penalità rispetto a `Minimal`. Se in futuro RevitCortex documenta un comportamento diverso, ripetere questa batteria.

---

## Verifica dal codice sorgente (decompilazione, 2026-04-26)

Decompilato `RevitCortex.Server.dll`, `RevitCortex.Plugin.dll`, `RevitCortex.Tools.dll`, `RevitCortex.Core.dll` con `ilspycmd 10.0.0`.

**Risultato: zero occorrenze di `Verbosity` (case-insensitive) in tutte e 4 le DLL.**

La classe modello del file di settings (`CortexSettings` in `RevitCortex.Plugin.decompiled.cs`, riga 852) ha **solo 4 proprietà**:

```csharp
internal class CortexSettings
{
    public int Port { get; set; } = 8080;
    public string? LogLevel { get; set; } = "Info";
    public string? Model { get; set; } = "claude-sonnet-4-6";
    public bool ReadOnlyMode { get; set; }
}
```

Il deserializer Newtonsoft.Json (`JsonConvert.DeserializeObject<CortexSettings>`) **ignora silenziosamente** tutti i campi extra presenti nel file. Quindi i seguenti campi presenti nel `settings.json` locale sono **morti**:

- `Verbosity` (qualunque valore: `Minimal`, `Standard`, `Full`, …)
- `EnableCodeExecution`
- `SupportReportKeepCount`

Il server lato standalone (`RevitCortex.Server.dll`, metodo `ResolvePort`) legge dal `settings.json` SOLO il campo `Port`. Tutto il resto viene ignorato anche lato server.

**Conclusione definitiva**: `Verbosity` non è "non funziona" — proprio **non esiste** nel codice di RevitCortex v1.0.0. È un campo fantasma probabilmente residuato da una versione precedente o da un'edizione manuale. Cambiare il suo valore non ha mai effetto.

**Azione consigliata**: rimuovere dal `settings.json` locale i 3 campi morti (`Verbosity`, `EnableCodeExecution`, `SupportReportKeepCount`) per tenere il file allineato a quello che il binario effettivamente legge:

```json
{
  "Port": 8080,
  "LogLevel": "Info",
  "Model": "claude-sonnet-4-6",
  "ReadOnlyMode": false
}
```

---

## Cosa osservare oltre ai byte

- **Campi extra**: a livelli più alti compaiono nuovi campi (es. `metadata`, `timing`, `internalIds`)? Annotare quali.
- **Verbosità testuale**: `description` o `message` diventano più lunghi/discorsivi?
- **Stacktrace ed errori**: provocare un errore (es. `get_element_parameters` con `elementIds: [-1]`). A `Diagnostic` dovrebbe esserci uno stack interno; a `Minimal` solo un messaggio breve.
- **Performance**: `duration_ms` nell'`audit.jsonl` cambia in modo rilevante? (Atteso: marginale, ma a `Diagnostic` può crescere se il server serializza più dati.)
- **Log su disco**: `~/.revitcortex/logs/` riceve nuove righe? (Attualmente la dir è quasi vuota — solo `token-usage.jsonl` di aprile).
- **Token usage**: `~/.revitcortex/usage-mcp.db` registra prompt/response token diversi? (Verificabile via SQLite.)

---

## Test specifici per livello

### `Minimal` (baseline attuale)

- [ ] `say_hello` → `response_bytes` ≈ 62.
- [ ] `get_warnings` → conteggio aggregato, no lista dettagliata.
- [ ] Errore controllato → messaggio breve, **niente** stack.

### `Standard`

- [ ] Risposte ~10–30% più lunghe rispetto a `Minimal`.
- [ ] Compaiono campi descrittivi (`description`, `category`, `level`) dove a `Minimal` c'erano solo ID.

### `Full`

- [ ] `get_element_parameters` espone più metadati di `Standard` (es. `definitionType`, gruppo qualificato esteso).
- [ ] `analyze_model_statistics` con `compact: true` torna comunque solo top-20, oppure compaiono tutte le categorie?
- [ ] `get_warnings` rimane uguale (è già completo a `Minimal`) o cresce?
- [ ] `get_current_view_elements` aggiunge campi extra per ogni elemento (es. `boundingBox`, `parameters` inline)?
- [ ] `~/.revitcortex/logs/` riceve nuove righe?
- [ ] Possibile aumento di `duration_ms` osservabile in `audit.jsonl`?

---

## Criteri di accettazione

1. **Monotonia**: per ogni tool della batteria, `response_bytes(Minimal) ≤ Normal ≤ Detailed ≤ Diagnostic`. Eccezioni vanno documentate.
2. **Nessuna regressione di correttezza**: cambiando verbosity i tool tornano gli stessi `result: "ok"` per gli stessi input. Nessun tool deve fallire **solo** perché si è alzato il livello.
3. **Restart effettivo**: confermare che modificare `settings.json` **senza** restart NON cambia il comportamento (il server è in-process). Serve a documentare il workflow corretto.
4. **Default consigliato**: a fine test scegliere il livello che dà il miglior compromesso byte/utilità per l'uso quotidiano (oggi `Minimal`, da rivedere).

---

## Snippet utili

### Estrarre `response_bytes` per uno specifico tool dall'`audit.jsonl`

```bash
# bash
grep '"tool":"get_project_info"' ~/.revitcortex/audit.jsonl | tail -1 | python -c "import sys, json; d=json.loads(sys.stdin.read()); print(d['response_bytes'], d['duration_ms'])"
```

```powershell
# PowerShell
Get-Content "$env:USERPROFILE\.revitcortex\audit.jsonl" | Where-Object { $_ -match '"tool":"get_project_info"' } | Select-Object -Last 1 | ConvertFrom-Json | Select-Object response_bytes, duration_ms
```

### Backup del settings.json prima di iniziare

```powershell
Copy-Item "$env:USERPROFILE\.revitcortex\settings.json" "$env:USERPROFILE\.revitcortex\settings.backup.json"
```

---

## Note

- I 4 livelli (`Minimal`/`Normal`/`Detailed`/`Diagnostic`) sono **inferiti** dal binario `RevitCortex.Server.dll` (stringhe presenti). Se il server al restart logga "verbosity X non valida" → aggiornare la lista.
- Il `LogLevel` (`Info`) nel `settings.json` è separato da `Verbosity`: il primo controlla i log su file, il secondo controlla la verbosità delle **risposte MCP**. Vanno testati indipendentemente.
- Prima di pubblicare risultati, ripristinare il livello che si vuole tenere come default (es. `Minimal`).
