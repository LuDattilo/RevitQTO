# QTO Plugin – Specifiche Implementazioni Aggiuntive v2.0
### Sessione di lavoro: 21 aprile 2026
### Riferimento: QTO-Plugin-Documentazione-v3.md

Questo documento è il **registro completo** di tutte le funzionalità aggiuntive definite nella sessione del 21/04/2026. Ogni sezione è autonoma e riferisce il capitolo corrispondente nella documentazione tecnica principale.

---

## I1. Scrittura Bidirezionale dei Parametri

**Requisito**: dopo la selezione degli elementi e l'assegnazione alla voce EP, il plug-in scrive due valori su ogni elemento Revit selezionato — codice EP e descrizione breve.

### Shared Parameters creati automaticamente

| Parametro | Tipo | Contenuto |
|---|---|---|
| `QTO_Codice` | Text | Codice voce EP (ultima/primaria) |
| `QTO_DescrizioneBreve` | Text | Descrizione sintetica della voce |
| `QTO_Stato` | Text | *(vuoto)* / `COMPUTATO` / `ESCLUSO` / `NP` / `PARZIALE` |

Il `SharedParameterManager` crea automaticamente il file `.txt` e fa il binding su tutte le categorie mappate in fase di setup. La scrittura avviene in una singola transazione per tutti gli elementi selezionati.

### Nota implementativa

Il parametro `QTO_Codice` contiene sempre **l'ultima o la primaria** assegnazione, per compatibilità con le schedule Revit standard. La lista completa multi-EP è nell'Extensible Storage (vedi I2).

---

## I2. Assegnazioni Multiple per Elemento (Multi-EP)

**Requisito**: un elemento Revit può essere assegnato a più voci EP (es. muro → muratura + intonaco + isolamento). Ogni assegnazione è visibile nel pannello di controllo e non sovrascrive le precedenti.

### Strategia di persistenza duale

- **Shared Parameters** → ultima assegnazione (schedule Revit compatibili)
- **Extensible Storage sull'elemento** → lista completa serializzata JSON

```csharp
// Schema ES con campo lista
builder.AddArrayField("QtoAssignments", typeof(string));
// Ogni entry JSON: {"Code":"A.02","Desc":"...","Qty":12.5,"Unit":"m²","UnitPrice":85.0}
```

### Comportamento nella TaggingView

Quando si selezionano elementi già computati, il pannello mostra automaticamente tutte le assegnazioni esistenti:

```
Assegnazioni esistenti – Muro Portante 30cm (ID 112345):
┌──────────────┬─────────────────────────────┬────────┬──────┐
│ Codice EP    │ Descrizione Breve           │ Q.tà   │ U.M. │
├──────────────┼─────────────────────────────┼────────┼──────┤
│ A.02.001     │ Muratura in mattoni pieni   │ 45,2   │ m²   │
│ B.01.003     │ Intonaco civile interno     │ 38,7   │ m²   │
└──────────────┴─────────────────────────────┴────────┴──────┘
```

Una nuova assegnazione viene **aggiunta** alla lista (non sovrascrive), salvo selezione esplicita "Sostituisci".

---

## I3. FilterBuilder con Filtri Stile Revit + Ricerca Inline

**Requisito**: la selezione degli elementi deve replicare il sistema filtri nativi di Revit, con in più un campo di ricerca testuale applicato direttamente al parametro scelto.

### UI SelectionView

```
[Categoria ▼]  [Parametro ▼]  [Operatore ▼]  [Valore: ______🔍]
[+ Aggiungi regola]   [Cerca nel filtro: ___________]
────────────────────────────────────────────────────────────
Risultati: 47 elementi trovati
[☑] Muro Portante 30cm – ID 112345  │ ● COMPUTATO: A.02.001
[☑] Muro Portante 30cm – ID 112346  │ ● COMPUTATO: A.02.001, B.01.003
[☑] Muro Portante 30cm – ID 112348  │ ○ non computato
────────────────────────────────────────────────────────────
[Seleziona Tutto] [Deseleziona]
[Isola]  [Nascondi]  [Togli Isolamento]  │  [➤ INSERISCI]
```

Il parametro disponibile nel dropdown `[Parametro ▼]` è popolato con `ParameterFilterUtilities.GetFilterableParametersInCommon(doc, categoryIds)`.

### Comandi finali selezione

| Comando | API Revit | Comportamento |
|---|---|---|
| **Seleziona** | `uidoc.Selection.SetElementIds(ids)` | Evidenzia nel modello |
| **Isola** | `view.IsolateElementsTemporary(ids)` | Isola nella vista attiva |
| **Nascondi** | `view.HideElements(ids)` | Nasconde elementi |
| **Togli Isolamento** | `view.DisableTemporaryViewMode(...)` | Ripristina vista |
| **➤ INSERISCI** | `externalEvent.Raise()` | Scrive parametri + calcola + salva DB |

---

## I4. Esclusione Elementi dal Computo

### Esclusione manuale

Checkbox nella ResultGrid. Gli elementi deselezionati ricevono `IsExcluded = true` nel DB e `QTO_Stato = "ESCLUSO"` come Shared Parameter. Compaiono in grigio con badge `[ESCLUSO]`.

### Esclusione per regola globale (SetupView)

Sezione "Regole di Esclusione" nel setup:

```
● Parametro [Commenti ▼]  contiene  ["ESISTENTE"]    [🗑]
● Parametro [QTO_Stato ▼]  è uguale a  ["NP"]        [🗑]
● Parametro [Fase ▼]  è uguale a  ["Demolizioni"]    [🗑]
```

Le regole globali vengono applicate come pre-filtro prima della ResultGrid e salvate nell'ES del `.rvt`.

---

## I5. Pannello Health Check – Verifica Computazione

**Requisito**: filtri di verifica per controllare lo stato di computazione di tutti gli elementi del modello, con navigazione diretta all'elemento critico.

### Stati e filtri

| Icona | Stato | Condizione | Colore |
|---|---|---|---|
| ✅ | Computato | `QtoAssignments.Count > 0` | Verde |
| ⚠ | Parziale | EP assegnato, `Quantity = 0` | Giallo |
| ❌ | Non computato | `QtoAssignments` vuoto | Rosso |
| 🔄 | Multi-EP | `QtoAssignments.Count > 1` | Blu |
| 🚫 | Escluso manuale | `IsExcluded = true` | Grigio |
| 🚫 | Escluso filtro | `QTO_Stato = "NP"` | Grigio chiaro |

Il pulsante "Mostra nel modello" applica `OverrideGraphicSettings` con i colori corrispondenti a tutti gli elementi della vista attiva. Doppio click su una riga → `NavigateToElementHandler` → selezione + zoom sull'elemento in Revit.

---

## I6. Salvataggio e Richiamo Regole di Selezione (Preset Filtri)

**Requisito**: poter nominare, salvare e richiamare le regole di filtro della SelectionView, per riutilizzarle su elementi simili.

### Serializzazione JSON

```json
{
  "RuleName": "Muri Esterni – Contrassegno A",
  "Category": "OST_Walls",
  "PhaseId": 123456,
  "PhaseStatus": "New",
  "Rules": [
    { "Parameter": "FUNCTION_PARAM", "Evaluator": "Equals", "Value": "Exterior" },
    { "Parameter": "QTO_Contrassegno", "Evaluator": "Contains", "Value": "A" }
  ],
  "InlineSearchParam": "ALL_MODEL_MARK"
}
```

