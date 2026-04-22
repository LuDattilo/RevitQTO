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