### Doppio layer di persistenza

1. **File JSON locale** (`%AppData%\QtoPlugin\rules\`) — portabile tra macchine
2. **DataStorage ES nel `.rvt`** — condiviso col team su modello workshared

La `SetupView` espone un dropdown "Regole salvate" da cui caricare un preset e applicarlo immediatamente.

---

## I7. Gestione Multi-Listino

**Requisito**: caricare e gestire simultaneamente più prezzari, con ricerca unificata e gestione dei conflitti di codice.

### Priorità conflitti

La colonna `Priority` nella tabella `PriceLists` determina quale listino vince in caso di codice duplicato. Gestito tramite `ORDER BY pl.Priority ASC` nella query FTS5.

### Ricerca a tre livelli

| Livello | Meccanismo | Soglia |
|---|---|---|
| 1 – Codice esatto | `LIKE 'B.01%'` | Sempre |
| 2 – FTS5 | `PriceItems_FTS MATCH` | Query normale |
| 3 – Fuzzy | Levenshtein .NET | Solo se FTS5 < 3 risultati |

### Navigazione per capitoli

Albero laterale `Chapter / SubChapter` come alternativa alla ricerca testuale, per chi preferisce sfogliare il prezzario linearmente.

---

## I8. Nuovi Prezzi (NP)

**Requisito**: creare voci di prezzo per lavorazioni non presenti nell'EP contrattuale, con analisi prezzi strutturata e workflow di approvazione.

### Riferimenti normativi

- **Art. 5, comma 7, All. II.14 – D.Lgs. 36/2023**: criteri di determinazione dei NP in ordine di preferenza (ragguaglio → prezzario regionale → analisi prezzi)
- **Art. 120 – D.Lgs. 36/2023**: varianti in corso d'opera
- **Parere MIT n. 3545/2025**: i NP non sono soggetti automaticamente al ribasso d'asta (campo `RibassoAsta` opzionale)

### Formula analisi prezzi

```
CT = Manodopera + Materiali + Noli + Trasporti
NP = CT × (1 + SpGenerali/100) × (1 + UtileImpresa/100) × (1 − RibassoAsta/100)
```

Spese generali: 13–17% | Utile impresa: 10% (D.Lgs. 36/2023 All. II.14)

### Workflow stati

```
Bozza ──► Concordato ──► Approvato (RUP)
             ↑
   contraddittorio DL / Impresa
```

Le voci con stato `Concordato` o `Approvato` vengono inserite nel DB come `PriceItems` con `IsNP = 1` e diventano ricercabili esattamente come qualsiasi voce del prezzario.

### Export NP

- Nel computo Excel: voci NP evidenziate con colore distinto
- Foglio separato "Analisi Nuovi Prezzi" con dettaglio componenti CT
- Documento pronto per allegato alla perizia di variante (art. 120 D.Lgs. 36/2023)

---

## I9. Filtro Iniziale per Fase Revit

**Requisito**: il primo filtro operativo deve essere la fase Revit, distinguendo nuova costruzione, demolizioni ed esistente.

### UI PhaseFilterView (primo passo obbligatorio)

```
FASE DI LAVORO
☑ Nuova costruzione  → Phase Created    = [Progetto ▼]
☑ Demolizioni        → Phase Demolished = [Progetto ▼]
☐ Esistente (solo visualizzazione, escluso dal computo)

Fasi disponibili: [Esistente]  [Demolizioni]  [Progetto]  [Futuro]
```

### Implementazione

```csharp
// Nuova costruzione
new ElementPhaseStatusFilter(phaseId, ElementOnPhaseStatus.New)
// Demolizioni
new ElementPhaseStatusFilter(phaseId, ElementOnPhaseStatus.Demolished)
```

### Mappatura Fase → Listino

| Fase Revit | Tipo | Comportamento nel listino |
|---|---|---|
| Progetto | Nuova costruzione | Ricerca su tutti i capitoli |
| Demolizioni | Demolizioni | Ricerca aperta sul capitolo Demolizioni |
| Esistente | Solo visualizzazione | Escluso automaticamente |

---

## I10. Rilevamento Elementi Aggiunti Post-Computazione

**Requisito**: quando il modello `.rvt` viene modificato dopo una prima sessione, rilevare automaticamente gli elementi aggiunti o rimossi rispetto a quanto già computato.

### Meccanismo di confronto

Il `ModelDiffService` confronta gli `UniqueId` presenti nel DB (già processati) con quelli rilevati dal `FilteredElementCollector` al rientro in sessione.

### Dialog di notifica

```
⚠ Modifiche al modello rilevate
Dall'ultima sessione (21/04/2026 17:32):

+ 14 elementi aggiunti (non ancora computati)
   → 8 x Muro Portante 30cm   (OST_Walls)
   → 4 x Solaio Tipo A        (OST_Floors)
   → 2 x Porta Interna        (OST_Doors)

- 3 elementi rimossi (erano computati)
   → 3 x Colonna C30/37  (importo annullato: € 1.240,00)

[Mostra nuovi elementi]  [Ignora]  [Esporta delta report]
```

**"Mostra nuovi elementi"**: isola nella vista 3D solo gli elementi con `ChangeType = 'Added'` e li carica nella SelectionView pre-filtrata, pronti per il tagging.

Il flag `Resolved = 1` nel `ModelDiffLog` viene impostato quando l'elemento viene computato o esplicitamente escluso, pulendo la lista dei pending.

### Color coding dedicato

| Colore | Stato |
|---|---|
| Arancione `(255,140,0)` | **Aggiunto dopo la prima computazione** |
| Verde `(100,200,100)` | Computato |
| Rosso `(220,80,80)` | Non computato |
| Giallo `(255,200,0)` | Parziale |
| Blu `(80,130,220)` | Multi-EP |
| Grigio/halftone | Escluso |

### Schema DB – tabelle coinvolte

```sql
-- Aggiornamento in Sessions
ALTER TABLE Sessions ADD COLUMN ModelSnapshotDate DATETIME;

-- Log modifiche
CREATE TABLE ModelDiffLog (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId       INTEGER REFERENCES Sessions(Id),
    UniqueId        TEXT NOT NULL,
    ChangeType      TEXT NOT NULL,  -- Added|Removed|Modified
    Category        TEXT,
    FamilyName      TEXT,
    PhaseCreated    TEXT,
    PhaseDemolished TEXT,
    DetectedAt      DATETIME DEFAULT CURRENT_TIMESTAMP,
    Resolved        INTEGER DEFAULT 0
);
```

---

## I11. View Filters Nativi Revit (FilterManager)

**Requisito**: creare e mantenere tre `ParameterFilterElement` persistenti nel modello — `QTO_Taggati`, `QTO_Mancanti`, `QTO_Anomalie` — applicabili con un clic a una vista, a un template o a un set di viste selezionate. Sostituiscono l'approccio per-element di `SetElementOverrides` con una soluzione durevole che sopravvive al riavvio di Revit e alla chiusura del plug-in.

### Rule engine basato su QTO_Stato

I filtri leggono il Shared Parameter `QTO_Stato` introdotto in §I1 (valori: *vuoto* / `COMPUTATO` / `ESCLUSO` / `NP` / `PARZIALE`). La mappatura filtro → regola è deterministica:

| Filtro | Regola su `QTO_Stato` | Override grafico |
|---|---|---|
| `QTO_Taggati` | `Equals "COMPUTATO"` OR `Equals "PARZIALE"` | Colore solido verde `(100,200,100)` |
| `QTO_Mancanti` | `Equals ""` (vuoto) OR `HasNoValue` | Colore solido rosso `(220,80,80)` |
| `QTO_Anomalie` | `Equals "NP"` OR `Equals "ESCLUSO"` | Halftone + pattern obliquo grigio |

### Creazione idempotente

```csharp
public ElementId EnsureFilter(Document doc, string filterName,
                              List<ElementId> categoryIds,
                              ElementFilter elementFilter)
{
    var existing = new FilteredElementCollector(doc)
        .OfClass(typeof(ParameterFilterElement))
        .Cast<ParameterFilterElement>()
        .FirstOrDefault(f => f.Name == filterName);
    if (existing != null)
    {
        existing.SetCategories(categoryIds);
        existing.SetElementFilter(elementFilter);
        return existing.Id;        // aggiornamento senza duplicare
    }
    return ParameterFilterElement.Create(
        doc, filterName, categoryIds, elementFilter).Id;
}

// Rule builder per QTO_Taggati (esempio)
var statoId   = SharedParameterManager.GetQtoStatoId(doc);      // ElementId del SP
var rComputato = ParameterFilterRuleFactory.CreateEqualsRule(statoId, "COMPUTATO");
var rParziale  = ParameterFilterRuleFactory.CreateEqualsRule(statoId, "PARZIALE");
var orFilter = new LogicalOrFilter(new List<ElementFilter> {
    new ElementParameterFilter(rComputato),
    new ElementParameterFilter(rParziale)
});
```

### Applicazione alla vista

Tre modalità esposte in `FilterManagerView`:

| Modalità | API Revit | Scope |
|---|---|---|
| Vista corrente | `view.AddFilter(pfeId)` + `view.SetFilterOverrides(pfeId, ogs)` | Solo `doc.ActiveView` |
| Template corrente | `template.AddFilter(pfeId)` | Tutte le viste che ereditano dal template |
| Set viste selezionate | Loop su `viewIds` selezionati in TreeView | Piani + Prospetti + Sezioni filtrati |

### Transazione atomica con TransactionGroup

L'intera sequenza (binding Shared Params + creazione filtri + applicazione override) è wrappata in un `TransactionGroup` per permettere un undo singolo all'utente.

```csharp
using var txGroup = new TransactionGroup(doc, "QTO Filter Manager");
txGroup.Start();
try
{
    EnsureSharedParametersBound(doc);    // §I1
    var idT = EnsureFilter(doc, "QTO_Taggati",  cats, orTaggati);
    var idM = EnsureFilter(doc, "QTO_Mancanti", cats, orMancanti);
    var idA = EnsureFilter(doc, "QTO_Anomalie", cats, orAnomalie);
    ApplyFiltersToViews(doc, targetViews, idT, idM, idA);
    txGroup.Assimilate();                 // singolo punto undo
}
catch
{
    txGroup.RollBack();
    throw;
}
```

### Legenda auto-generata (opzionale)

Checkbox "Aggiungi legenda" nella dialog. Il plug-in crea una `Legend View` nominata `QTO_Legend` con:
- 3 `FilledRegion` campione (verde / rosso / halftone) + `TextNote` didascalici
- Inserita automaticamente in tutti i fogli con una vista target, ancorata in basso a destra (offset XYZ configurabile)

### Rapporto con OverrideColorHandler

L'`OverrideColorHandler` descritto in Revit-QTO-Plugin-Doc.md §6.3 resta come **modalità "Preview temporanea"** (utile prima di committare i filtri persistenti). Dopo l'applicazione dei filtri nativi, la preview viene azzerata: il plug-in mantiene una `HashSet<ElementId>` di sessione con gli id precedentemente sovrascritti e chiama `view.SetElementOverrides(id, new OverrideGraphicSettings())` su ciascuno.

### Pulizia / Rimozione

Pulsante "Rimuovi filtri QTO" esegue:
```csharp
foreach (var name in new[] { "QTO_Taggati", "QTO_Mancanti", "QTO_Anomalie" })
{
    var pfe = FindByName(doc, name);
    if (pfe != null) doc.Delete(pfe.Id);   // cascade: rimosso anche dalle viste
}
```

### Validazione API con RevitCortex (riferimento implementativo)

Le API utilizzate sono coerenti con il wrapper MCP di **RevitCortex v1.0.13** (`C:\Users\luigi.dattilo\OneDrive - GPA Ingegneria Srl\Documenti\RevitCortex\distribution\RevitCortex-v1.0.13`), che fornisce una controparte ad alto livello per la stessa famiglia di operazioni:

| API Revit bassa (usata nel plug-in) | Tool MCP RevitCortex (validato) | Note di allineamento |
|---|---|---|
| `ParameterFilterElement.Create(doc, name, categoryIds, elementFilter)` | `create_view_filter(name, categories[], parameterName, rule, value)` | RC accetta `rule ∈ {equals, notEquals, greater, less, contains, startsWith, endsWith}` → matching diretto con `ParameterFilterRuleFactory.CreateEqualsRule/CreateContainsRule/…` |
| `view.AddFilter(pfeId) + view.SetFilterOverrides(pfeId, ogs)` | *incluso in* `create_view_filter` *+* `override_graphics(elementIds, R, G, B, isHalftone, transparency)` | Stessa architettura: separazione filtro ↔ override grafico |
| `template.AddFilter(pfeId)` + propagazione | `apply_view_template(action ∈ {list,apply,remove}, templateId, viewIds)` | La modalità "Template corrente" di §I11 è equivalente a `action=apply` |
| `view.SetElementOverrides(id, ogs)` | `override_graphics(elementIds, ..., viewId)` | `viewId=0` = vista attiva (nostra convenzione analoga) |
| `FilteredElementCollector` + `OfCategory` | `ai_element_filter(filterCategory=OST_*)` | Stesso pattern; RC espone direttamente la stringa BuiltInCategory |
| `LookupParameter(name)` su Room | `filter_by_parameter_value(category, parameterName, value)` | Conferma scelta name-based vs GUID (§I12 `EvaluateRoomFormula`) |

**Decisione architetturale derivata**: il plug-in espone anche una modalità "Preview rapida" che internamente potrebbe usare RevitCortex come MCP backend (se installato) per evitare di reimplementare low-level. Questa è un'opzione Sprint 10+ da valutare; per Sprint 5 si mantiene l'implementazione diretta Revit API per avere zero dipendenze runtime.

---

## I12. Sorgente B – Quantità da Rooms/Spaces con NCalc

**Requisito**: estrarre quantità direttamente da Rooms (architettonici) e MEP Spaces mediante formule NCalc personalizzate, per voci EP che non si agganciano a famiglie host (es. finiture pavimentali per stanza, tinteggiature per superficie, oneri di pulizia a m²).

### Categorie aggiuntive

Estensione della tabella di §Fase 3 (QTO-Plugin-Documentazione-v3.md):

| Categoria | Parametri BuiltIn verificati | Testo parametro (name-based) |
|---|---|---|
| `OST_Rooms` | `ROOM_AREA`, `ROOM_VOLUME`, `ROOM_PERIMETER`, `ROOM_HEIGHT`, `ROOM_UPPER_LEVEL`, `ROOM_LOWER_OFFSET`, `ROOM_UPPER_OFFSET` | `"Area"`, `"Volume"`, `"Perimeter"`, `"Unbounded Height"` |
| `OST_MEPSpaces` | eredita `ROOM_*` da `SpatialElement` + `ROOM_COMPUTATION_HEIGHT` | `"Area"`, `"Volume"`, `"Computation Height"` |

> **Nota API**: l'area "finitura pavimento" non è un BuiltInParameter distinto. Il valore di `ROOM_AREA` riflette il setting globale `AreaVolumeSettings.BoundaryLocation` (`AtWallCenter` / `AtWallFinish` / `AtWallCore` / `AtWallCoreCenter`). Per coerenza d'impresa, il plug-in forza il setting a `AtWallFinish` in SetupView se non già configurato. Parametri finiture (`ROOM_FINISH_FLOOR`, `ROOM_FINISH_BASE`, `ROOM_FINISH_WALL`, `ROOM_FINISH_CEILING`) sono di tipo `Text` e contengono nomi materiali, non aree.

### Raccolta con filtri non-placed + ridondanti + non-enclosed

`SpatialElement` espone tre patologie: Room non posizionati (mai inseriti), ridondanti (stesso identificativo su più posizioni) e non-enclosed (senza perimetro chiuso). Il check canonico è **un solo predicato**: `Area > 0` implica posizionamento + perimetro chiuso + non-ridondanza.

```csharp
public List<Room> GetValidRooms(Document doc, ElementId phaseId)
{
    return new FilteredElementCollector(doc)
        .OfCategory(BuiltInCategory.OST_Rooms)
        .WherePasses(new ElementPhaseStatusFilter(phaseId, ElementOnPhaseStatus.New))
        .Cast<Autodesk.Revit.DB.Architecture.Room>()
        .Where(r => r.Area > 0)   // filtra non-placed, ridondanti e non-enclosed in un colpo solo
        .ToList();
}
```

### Editor formule NCalc con auto-discovery parametri

La `FormulaEditorView` espone:
- Dropdown categoria target (Rooms / Spaces)
- Dropdown parametri disponibili (popolato dinamicamente leggendo il primo Room valido)
- Editor testuale con validazione sintassi NCalc in tempo reale
- Pulsante "Test" che valuta su un Room campione e mostra il risultato

Esempi di formule (usando nomi visibili dei parametri, compatibili con le API di alto livello tipo `create_view_filter` di RevitCortex):

| Voce EP | Formula | Note |
|---|---|---|
| Pavimento gres | `Area` | Area Room = superficie pavimento con `BoundaryLocation = AtWallFinish` |
| Pavimento con sfrido | `Area * 1.08` | 8% sfrido |
| Tinteggiatura pareti | `Perimeter * H_Controsoffitto` | Shared param `H_Controsoffitto` per altezza utile |
| Battiscopa | `Perimeter - LargTotAperture` | Shared param `LargTotAperture` somma delle larghezze porte |
| Controsoffitto | `Area` | Stessa area del pavimento (per Room con soffitto piano) |
| Oneri pulizia fine lavori | `Area * 2.5` | €/m² forfettario |
| Volume demolizione | `Volume` | Per demolizioni computate a m³ |

### Binding variabili NCalc → parametri Revit (custom handler)

NCalc supporta la risoluzione custom di identificatori tramite `EvaluateParameter`. Il plug-in inietta un handler che fa il lookup sul Room corrente:

```csharp
public double EvaluateRoomFormula(Room room, string formulaText)
{
    var expr = new Expression(formulaText);
    expr.EvaluateParameter += (name, args) =>
    {
        // Pattern name-based (analogo a come RevitCortex risolve parameterName
        // in create_view_filter / filter_by_parameter_value).
        // LookupParameter copre built-in (nome UI localizzato), shared e project parameters.
        var p = room.LookupParameter(name);
        if (p != null && p.StorageType == StorageType.Double)
        {
            args.Result = UnitUtils.ConvertFromInternalUnits(
                p.AsDouble(), GetDisplayUnit(p));
            return;
        }
        // Fallback BuiltInParameter per chiavi di progetto che usano enum-names
        if (Enum.TryParse<BuiltInParameter>(name, out var bip))
        {
            var bp = room.get_Parameter(bip);
            if (bp != null && bp.StorageType == StorageType.Double)
            {
                args.Result = UnitUtils.ConvertFromInternalUnits(
                    bp.AsDouble(), GetDisplayUnit(bp));
                return;
            }
        }
        throw new FormulaException($"Parametro '{name}' non trovato sul Room");
    };
    return Convert.ToDouble(expr.Evaluate());
}
```

> **Nota localizzazione**: `LookupParameter` in Revit italiano accetta nomi localizzati (`"Area"`, `"Volume"`, `"Perimetro"`, `"Altezza illimitata"`). Per garantire portabilità cross-locale, il plug-in mantiene un dizionario `ParameterNameResolver` che mappa i nomi "canonici" (`Area`, `Volume`, `Perimeter`) sui nomi localizzati dell'installazione corrente, risolto via `Category.GetBuiltInParameter()` + `LabelUtils.GetLabelFor(BuiltInParameter)` in `OnStartup`.

### Persistenza formula sulla voce EP

Ogni assegnazione Sorgente B salva in `QtoAssignments`:
- `Quantity` = risultato valutato della formula (in unità SI)
- `Formula` = espressione sorgente (tracciabilità audit)
- `SourceType` = `RoomSpace`

```sql
ALTER TABLE QtoAssignments ADD COLUMN SourceType TEXT DEFAULT 'FamilyHost';
ALTER TABLE QtoAssignments ADD COLUMN Formula TEXT;
-- Valori SourceType: FamilyHost | RoomSpace | Manual
```

### Re-evaluation automatica

Quando un parametro di un Room cambia (es. spostamento muro ⇒ `ROOM_AREA` varia), `ModelDiffService` marca le `QtoAssignments` con `SourceType = 'RoomSpace'` e `UniqueId = room.UniqueId` come `ChangeType = 'Modified'`, proponendo ricalcolo automatico della formula all'apertura della sessione.

### Shared Parameter di supporto

`H_Controsoffitto` (Double, `SpecTypeId.Length`, bindato su `OST_Rooms`) è il parametro custom tipico che l'utente può aggiungere a mano in SetupView. Il plug-in automatizza la creazione tramite la sezione "Parametri custom Room" (tab dedicato di SetupView).

---

## I13. Sorgente C – Voci Manuali Svincolate dal Modello

**Requisito**: computare voci EP che non hanno corrispondenza diretta con elementi Revit — oneri di sicurezza, trasporti, noli, mano d'opera oraria, pulizie a corpo, forniture franco cantiere. Queste voci devono confluire nel totale di sessione e nell'export mantenendo tracciabilità documentale.

### Schema DB – nuova tabella `ManualItems`

```sql
CREATE TABLE ManualItems (
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId      INTEGER REFERENCES Sessions(Id),
    EpCode         TEXT NOT NULL,
    EpDescription  TEXT,
    Quantity       REAL NOT NULL,
    Unit           TEXT,
    UnitPrice      REAL,
    Total          REAL,
    Notes          TEXT,              -- es. "Da verbale riunione 12/03/2026"
    AttachmentPath TEXT,              -- path a documento giustificativo (PDF/DOC)
    CreatedBy      TEXT,
    CreatedAt      DATETIME DEFAULT CURRENT_TIMESTAMP,
    ModifiedAt     DATETIME,
    IsDeleted      INTEGER DEFAULT 0  -- soft delete per audit trail
);
```

**Nessun campo `UniqueId` / `ElementId`**: le voci manuali sono orfane per design. Il `SourceType = 'Manual'` serve solo nel report unificato di export.

### UI – Tab "Voci Manuali" in TaggingView

```
VOCI MANUALI — Sessione: Computo Lotto A
┌─────────────┬────────────────────────────┬──────┬──────┬─────────┬──────┐
│ Codice EP   │ Descrizione                │ Q.tà │ U.M. │  Prezzo │ Tot. │
├─────────────┼────────────────────────────┼──────┼──────┼─────────┼──────┤
│ OS.001      │ Oneri sicurezza COVID      │ 1,00 │ cad  │ 1.200,0 │ 1200 │
│ TR.042      │ Trasporto a discarica      │ 5,00 │ m³   │   85,0  │  425 │
│ MD.010      │ Manodopera extra lavori    │ 8,00 │ h    │   32,5  │  260 │
└─────────────┴────────────────────────────┴──────┴──────┴─────────┴──────┘

[+ Nuova voce]  [Import CSV]  [📎 Allega giustificativo]  [Export]
```

Ogni riga è editabile inline. Il codice EP è picker con FTS5 su `PriceItems` (sia listino principale che NP). Se l'utente inserisce un codice non presente, viene proposta la creazione di un NP via `NpEngine` (§I8).

### Merge multi-sorgente nel totalizzatore

Una stessa voce EP può ricevere quantità contemporaneamente da sorgenti A, B, C. La logica di aggregazione è **sommativa senza priorità** (l'utente può marcare manualmente una riga come "override" per disabilitare la somma):

```sql
-- Totale per voce EP su tutte le sorgenti
SELECT
    qa.EpCode,
    SUM(qa.Quantity)               AS QtyFromModel,      -- A + B
    COALESCE(SUM(mi.Quantity), 0)  AS QtyFromManual,     -- C
    SUM(qa.Quantity) + COALESCE(SUM(mi.Quantity), 0) AS QtyTotal,
    AVG(qa.UnitPrice)              AS UnitPrice,
    SUM(qa.Total) + COALESCE(SUM(mi.Total), 0)       AS Total
FROM QtoAssignments qa
LEFT JOIN ManualItems mi
       ON mi.EpCode = qa.EpCode
      AND mi.SessionId = qa.SessionId
      AND mi.IsDeleted = 0
WHERE qa.SessionId = ?
  AND qa.IsDeleted = 0
  AND qa.IsExcluded = 0
GROUP BY qa.EpCode;
```

**Regola di conflitto prezzo unitario**: se `UnitPrice` differisce fra le righe della stessa EP (prezzo manuale diverso dal listino), il report evidenzia la discrepanza in giallo con tooltip: `"Prezzi non omogenei: 85,00 da listino / 87,50 manuale — verificare."`

### Tracciabilità nell'export

- **Excel**: foglio dedicato "Voci Manuali" con colonna `Giustificativo` che contiene hyperlink al file allegato
- **TSV PriMus**: righe manuali con prefisso `M:` nel campo Note
- **XPWE**: nodo `<ManualItems>` gerarchico allo stesso livello delle voci da modello, preservando la gerarchia di capitoli

### Import da CSV

Formato atteso (separatore `;`, UTF-8, header obbligatorio):
```csv
EpCode;Description;Quantity;Unit;UnitPrice;Notes
OS.001;Oneri sicurezza;1,00;cad;1200,00;Verbale 12/03
TR.042;Trasporto discarica;5,00;m3;85,00;DDT n.142
MD.010;Manodopera extra;8,00;h;32,50;Ordine di servizio n.7
```

Utile per importare da fogli Excel preesistenti o verbali di cantiere senza reimmissione manuale.

---

## I14. Vista 3D Dedicata per QTO

**Requisito**: pulsante ribbon **"🎯 Vista QTO"** che apre (o crea se non esistente) una `View3D` isometrica predefinita, con template e i 3 filtri di §I11 già applicati. È la vista "lensing" per l'analisi visiva del computo, tipicamente aperta sul **secondo monitor** in configurazione flottante per affiancarla alla vista di progettazione.

### Elementi gestiti (set completo)

| Elemento | Nome convenzione | Tipo | Scope |
|---|---|---|---|
| `View3D` | `QTO_3D_View` | 3D isometrica | Singola vista shared, idempotente |
| `ViewPlan` × N livelli | `QTO_Plan_<LevelName>` | Pianta 2D per livello | Generate da `Level.Name` (es. `QTO_Plan_GF`, `QTO_Plan_L1`…) |
| `ViewSchedule` × 3 | `QTO_Schedule_Assegnazioni` · `_Mancanti` · `_NuoviPrezzi` | Schedule native Revit | Aggiornamento live al variare parametri |
| `View` template 3D | `QTO_View_Template` | Template 3D | Applicato alla `View3D` |
| `View` template 2D | `QTO_View_Template_2D` | Template planimetrico | Applicato a tutte le `QTO_Plan_*` |
| Filtri vista | `QTO_Taggati` · `QTO_Mancanti` · `QTO_Anomalie` (§I11) | `ParameterFilterElement` | Ereditati da entrambi i template |

### Contenuti del template `QTO_View_Template`

| Proprietà | Valore | Motivazione |
|---|---|---|
| View Scale | 1:100 | Standard piante/prospetti BIM |
| Detail Level | `Coarse` | Performance su modelli > 20k elementi |
| Visual Style | `Consistent Colors` | I filtri dominano sul materiale |
| Shadows / Ambient | Off | Evita occlusione colori di stato |
| Section Box | Attivo su `PhaseFilter.BoundingBox` | Limita al volume della fase attiva |
| V/G overrides | Categorie QTO piene, resto halftone 50% | Focus visivo sulle categorie computate |
| Filtri applicati | `QTO_Taggati` + `QTO_Mancanti` + `QTO_Anomalie` | Da §I11, applicati in cascata |
| Orientamento | Isometrica SW (default) | Configurabile in SetupView |

### Creazione idempotente

```csharp
public View3D EnsureQtoView(Document doc)
{
    // 1) Riuso vista esistente
    var existing = new FilteredElementCollector(doc)
        .OfClass(typeof(View3D))
        .Cast<View3D>()
        .FirstOrDefault(v => !v.IsTemplate && v.Name == "QTO_3D_View");
    if (existing != null)
    {
        VerifyTemplateIntegrity(doc, existing);   // vedi sotto
        return existing;
    }

    // 2) Creazione: 3D view type + CreateIsometric
    var viewType = new FilteredElementCollector(doc)
        .OfClass(typeof(ViewFamilyType))
        .Cast<ViewFamilyType>()
        .First(vft => vft.ViewFamily == ViewFamily.ThreeDimensional);

    var view = View3D.CreateIsometric(doc, viewType.Id);
    view.Name = "QTO_3D_View";

    // 3) Applica template
    var template = EnsureQtoViewTemplate(doc);   // crea se mancante
    view.ViewTemplateId = template.Id;

    return view;
}
```

### Verifica integrità template (drift detection)

Se l'utente edita manualmente il template (es. disattiva un filtro, rimuove un override), al prossimo click su "Vista QTO" il plug-in rileva il drift e propone il ripristino:

```csharp
private void VerifyTemplateIntegrity(Document doc, View3D view)
{
    var tpl = doc.GetElement(view.ViewTemplateId) as View;
    var expectedFilters = new[] { "QTO_Taggati", "QTO_Mancanti", "QTO_Anomalie" };
    var appliedFilters = tpl.GetFilters()
        .Select(id => (doc.GetElement(id) as ParameterFilterElement)?.Name)
        .ToHashSet();

    if (!expectedFilters.All(f => appliedFilters.Contains(f)))
    {
        // Mostra dialog: "Il template è stato alterato. Ripristinare?"
        // [Ripristina] [Ignora] [Apri senza correzioni]
    }
}
```

### Transazione atomica

L'intera sequenza (ensure template + ensure filters §I11 + ensure view + apply template + activate) è wrappata in un `TransactionGroup` unico:

```csharp
using var txGroup = new TransactionGroup(doc, "Apri Vista QTO");
txGroup.Start();
try
{
    EnsureQtoFilters(doc);                   // §I11
    var tpl = EnsureQtoViewTemplate(doc);
    var view = EnsureQtoView(doc);
    uidoc.ActiveView = view;                 // activate sull'UIDocument
    uidoc.RefreshActiveView();
    txGroup.Assimilate();
}
catch { txGroup.RollBack(); throw; }
```

### UI integrazione

| Dove | Elemento |
|---|---|
| **Ribbon** tab QTO, panel "Verifiche" | Pulsante **🎯 Vista QTO** accanto a "Filtri Vista" e "Health Check" |
| **DockablePane** status bar | Indicatore `[● Vista QTO attiva]` quando `doc.ActiveView.Name == "QTO_3D_View"` |
| **SetupView** tab "Vista QTO" | Configurazione: orientamento isometrico (SW/SE/NW/NE), Detail Level, Section Box auto-fit |

### Multi-monitor workflow (integrazione con pannelli flottanti)

Scenario tipico su due schermi:
- **Monitor primario**: vista di progettazione (piante, sezioni, 3D architettonico)
- **Monitor secondario**: `QTO_3D_View` + DockablePane QTO in stato flottante

Al click del pulsante, se `QTO_3D_View` è già attiva in una finestra di lavoro, Revit porta quella finestra in focus (comportamento nativo di `uidoc.ActiveView = view`). Se non è aperta, la apre come tab nuova nel workspace corrente. L'utente poi la trascina manualmente sul secondo monitor (Revit persiste la posizione tramite `.rvt` → `WindowManager` native).

### Validazione API con RevitCortex

| Codice plug-in | Tool MCP RevitCortex | Note |
|---|---|---|
| `View3D.CreateIsometric(doc, typeId)` | `create_view` con `viewType: ThreeDimensional` | RC accetta stringa, sotto risolve a `ViewFamilyType` |
| `view.ViewTemplateId = id` | `apply_view_template(action=apply, templateId, viewIds)` | Equivalente diretto |
| `uidoc.ActiveView = view` | *(non esposto in RC MCP, gestito da UI Revit)* | Comportamento standard UIDocument |
| `view.GetFilters()` | *(parte di `create_view_filter` query)* | RC gestisce bind filtro ↔ vista in unica chiamata |

### Persistenza su `.rvt`

La vista e il template **vivono nel modello** (non in SQLite), quindi sopravvivono a:
- Perdita del DB locale
- Apertura del `.rvt` da altro PC senza plug-in installato (resta visibile ma non aggiornabile)
- Condivisione via Cloud Worksharing

**Limitazione nota**: se un collega apre il `.rvt` senza plug-in e modifica/cancella la vista, al riavvio della sessione il plug-in rileva l'assenza e propone ricreazione automatica.

### Estensione – Piante 2D per livello (`QTO_Plan_*`)

**Requisito**: oltre alla 3D, generare **una `ViewPlan` per ogni `Level`** del progetto, con template 2D dedicato. Le piante 2D sono cruciali per l'analisi dettagliata per piano, l'impaginazione su fogli e l'uso affiancato della 3D sul secondo monitor.

**Template `QTO_View_Template_2D`** (divergente dal 3D):

| Proprietà | Valore 2D | Motivazione divergenza dal 3D |
|---|---|---|
| View Scale | 1:50 | Standard BIM piante (1:100 troppo piccolo per visualizzare QTO_Codice) |
| Detail Level | `Medium` | Aperture e infissi vanno visti (vs Coarse del 3D) |
| Section Box | ❌ Off | Non applicabile a ViewPlan (ha `View Range` invece) |
| View Range | Cut=1200 mm · Bottom=Level · Top=Level Above | Standard planimetrico |
| Underlay | Off | Evita confusione visiva |
| Filtri QTO | Stessi 3 di §I11 | Coerenza stato cross-view |

Creazione idempotente (analoga al 3D, iterando sui livelli):

```csharp
public List<ViewPlan> EnsureQtoPlans(Document doc)
{
    var levels = new FilteredElementCollector(doc)
        .OfClass(typeof(Level)).Cast<Level>()
        .OrderBy(l => l.Elevation).ToList();

    var planType = new FilteredElementCollector(doc)
        .OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
        .First(v => v.ViewFamily == ViewFamily.FloorPlan);

    var template2d = EnsureQtoViewTemplate2D(doc);
    var result = new List<ViewPlan>();

    foreach (var level in levels)
    {
        var viewName = $"QTO_Plan_{SanitizeName(level.Name)}";
        var existing = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
            .FirstOrDefault(v => !v.IsTemplate && v.Name == viewName);
        if (existing != null) { result.Add(existing); continue; }

        var plan = ViewPlan.Create(doc, planType.Id, level.Id);
        plan.Name = viewName;
        plan.ViewTemplateId = template2d.Id;
        result.Add(plan);
    }
    return result;
}
```

**SetupView — checkbox abilitante**:
- ☑ "Genera piante 2D per livello" (default: on)
- ☐ "Rigenera ad ogni apertura" (default: off — evita sovrascrittura di customizzazioni utente su piante esistenti)

### Estensione – Schedule native Revit (`QTO_Schedule_*`)

**Requisito**: generare **3 `ViewSchedule` native** che si aggiornano automaticamente al variare dei parametri QTO sugli elementi. Le schedule native sono il complemento numerico della 3D e offrono export diretto via `ViewSchedule.Export()`.

**3 schedule predefinite**:

| Nome | Filtro `ScheduleFilter` | Campi principali |
|---|---|---|
| `QTO_Schedule_Assegnazioni` | `QTO_Stato ∈ {COMPUTATO, PARZIALE}` | ElementId, Category, Family, `QTO_Codice`, `QTO_DescrizioneBreve`, Volume, Area, Length |
| `QTO_Schedule_Mancanti` | `QTO_Stato` vuoto / `HasNoValue` | ElementId, Category, Family, Level, Phase Created |
| `QTO_Schedule_NuoviPrezzi` | `QTO_Codice` begins-with `NP.` | ElementId, `QTO_Codice`, Quantity, `QTO_DescrizioneBreve` |

Sono **Multi-Category schedules** (`BuiltInCategory.OST_MultiCategory` + filtro su categorie QTO abilitate), così elencano trasversalmente Walls + Floors + Ceilings + Roofs + Generic ecc. in un'unica tabella — impossibile da fare con schedule single-category.

```csharp
public ViewSchedule EnsureQtoSchedule(Document doc, string name,
                                       ScheduleFilter filter,
                                       List<string> fieldNames)
{
    var existing = new FilteredElementCollector(doc)
        .OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
        .FirstOrDefault(s => s.Name == name);
    if (existing != null) return existing;   // riuso

    var schedule = ViewSchedule.CreateSchedule(
        doc, new ElementId(BuiltInCategory.OST_MultiCategory));
    schedule.Name = name;

    // Aggiungi campi
    foreach (var fname in fieldNames)
    {
        var field = schedule.Definition.GetSchedulableFields()
            .FirstOrDefault(f => f.GetName(doc).Equals(fname,
                StringComparison.OrdinalIgnoreCase));
        if (field != null) schedule.Definition.AddField(field);
    }

    // Applica filtro
    schedule.Definition.AddFilter(filter);

    // Ordinamento default per QTO_Codice
    var qtoCodeField = schedule.Definition.GetField(
        schedule.Definition.GetFieldCount() - 1);
    schedule.Definition.AddSortGroupField(
        new SortGroupField(qtoCodeField.FieldId, ScheduleSortOrder.Ascending));

    return schedule;
}
```

**Filtri `ScheduleFilter` per lo stato**:
```csharp
// Filtro "Mancanti" (stato vuoto)
var statoField = schedule.Definition.GetSchedulableFields()
    .First(f => f.GetName(doc) == "QTO_Stato");
var statoFieldId = schedule.Definition.AddField(statoField).FieldId;
var filter = new ScheduleFilter(
    statoFieldId, ScheduleFilterType.Equal, "");
```

**Perché native Revit e non export custom?**
- ✅ **Auto-aggiornamento**: al variare di `QTO_Stato` su un elemento, la schedule si ricalcola istantaneamente (0 codice di sync)
- ✅ **Esportabili in fogli di Revit**: l'architetto può inserirle in un A3 per stampare il report di mancanza
- ✅ **Export txt nativo** via `ViewSchedule.Export(folder, options)` → compatibile Primus / qualsiasi tool esterno
- ✅ **Condivise col team**: se il modello è su Cloud Worksharing, i colleghi vedono le schedule QTO anche senza plug-in

### Ribbon: SplitButton "Viste QTO"

Il pulsante è un `SplitButton` Revit (pattern `RibbonPanel.AddItem(SplitButtonData)`):

```
┌────────────────┐
│     🎯         │     click principale → apre Vista 3D QTO
│   Viste QTO  ▾ │     click arrow → dropdown:
└────────────────┘       • Vista 3D QTO
                         • Piante QTO ►  (submenu livelli)
                         ─────────────
                         • Schedule Assegnazioni
                         • Schedule Mancanti
                         • Schedule Nuovi Prezzi
                         ─────────────
                         • Rigenera tutte
                         • Impostazioni…
```

```csharp
var splitData = new SplitButtonData("QtoViewsSplit", "Viste QTO");
var split = panel.AddItem(splitData) as SplitButton;

// Voce "principale" (quella che gira col click diretto)
split.AddPushButton(new PushButtonData(
    "Open3D", "Vista 3D", asmPath, "QtoPlugin.Commands.OpenQto3DCommand"));

// Voci dropdown
split.AddPushButton(new PushButtonData(
    "OpenPlans", "Piante QTO (tutti i livelli)", asmPath,
    "QtoPlugin.Commands.OpenQtoPlansCommand"));
split.AddSeparator();
split.AddPushButton(new PushButtonData(
    "SchedAssign", "Schedule Assegnazioni", asmPath,
    "QtoPlugin.Commands.OpenScheduleAssegnazioniCommand"));
// ... altre schedule
split.AddSeparator();
split.AddPushButton(new PushButtonData(
    "RegenAll", "Rigenera tutte le viste QTO", asmPath,
    "QtoPlugin.Commands.RegenerateAllQtoViewsCommand"));
```

### Validazione API con RevitCortex (estensione)

| API Revit | Tool MCP RevitCortex | Note |
|---|---|---|
| `ViewPlan.Create(doc, planTypeId, levelId)` | `create_view` con `viewType: FloorPlan` + `levelId` | Match diretto |
| `ViewSchedule.CreateSchedule(doc, categoryId)` | `create_schedule` / `create_preset_schedule` | RC ha entrambi: preset per pattern comuni + create custom |
| `schedule.Definition.GetSchedulableFields()` | `list_schedulable_fields` | Utilizzabile per UI auto-discovery campi |
| `schedule.Definition.AddField(field)` | *(parte di `create_schedule`)* | Incorporato nella create |
| `ScheduleFilter(fieldId, type, value)` | *(parte di `create_schedule` via `filters[]`)* | RC espone filtri come array strutturato |
| `ViewSchedule.Export(folder, opts)` | `export_schedule(scheduleName, format, path)` | Path = percorso output |
| `ViewSchedule.Duplicate` | `duplicate_schedule` | Utile per pattern "copia e modifica" |

Il tool `modify_schedule` di RC è particolarmente utile: una volta creata la schedule, l'utente può modificarla interattivamente (aggiungere/rimuovere campi, cambiare filtri) senza reimportare, via comandi JSON ben documentati.

---

## I15. Registrazione DockablePane (pattern corretto)

**Requisito**: il `DockablePane` del plug-in (namespace `QtoPreviewPane`, label utente **"CME · Computo"**) deve:
1. Registrarsi **una volta sola** in `OnStartup`
2. **NON** auto-mostrarsi all'apertura di ogni documento Revit
3. Avere dimensioni iniziali adeguate sia in modalità **flottante** (default per workflow multi-monitor) che dockata
4. Rispettare la persistenza nativa di Revit per lo stato di visibilità

### Sintomi comuni di malconfigurazione

| Sintomo osservato | Root cause |
|---|---|
| "Il pannello è già aperto all'apertura di qualunque file Revit" | Revit persiste lo stato di visibilità dei DockablePane a livello di **profilo utente** (file `UIState.dat`). È nativo, non un bug. Diventa patologico se il plug-in chiama `pane.Show()` in event handler come `DocumentOpened` o `ViewActivated`, forzando la riapertura anche quando l'utente l'aveva chiuso |
| "Il pannello è troppo piccolo" (tipico 150×100 px) | `DockablePaneProviderData.InitialState` non configurato + `MinWidth`/`MinHeight` della UserControl root non impostati → Revit applica un fallback minuscolo |
| "Il pannello parte ancorato invece di flottante" | `InitialState.DockPosition` default a `Right` o `Tabbed`. Per il workflow multi-monitor serve `DockPosition.Floating` esplicito |
| "La posizione flottante è sempre in alto a sinistra schermo primario" | `InitialState.FloatingRectangle` non configurato → Revit parte a coordinate (0,0) del monitor primario |

### Fix 1 — Registrazione in `OnStartup` (idempotente, no Show)

```csharp
public class QtoApplication : IExternalApplication
{
    // Guid STABILE tra versioni (mai rigenerarlo, altrimenti Revit perde
    // lo stato di visibilità persistito tra le sessioni)
    public static readonly DockablePaneId PaneId =
        new(new Guid("A4B2C1D0-E5F6-7890-ABCD-EF1234567891"));

    public Result OnStartup(UIControlledApplication app)
    {
        // Registrazione una sola volta, SENZA Show()
        var provider = new QtoPaneProvider();
        app.RegisterDockablePane(PaneId, "CME · Computo", provider);

        // ❌ NON fare mai questo:
        // app.ControlledApplication.DocumentOpened += (s, e) => ShowPane();
        // app.ViewActivated += (s, e) => ShowPane();
        //
        // Revit persiste lo stato di visibilità da solo. Se forzi Show negli
        // eventi, l'utente non riesce MAI a chiudere il pannello definitivamente.

        return Result.Succeeded;
    }
}
```

### Fix 2 — `DockablePaneProviderData.InitialState` completo

```csharp
public class QtoPaneProvider : IDockablePaneProvider
{
    private QtoPreviewPane _paneWindow;

    public void SetupDockablePane(DockablePaneProviderData data)
    {
        _paneWindow ??= new QtoPreviewPane();
        data.FrameworkElement = _paneWindow;

        data.InitialState = new DockablePaneState
        {
            // Decisione architetturale: pannelli flottanti per workflow multi-monitor
            DockPosition = DockPosition.Floating,

            // Coordinate iniziali: NON (0,0). Calcola in base al monitor primario:
            // primary_width - pane_width - 80 per avere il pannello in alto a destra
            FloatingRectangle = ComputeInitialFloatingRect(),

            // Dimensioni minime in modalità dockata (fallback se utente lo ancora)
            MinimumWidth = 420,
            MinimumHeight = 600
        };

        // ❌ NON impostare data.VisibleByDefault: la visibilità è gestita da Revit
        // in base allo stato utente persistito. Settarla forza un comportamento
        // che "ruba" il controllo all'utente.
    }

    private Rectangle ComputeInitialFloatingRect()
    {
        var primary = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
        const int w = 520, h = 760;
        return new Rectangle(
            primary.Right - w - 40,   // 40px margine destro
            primary.Top + 80,          // 80px sotto la barra Revit
            w, h);
    }
}
```

### Fix 3 — UserControl XAML con minimi espliciti

```xml
<UserControl x:Class="QtoPlugin.UI.Panes.QtoPreviewPane"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             MinWidth="420"
             MinHeight="600"
             d:DesignWidth="520"
             d:DesignHeight="760"
             FontFamily="{StaticResource FontBody}"
             FontSize="{StaticResource FontSizeBody}"
             Background="{StaticResource PanelBgBrush}">
  <!-- ... layout QtoPreviewPane con tab Selezione Corrente / Riepilogo ... -->
</UserControl>
```

I minimi XAML servono come **secondo livello di difesa**: se Revit 2026+ cambia il comportamento di `InitialState` (già successo tra 2024 e 2025), la UserControl si auto-protegge dal rendering micro.

### Fix 4 — Apertura su richiesta esplicita (pulsante ribbon)

L'utente apre il pannello tramite il pulsante "Avvia QTO" nel ribbon (primo pulsante del panel "Start"). Dopo l'apertura, Revit persiste lo stato e lo ripropone alla sessione successiva senza ulteriore codice del plug-in:

```csharp
// LaunchQtoCommand.cs
public Result Execute(ExternalCommandData commandData, ref string msg, ElementSet els)
{
    var uiApp = commandData.Application;
    var pane = uiApp.GetDockablePane(QtoApplication.PaneId);

    if (pane.IsShown())
    {
        // Già aperto → porta in focus
        pane.Show();  // no-op se visibile, ma garantisce il focus
    }
    else
    {
        pane.Show();  // prima apertura della sessione utente
    }
    return Result.Succeeded;
}
```

### Comportamento nativo Revit — non toccare

Revit gestisce autonomamente (senza intervento del plug-in):
- Persistenza stato visibilità tra sessioni (`UIState.dat`)
- Persistenza posizione + dimensione flottante per quel `DockablePaneId`
- Ripristino della configurazione all'avvio successivo

**Regola d'oro**: il plug-in **registra** il pane e **risponde** al click utente. Non forza visibilità, non monitora eventi di apertura documento per "riattivare". Lascia Revit fare il suo lavoro.

### Validazione con RevitCortex

Il plug-in `RevitCortex v1.0.13` (riferimento implementativo) registra il proprio DockablePane esattamente con questo pattern: una sola `RegisterDockablePane` in startup, nessun forcing di visibilità, dimensioni iniziali configurate. Il comportamento "si apre sempre" che talvolta viene osservato è, appunto, persistenza nativa Revit — non va "corretta" ma documentata nel manuale utente come "chiudi il pannello quando non serve; Revit ricorderà la scelta".

### Troubleshooting rapido

Se dopo questi fix il pane appare ancora minuscolo alla prima apertura:
1. Cancella il file `%AppData%\Autodesk\Revit\<versione>\UIState.dat` — forza un reset dello stato persistito (l'utente perde anche altre configurazioni DockablePane, avvisare)
2. Verifica che `GetDockablePane(paneId)` NON lanci `InvalidOperationException` — se lo fa, il `Guid` è cambiato tra build (non deve MAI succedere)
3. Log del `data.InitialState` effettivamente applicato: se `DockPosition` viene sovrascritto a `Tabbed`, un altro plug-in ha "rubato" la posizione (raro ma possibile con toolkit concorrenti)

---

## Matrice di Impatto – Sprint

La tabella mostra in quale sprint viene sviluppata ogni implementazione aggiuntiva.

| Implementazione | Sprint | Dipendenze |
|---|---|---|
| I1 – Scrittura bidirezionale | 3 (Shared Parameters) | ES + SharedParamManager |
| I2 – Multi-EP via ES | 3 | ExtensibleStorageRepo |
| I3 – FilterBuilder + ricerca inline | 4 | PhaseFilterView |
| I4 – Esclusione elementi | 4 | FilterBuilder |
| I5 – Health Check | 6 | QtoRepository |
| I6 – Preset regole selezione | 4 | SelectionView + DataStorage |
| I7 – Multi-listino + FTS5 | 2 | DB schema + parser |
| I8 – Nuovi Prezzi NP | 8 | DB NuoviPrezzi + NpEngine |
| I9 – Filtro fase Revit | 4 | PhaseFilterView (primo step) |
| I10 – Diff modello post-edit | 7 | ModelDiffService + ModelDiffLog |
| I11 – FilterManager nativo | 5 | Shared Param `QTO_Stato` + FilterManagerView |
| I12 – Sorgente B Room+NCalc | 4–5 | NCalc + ParameterNameResolver + FormulaEditorView |
| I13 – Sorgente C voci manuali | 5 | Schema DB `ManualItems` + tab TaggingView |
| I14 – Viste QTO dedicate (3D + 2D + Schedules) | 5 | §I11 FilterManager + Level iteration + Multi-Category Schedules |
| I15 – Registrazione DockablePane corretta | 0 (Sprint 0 setup) | IDockablePaneProvider + DockablePaneState + UIState.dat |
