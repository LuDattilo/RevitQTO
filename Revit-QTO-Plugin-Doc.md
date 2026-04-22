# Revit QTO Plugin – Documentazione Tecnica e Planning di Sviluppo

## Executive Summary

Questo documento costituisce la specifica tecnica completa per lo sviluppo di un plug-in Revit per il Quantity Take-Off (QTO), destinato a professionisti BIM nell'ambito di appalti pubblici italiani. Il sistema è concepito come un motore deterministico basato su regole, con integrazione opzionale di AI per funzioni ausiliarie di mapping semantico. Il documento copre l'architettura del sistema, le scelte tecnologiche motivate, la struttura del codice, i pattern API critici e un piano di sviluppo suddiviso in sprint.

---

## 1. Stack Tecnologico e Prerequisiti

### 1.1 Target di Versioni Revit

Il plug-in deve supportare **Revit 2022–2026**, coprendo le differenze introdotte dall'API in questo intervallo. La distinzione principale è il cambio del modello di runtime:

- **Revit 2022–2024**: .NET Framework 4.8
- **Revit 2025+**: .NET 8 (breaking change, ricompilazione obbligatoria)

La strategia raccomandata è il **multi-targeting** con un unico codebase, usando conditional compilation nel file `.csproj`:

```xml
<TargetFrameworks>net48;net8.0-windows</TargetFrameworks>
<DefineConstants Condition="'$(TargetFramework)'=='net48'">REVIT2024_OR_EARLIER</DefineConstants>
<DefineConstants Condition="'$(TargetFramework)'=='net8.0-windows'">REVIT2025_OR_LATER</DefineConstants>
```

Nel codice C# si gestiscono i breakpoint API con direttive `#if`:

```csharp
#if REVIT2025_OR_LATER
    // Usare ForgeTypeId per unità di misura
    double areaMeters = UnitUtils.ConvertFromInternalUnits(areaFt2, UnitTypeId.SquareMeters);
#else
    // API legacy pre-2022
    double areaMeters = UnitUtils.ConvertFromInternalUnits(areaFt2, DisplayUnitType.DUT_SQUARE_METERS);
#endif
```

Il cambio più rilevante riguarda le unità di misura: da Revit 2022 `DisplayUnitType` è deprecato e sostituito da `ForgeTypeId`, acceduto tramite `UnitUtils`, `LabelUtils` e `SpecUtils`. Le classi `SpecTypeId` e `UnitTypeId` contengono le costanti statiche per ogni grandezza fisica.

### 1.2 Dipendenze NuGet

| Libreria | Versione consigliata | Scopo |
|---|---|---|
| `RevitAPI` / `RevitAPIUI` | (locale, da cartella Revit) | Core Revit integration |
| `NCalc2` | 2.x | Formula engine deterministico |
| `ClosedXML` | 0.102+ | Export Excel professionale |
| `CommunityToolkit.Mvvm` | 8.x | MVVM per WPF (.NET 8) |
| `Revit.Async` | opzionale | Wrapper async/await per ExternalEvent |

---

## 2. Architettura del Sistema

### 2.1 Layer Architecture

```
┌─────────────────────────────────────────────────┐
│  UI Layer (WPF + MVVM)                          │
│  - MainWindowViewModel                          │
│  - SetupView / MappingView / TaggingView         │
│  - HealthCheckView / ExportView                 │
├─────────────────────────────────────────────────┤
│  Application Services Layer                     │
│  - QtoCommandOrchestrator                       │
│  - ExternalEventHandlers (IExternalEventHandler)│
├─────────────────────────────────────────────────┤
│  Core Engine (C# / Revit API)                   │
│  - PriceListParser (.dcf / .xlsx / .csv)        │
│  - CategoryMapper + FormulaEngine (NCalc)       │
│  - QuantityExtractor (FilteredElementCollector) │
│  - HealthCheckEngine                            │
│  - ExportEngine (ClosedXML)                     │
├─────────────────────────────────────────────────┤
│  Data Layer                                     │
│  - ExtensibleStorageRepository (config. mappature) │
│  - SharedParameterManager (QTO_Codice, etc.)    │
│  - FileRepository (I/O .dcf, .xlsx, .csv)       │
└─────────────────────────────────────────────────┘
```

Il pattern architetturale adottato per la UI è **MVVM (Model-View-ViewModel)**, che separa la logica Revit dalla logica di presentazione, garantisce testabilità e facilita l'evoluzione dell'interfaccia. Il `ViewModel` non conosce né la `View` né la Revit API direttamente: riceve i dati tramite i service del Core Engine.

### 2.2 Threading Model in Revit

La Revit API è **single-threaded** e può essere invocata solo dal thread principale di Revit. Per le finestre WPF modeless (non bloccanti), il pattern obbligatorio è `IExternalEventHandler` + `ExternalEvent`:

```csharp
public class WriteQtoCodeHandler : IExternalEventHandler
{
    public string QtoCode { get; set; }
    public List<ElementId> TargetElementIds { get; set; }

    public void Execute(UIApplication app)
    {
        var doc = app.ActiveUIDocument.Document;
        using var tx = new Transaction(doc, "Assegna QTO_Codice");
        tx.Start();
        foreach (var id in TargetElementIds)
        {
            var elem = doc.GetElement(id);
            var param = elem.LookupParameter("QTO_Codice");
            param?.Set(QtoCode);
        }
        tx.Commit();
    }

    public string GetName() => "WriteQtoCodeHandler";
}
```

Il `ViewModel` invoca `externalEvent.Raise()` dopo aver impostato le proprietà sull'handler, senza mai toccare direttamente la Revit API dal thread WPF.

---

## 3. Struttura del Progetto Visual Studio

```
QtoRevitPlugin/
├── QtoRevitPlugin.csproj          ← multi-target net48;net8.0-windows
├── Application/
│   └── QtoApplication.cs          ← IExternalApplication, ribbon setup
├── Commands/
│   ├── LaunchQtoCommand.cs        ← IExternalCommand entry point
│   └── HealthCheckCommand.cs
├── Core/
│   ├── Parsers/
│   │   ├── DcfParser.cs           ← XML parser per formato .dcf / .xpwe
│   │   ├── ExcelParser.cs         ← lettura .xlsx con ClosedXML
│   │   └── CsvParser.cs
│   ├── Mapping/
│   │   ├── CategoryMapper.cs      ← associa cat. Revit → parametro geometrico
│   │   └── FormulaEngine.cs       ← wrapper NCalc
│   ├── Extraction/
│   │   └── QuantityExtractor.cs   ← FilteredElementCollector multi-categoria
│   ├── Validation/
│   │   └── HealthCheckEngine.cs
│   └── Export/
│       └── ExportEngine.cs        ← ClosedXML + testo tabulato
├── Data/
│   ├── ExtensibleStorageRepo.cs   ← salva/legge config. nel .rvt
│   └── SharedParameterManager.cs ← crea/verifica file .txt parametri condivisi
├── ExternalEvents/
│   ├── WriteParameterHandler.cs
│   ├── IsolateElementsHandler.cs
│   └── OverrideColorHandler.cs
├── Models/
│   ├── PriceItem.cs               ← voce di listino
│   ├── CategoryMapping.cs         ← config. mappatura categoria
│   └── QtoResult.cs               ← risultato calcolo
├── UI/
│   ├── Windows/
│   │   └── QtoMainWindow.xaml(.cs)
│   ├── Views/
│   │   ├── SetupView.xaml(.cs)
│   │   ├── MappingView.xaml(.cs)
│   │   ├── TaggingView.xaml(.cs)
│   │   ├── HealthCheckView.xaml(.cs)
│   │   └── ExportView.xaml(.cs)
│   └── ViewModels/
│       ├── MainWindowViewModel.cs
│       ├── SetupViewModel.cs
│       ├── MappingViewModel.cs
│       ├── TaggingViewModel.cs
│       ├── HealthCheckViewModel.cs
│       └── ExportViewModel.cs
├── AI/
│   └── SmartMappingService.cs     ← integrazione LLM (opzionale)
└── QtoRevitPlugin.addin           ← manifest Revit
```

---

## 4. Fase 1 – Setup e Parsing del Listino

### 4.1 Struttura del Formato .dcf / .xpwe

Il formato DCF di ACCA PriMus è basato su **XML standard** (e la variante `.xpwe` è anch'essa XML). La struttura radice del file XML contiene tipicamente i nodi:

```xml
<PriMus>
  <Listino>
    <VoceEP CodiceVoce="A.01.001" 
            DescrVoce="Scavo a sezione obbligata..." 
            UnitaMisura="mc" 
            PrezzoUnitario="12.50"
            Capitolo="A - SCAVI E MOVIMENTI DI TERRA" />
    ...
  </Listino>
</PriMus>
```

Il parser DCF usa `System.Xml.Linq` (XDocument) per evitare dipendenze esterne:

```csharp
public class DcfParser : IPriceListParser
{
    public List<PriceItem> Parse(string filePath)
    {
        var xdoc = XDocument.Load(filePath);
        var ns = xdoc.Root?.GetDefaultNamespace() ?? XNamespace.None;

        return xdoc.Descendants(ns + "VoceEP")
            .Select(v => new PriceItem
            {
                Code        = v.Attribute("CodiceVoce")?.Value ?? string.Empty,
                Description = v.Attribute("DescrVoce")?.Value ?? string.Empty,
                Unit        = v.Attribute("UnitaMisura")?.Value ?? string.Empty,
                UnitPrice   = double.TryParse(
                    v.Attribute("PrezzoUnitario")?.Value,
                    NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : 0.0
            }).ToList();
    }
}
```

> **Nota**: I nomi esatti degli attributi XML variano tra le versioni di PriMus. È consigliabile ispezionare un file .dcf di esempio con il prezzario regionale di riferimento (es. Toscana) prima di finalizzare il parser. ACCA mette a disposizione PW-CONV per la conversione tra formati.

### 4.2 Column Mapping UI

La UI di mapping espone una `DataGrid` WPF in cui l'utente associa le colonne rilevate automaticamente dal file importato alle proprietà semantiche attese (`Descrizione`, `PrezzoUnitario`, `UnitaMisura`, `CodiceVoce`). Il `MappingViewModel` propone una corrispondenza predefinita basata su euristica sui nomi colonna, modificabile dall'utente.

### 4.3 Formula Engine con NCalc

NCalc è una libreria .NET leggera e rapida per la valutazione di espressioni matematiche con parametri:

```csharp
public class FormulaEngine
{
    public double Evaluate(string formula, Dictionary<string, double> parameters)
    {
        var expr = new NCalc.Expression(formula);
        foreach (var kv in parameters)
            expr.Parameters[kv.Key] = kv.Value;
        return Convert.ToDouble(expr.Evaluate());
    }
}

// Esempio di utilizzo:
// formula = "Prezzo * (1 + PercSicurezza / 100)"
// parameters = { "Prezzo": 12.50, "PercSicurezza": 2.0 }
```

Le formule vengono salvate nell'Extensible Storage insieme alla configurazione di mapping, rendendo il setup persistente nel file .rvt.

---

## 5. Fase 2 – Mappatura Categorie Revit e Parametri Geometrici

### 5.1 FilteredElementCollector Multi-Categoria

L'estrazione dati usa `ElementMultiCategoryFilter` per interrogare tutte le categorie mappate in un'unica passata sull'albero del documento, minimizzando i round-trip all'API:

```csharp
public class QuantityExtractor
{
    private readonly Document _doc;

    public List<ElementQuantity> Extract(IEnumerable<CategoryMapping> mappings)
    {
        var categoryIds = mappings
            .Select(m => new ElementId((int)m.BuiltInCategory))
            .ToList();

        var multiFilter = new ElementMultiCategoryFilter(categoryIds);
        var elements = new FilteredElementCollector(_doc)
            .WherePasses(multiFilter)
            .WhereElementIsNotElementType()
            .ToElements();

        var results = new List<ElementQuantity>();
        foreach (var elem in elements)
        {
            var mapping = mappings.First(m => (int)m.BuiltInCategory == elem.Category.Id.IntegerValue);
            var quantity = GetQuantity(elem, mapping.ParameterType);
            var qtoCode = elem.LookupParameter("QTO_Codice")?.AsString();

            results.Add(new ElementQuantity
            {
                ElementId  = elem.Id.IntegerValue,
                Category   = elem.Category.Name,
                QtoCode    = qtoCode,
                Quantity   = quantity,
                Unit       = mapping.UnitLabel
            });
        }
        return results;
    }

    private double GetQuantity(Element elem, QuantityParameterType paramType)
    {
        return paramType switch
        {
            QuantityParameterType.Area   => GetDoubleParam(elem, BuiltInParameter.HOST_AREA_COMPUTED),
            QuantityParameterType.Volume => GetDoubleParam(elem, BuiltInParameter.HOST_VOLUME_COMPUTED),
            QuantityParameterType.Length => GetDoubleParam(elem, BuiltInParameter.CURVE_ELEM_LENGTH),
            QuantityParameterType.Count  => 1.0,
            _ => 0.0
        };
    }

    private double GetDoubleParam(Element elem, BuiltInParameter bip)
    {
        var param = elem.get_Parameter(bip);
        if (param == null || param.StorageType != StorageType.Double) return 0.0;
#if REVIT2025_OR_LATER
        return UnitUtils.ConvertFromInternalUnits(param.AsDouble(), UnitTypeId.SquareMeters);
#else
        return UnitUtils.ConvertFromInternalUnits(param.AsDouble(), DisplayUnitType.DUT_SQUARE_METERS);
#endif
    }
}
```

Categorie Revit supportate e parametri disponibili:

| Categoria Revit | Parametri disponibili |
|---|---|
| `OST_Walls` | Area, Volume, Lunghezza |
| `OST_Floors` | Area, Volume |
| `OST_Ceilings` | Area |
| `OST_Roofs` | Area, Volume |
| `OST_Columns` / `OST_StructuralColumns` | Volume, Lunghezza, Conteggio |
| `OST_Doors` / `OST_Windows` | Conteggio, Area (apertura) |
| `OST_GenericModel` | Area, Volume, Lunghezza, Conteggio |
| `OST_StructuralFraming` | Volume, Lunghezza |

### 5.2 Extensible Storage – Persistenza della Configurazione

L'Extensible Storage permette di agganciare dati strutturati a qualsiasi elemento Revit, compreso il `ProjectInfo`, e i dati vengono salvati nel file .rvt. A differenza dei Shared Parameters, i dati ES non sono visibili nelle schedules o dall'utente finale:

```csharp
public class ExtensibleStorageRepo
{
    private static readonly Guid SchemaGuid = new Guid("A4B2C1D0-E5F6-7890-ABCD-EF1234567890");
    private const string SchemaName = "QtoPluginConfig_v1";

    public void SaveMappings(Document doc, List<CategoryMapping> mappings)
    {
        using var tx = new Transaction(doc, "Salva config QTO");
        tx.Start();

        var schema = GetOrCreateSchema();
        var projectInfo = doc.ProjectInformation;
        var entity = new Entity(schema);

        var serialized = JsonSerializer.Serialize(mappings);
        entity.Set(schema.GetField("MappingsJson"), serialized);
        projectInfo.SetEntity(entity);

        tx.Commit();
    }

    public List<CategoryMapping> LoadMappings(Document doc)
    {
        var schema = Schema.Lookup(SchemaGuid);
        if (schema == null) return new List<CategoryMapping>();

        var entity = doc.ProjectInformation.GetEntity(schema);
        if (!entity.IsValid()) return new List<CategoryMapping>();

        var json = entity.Get<string>(schema.GetField("MappingsJson"));
        return JsonSerializer.Deserialize<List<CategoryMapping>>(json) ?? new List<CategoryMapping>();
    }

    private Schema GetOrCreateSchema()
    {
        var existing = Schema.Lookup(SchemaGuid);
        if (existing != null) return existing;

        var builder = new SchemaBuilder(SchemaGuid);
        builder.SetSchemaName(SchemaName);
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Application);
        builder.SetVendorId("QTO_STUDIO");
        builder.AddSimpleField("MappingsJson", typeof(string));
        return builder.Finish();
    }
}
```

> **Best practice ES**: il GUID dello schema deve essere gestito con attenzione — un cambio di GUID crea un nuovo schema, rendendo inaccessibili i dati del vecchio. Versionare il nome dello schema (`_v1`, `_v2`) per gestire le migrazioni. Mantenere i dati serializzati piccoli (JSON compatto) per non rallentare open/sync del file.

---

## 6. Fase 3 – Tagging: Assegnazione Codici QTO

### 6.1 Shared Parameter – QTO_Codice

Il plug-in deve creare automaticamente un file di Shared Parameters e registrare il parametro `QTO_Codice` su tutte le categorie mappate:

```csharp
public class SharedParameterManager
{
    private const string GroupName  = "QTO_Parameters";
    private const string ParamName  = "QTO_Codice";
    private const string TempSpFile = "QTO_SharedParams.txt";

    public void EnsureParameterExists(Application app, Document doc, 
                                      IEnumerable<BuiltInCategory> categories)
    {
        var spFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "QtoPlugin", TempSpFile);

        Directory.CreateDirectory(Path.GetDirectoryName(spFilePath)!);
        if (!File.Exists(spFilePath))
            File.WriteAllText(spFilePath, "# QTO Shared Parameters\r\n");

        app.SharedParametersFilename = spFilePath;
        var spFile = app.OpenSharedParameterFile();
        var group  = spFile.Groups.get_Item(GroupName) 
                     ?? spFile.Groups.Create(GroupName);

        ExternalDefinition def;
        if (group.Definitions.get_Item(ParamName) is ExternalDefinition existing)
            def = existing;
        else
        {
#if REVIT2025_OR_LATER
            var opts = new ExternalDefinitionCreationOptions(ParamName, SpecTypeId.String.Text);
#else
            var opts = new ExternalDefinitionCreationOptions(ParamName, ParameterType.Text);
#endif
            def = (ExternalDefinition)group.Definitions.Create(opts);
        }

        using var tx = new Transaction(doc, "Crea Shared Parameter QTO_Codice");
        tx.Start();
        var catSet = app.Create.NewCategorySet();
        foreach (var bic in categories)
            catSet.Insert(doc.Settings.Categories.get_Item(bic));

        var binding = app.Create.NewInstanceBinding(catSet);
        doc.ParameterBindings.Insert(def, binding, GroupTypeId.Data);
        tx.Commit();
    }
}
```

### 6.2 UI di Tagging

La `TaggingView` è il cuore operativo del plug-in. Strutturalmente è una finestra modeless (`Show()` invece di `ShowDialog()`) che espone:

- **Campo di ricerca** con debounce su keystroke (300ms) che filtra `ObservableCollection<PriceItem>`
- **DataGrid** dei risultati con colonne: Codice, Descrizione, U.M., Prezzo Unitario
- **Contatore elementi selezionati** nel modello 3D (sincronizzato con `UIDocument.Selection`)
- **Pulsante "Assegna"**: scrive `QTO_Codice` tramite `ExternalEvent`
- **Pulsante "Isola Computati" / "Isola Mancanti"**: evidenziazione grafica

### 6.3 Isolamento Visuale e Color Coding

Per evidenziare graficamente lo stato del tagging si usano due approcci complementari:

```csharp
public class OverrideColorHandler : IExternalEventHandler
{
    public enum ColorMode { Tagged, Untagged, Reset }
    public ColorMode Mode { get; set; }

    public void Execute(UIApplication app)
    {
        var uidoc = app.ActiveUIDocument;
        var doc   = uidoc.Document;
        var view  = doc.ActiveView;

        var allElements = new FilteredElementCollector(doc, view.Id)
            .WhereElementIsNotElementType()
            .ToElements();

        using var tx = new Transaction(doc, "QTO Color Override");
        tx.Start();

        foreach (var elem in allElements)
        {
            if (elem.Category == null) continue;
            var code = elem.LookupParameter("QTO_Codice")?.AsString();
            bool isTagged = !string.IsNullOrEmpty(code);

            var ogs = new OverrideGraphicSettings();
            if (Mode == ColorMode.Reset)
            {
                view.SetElementOverrides(elem.Id, ogs); // reset
                continue;
            }

            if ((Mode == ColorMode.Tagged && isTagged) ||
                (Mode == ColorMode.Untagged && !isTagged))
            {
                var color = Mode == ColorMode.Tagged
                    ? new Color(100, 200, 100)  // verde = computato
                    : new Color(220, 80, 80);    // rosso = mancante

                ogs.SetSurfaceForegroundPatternColor(color);
                ogs.SetSurfaceForegroundPatternId(GetSolidPatternId(doc));
            }
            else
            {
                ogs.SetHalftone(true); // sfuma gli elementi "altri"
            }
            view.SetElementOverrides(elem.Id, ogs);
        }
        tx.Commit();
    }
}
```

---

## 7. Fase 4 – Health Check e Validazione

### 7.1 Regole di Validazione

```csharp
public class HealthCheckEngine
{
    public List<HealthCheckIssue> RunCheck(Document doc, List<CategoryMapping> mappings)
    {
        var issues = new List<HealthCheckIssue>();
        var extractor = new QuantityExtractor(doc);
        var elements  = extractor.Extract(mappings);

        foreach (var eq in elements)
        {
            // Regola 1: Codice QTO mancante
            if (string.IsNullOrEmpty(eq.QtoCode))
                issues.Add(new HealthCheckIssue(eq.ElementId, IssueType.MissingCode,
                    $"Elemento {eq.ElementId} ({eq.Category}) senza QTO_Codice"));

            // Regola 2: Quantità nulla o negativa
            if (eq.Quantity <= 0)
                issues.Add(new HealthCheckIssue(eq.ElementId, IssueType.ZeroQuantity,
                    $"Elemento {eq.ElementId} ha quantità zero o negativa"));

            // Regola 3: Codice non presente nel listino caricato
            if (!string.IsNullOrEmpty(eq.QtoCode) && !_priceItems.Any(p => p.Code == eq.QtoCode))
                issues.Add(new HealthCheckIssue(eq.ElementId, IssueType.InvalidCode,
                    $"Codice '{eq.QtoCode}' non trovato nel listino corrente"));
        }

        // Regola 4: Categorie nel modello non mappate
        var unmappedCategories = GetAllModelCategories(doc)
            .Except(mappings.Select(m => m.BuiltInCategory));
        foreach (var cat in unmappedCategories)
            issues.Add(new HealthCheckIssue(null, IssueType.UnmappedCategory,
                $"Categoria '{cat}' presente nel modello ma non mappata"));

        return issues;
    }
}
```

### 7.2 Navigazione agli Elementi dal Report

La `HealthCheckView` espone la lista degli errori in una `DataGrid`. Il doppio click su una riga esegue:

```csharp
private void OnIssueDoubleClick(HealthCheckIssue issue)
{
    if (issue.ElementId.HasValue)
    {
        _navigateHandler.TargetElementId = new ElementId(issue.ElementId.Value);
        _navigateEvent.Raise();
    }
}

// Handler Revit:
public class NavigateToElementHandler : IExternalEventHandler
{
    public ElementId TargetElementId { get; set; }
    public void Execute(UIApplication app)
    {
        var uidoc = app.ActiveUIDocument;
        uidoc.Selection.SetElementIds(new[] { TargetElementId });
        uidoc.ShowElements(TargetElementId);
    }
}
```

---

## 8. Fase 5 – Calcolo Deterministico ed Esportazione

### 8.1 Motore di Calcolo

Il calcolo è puramente lineare. Per ogni elemento:

```
Totale_Voce = Quantità_Revit × Prezzo_Listino
```

Con formula personalizzata NCalc (opzionale):

```
Totale_Voce = Quantità_Revit × f(Prezzo_Listino, Parametri_Config)
```

```csharp
public List<QtoResult> Calculate(List<ElementQuantity> quantities, 
                                  List<PriceItem> priceList,
                                  List<CategoryMapping> mappings)
{
    return quantities
        .Where(q => !string.IsNullOrEmpty(q.QtoCode))
        .Select(q =>
        {
            var item    = priceList.FirstOrDefault(p => p.Code == q.QtoCode);
            var mapping = mappings.First(m => m.CategoryName == q.Category);
            
            double effectivePrice = string.IsNullOrEmpty(mapping.PriceFormula)
                ? item?.UnitPrice ?? 0.0
                : _formulaEngine.Evaluate(mapping.PriceFormula, 
                    new Dictionary<string, double> { 
                        { "Prezzo", item?.UnitPrice ?? 0.0 },
                        { "PercSicurezza", mapping.SecurityPercent } 
                    });

            return new QtoResult
            {
                ElementId   = q.ElementId,
                QtoCode     = q.QtoCode,
                Description = item?.Description ?? "N/D",
                Unit        = item?.Unit ?? q.Unit,
                Quantity    = q.Quantity,
                UnitPrice   = effectivePrice,
                Total       = q.Quantity * effectivePrice
            };
        }).ToList();
}
```

### 8.2 Export Excel con ClosedXML

```csharp
public void ExportToExcel(List<QtoResult> results, string outputPath)
{
    using var wb = new XLWorkbook();
    var ws = wb.Worksheets.Add("Computo QTO");

    var headers = new[] { "Codice", "Descrizione", "U.M.", "Quantità", "Prezzo Unit.", "Totale" };
    for (int i = 0; i < headers.Length; i++)
    {
        ws.Cell(1, i + 1).Value = headers[i];
        ws.Cell(1, i + 1).Style.Font.Bold = true;
        ws.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E79");
        ws.Cell(1, i + 1).Style.Font.FontColor = XLColor.White;
    }

    var grouped = results.GroupBy(r => r.QtoCode);
    int row = 2;
    foreach (var group in grouped)
    {
        var totalQty   = group.Sum(r => r.Quantity);
        var unitPrice  = group.First().UnitPrice;
        var totalCost  = group.Sum(r => r.Total);
        var first      = group.First();

        ws.Cell(row, 1).Value = first.QtoCode;
        ws.Cell(row, 2).Value = first.Description;
        ws.Cell(row, 3).Value = first.Unit;
        ws.Cell(row, 4).Value = totalQty;  ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0.00";
        ws.Cell(row, 5).Value = unitPrice; ws.Cell(row, 5).Style.NumberFormat.Format = "€ #,##0.00";
        ws.Cell(row, 6).Value = totalCost; ws.Cell(row, 6).Style.NumberFormat.Format = "€ #,##0.00";
        row++;
    }

    ws.Cell(row, 5).Value = "TOTALE";
    ws.Cell(row, 6).FormulaA1 = $"=SUM(F2:F{row - 1})";
    ws.Cell(row, 6).Style.Font.Bold = true;

    ws.Columns().AdjustToContents();
    wb.SaveAs(outputPath);
}
```

---

## 9. Implementazione AI – Smart Mapping (Opzionale)

```csharp
public class SmartMappingService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public async Task<List<MappingSuggestion>> SuggestMappings(
        List<string> familyNames, 
        List<PriceItem> priceItems,
        int topK = 3)
    {
        var prompt = BuildPrompt(familyNames, priceItems);
        var response = await CallLlmApi(prompt);
        return ParseSuggestions(response, priceItems);
    }

    private string BuildPrompt(List<string> families, List<PriceItem> items)
    {
        var itemsJson = JsonSerializer.Serialize(
            items.Select(i => new { i.Code, i.Description, i.Unit }));
        var familiesStr = string.Join("\n", families);

        return $"""
        Sei un esperto di computo metrico italiano.
        Date le seguenti famiglie Revit:
        {familiesStr}
        
        E le seguenti voci di listino (JSON):
        {itemsJson}
        
        Per ogni famiglia, suggerisci le 3 voci di listino più pertinenti.
        Rispondi in formato JSON: [{{"family":"...", "suggestions":["CODE1","CODE2","CODE3"]}}]
        """;
    }
}
```

Il servizio espone i suggerimenti nella `TaggingView` come tooltip o dropdown, ma l'utente deve **confermare esplicitamente** ogni assegnazione. Nessun valore viene scritto nel modello senza intervento umano.

---

## 10. Piano di Sviluppo – Sprint Planning

### 10.1 Parametri di Progetto

- **Team stimato**: 1 sviluppatore senior C#/Revit API (80% del tempo)
- **Sprint duration**: 2 settimane
- **Metodologia**: Scrum semplificato con sprint review
- **Effort totale stimato**: ~14 settimane (7 sprint)

### 10.2 Roadmap Sprint

#### Sprint 0 – Fondamenta (Settimane 1–2)

| Task | Effort (giorni) |
|---|---|
| Setup progetto VS con multi-targeting net48 + net8 | 1 |
| Ribbon + IExternalApplication + addin manifest | 1 |
| Struttura cartelle, modelli dati (PriceItem, CategoryMapping, QtoResult) | 1 |
| Template MVVM: MainWindow, navigazione View tramite Frame WPF | 2 |
| Unit test project setup | 1 |
| CI/CD base (build multi-version, artifact per versione Revit) | 2 |
| **Deliverable** | Addin caricabile in Revit con ribbon, finestra WPF vuota |

#### Sprint 1 – Parsing Listino (Settimane 3–4)

| Task | Effort (giorni) |
|---|---|
| DcfParser – XML parsing con XDocument | 2 |
| ExcelParser – lettura con ClosedXML | 1 |
| CsvParser con auto-detection delimitatore | 1 |
| Column Mapping UI (DataGrid + combobox mapping) | 2 |
| FormulaEngine (NCalc wrapper + test) | 1 |
| Validazione input (file vuoto, colonne mancanti) | 1 |
| **Deliverable** | Listino caricato e visualizzato in tabella |

#### Sprint 2 – Extensible Storage e Shared Parameters (Settimane 5–6)

| Task | Effort (giorni) |
|---|---|
| ExtensibleStorageRepo – schema, read/write JSON config | 2 |
| Schema versioning e migrazione | 1 |
| SharedParameterManager – creazione automatica file + binding | 2 |
| Supporto multi-versione API (ForgeTypeId vs ParameterType) | 1 |
| Test: round-trip salva/ricarica configurazione in .rvt | 2 |
| **Deliverable** | Configurazione persistente nel file di progetto Revit |

#### Sprint 3 – Category Mapping e Extraction (Settimane 7–8)

| Task | Effort (giorni) |
|---|---|
| MappingView UI – selezione categoria, parametro geometrico, formula | 2 |
| QuantityExtractor – FilteredElementCollector multi-categoria | 2 |
| Gestione conversione unità (interno Revit → m², m³, m) | 1 |
| Test estrazione su modello reale (pareti, solai, finestre) | 2 |
| **Deliverable** | Tabella con quantità estratte per ogni categoria |

#### Sprint 4 – Tagging e Visual Feedback (Settimane 9–10)

| Task | Effort (giorni) |
|---|---|
| TaggingView UI – ricerca listino, selezione, assegnazione | 2 |
| WriteParameterHandler (ExternalEvent) – scrittura QTO_Codice | 1 |
| OverrideColorHandler – verde/rosso/halftone per stato tagging | 2 |
| IsolateCategoriesTemporary / IsolateElementsTemporary | 1 |
| Sincronizzazione selezione Revit ↔ DataGrid WPF | 2 |
| **Deliverable** | Operativo per tagging manuale con feedback visivo |

#### Sprint 5 – Health Check ed Export (Settimane 11–12)

| Task | Effort (giorni) |
|---|---|
| HealthCheckEngine – 4 regole di validazione | 2 |
| HealthCheckView – DataGrid errori + navigazione elemento | 2 |
| ExportEngine – Excel (ClosedXML) con formattazione professionale | 2 |
| Export testo tabulato (TSV) per compatibilità PriMus | 1 |
| Test end-to-end su progetto reale | 1 |
| **Deliverable** | Export .xlsx completo e funzionante |

#### Sprint 6 – AI Smart Mapping e Rifinitura (Settimane 13–14)

| Task | Effort (giorni) |
|---|---|
| SmartMappingService – integrazione LLM API (es. Claude/OpenAI) | 2 |
| UI suggerimenti nella TaggingView (tooltip/badge) | 1 |
| Gestione errori globale (logging, user-friendly messages) | 1 |
| DockablePane opzionale per accesso rapido | 2 |
| Documentazione tecnica (README, XML docs) | 1 |
| Installer (WiX o NSIS) con versioni 2022–2026 | 1 |
| **Deliverable** | Versione 1.0 production-ready |

### 10.3 Gantt Semplificato

```
Settimane:  1  2  3  4  5  6  7  8  9  10 11 12 13 14
Sprint 0:   ████
Sprint 1:         ████
Sprint 2:               ████
Sprint 3:                     ████
Sprint 4:                           ████
Sprint 5:                                 ████
Sprint 6:                                       ████
```

### 10.4 Rischi e Mitigazioni

| Rischio | Probabilità | Impatto | Mitigazione |
|---|---|---|---|
| Struttura XML .dcf non documentata ufficialmente | Alta | Media | Analisi empirica di file campione; contatto ACCA forum |
| Breaking changes API Revit 2026 | Media | Alta | Conditional compilation + test automatici per versione |
| Prestazioni lente su modelli grandi (>50.000 elem.) | Media | Alta | FilteredElementCollector con filtri rapidi; lazy loading |
| ES schema conflict tra versioni del plug-in | Bassa | Alta | Schema versioning con GUID distinti; migrazione automatica |
| Thread violation Revit API da WPF | Media | Alta | Tutti i write via ExternalEvent; no Revit API in background thread |

---

## 11. Considerazioni per la Compliance BIM Italia

- **ISO 19650 e UNI PdR 74:2019**: i parametri QTO (`QTO_Codice`, `QTO_Descrizione`) devono essere inseriti nel Piano di Gestione Informativa (PGI) come parametri di progetto documentati.
- **Prezzari Regionali**: ACCA distribuisce tutti i prezzari regionali italiani in formato DCF; testare il parser con almeno tre prezzari (es. Toscana, Lombardia, DEI) prima della release.
- **Esportazione per la SA (Stazione Appaltante)**: il formato di output Excel deve essere conforme alla struttura della tabella R.7 del DM 312/2021 (Quadro Economico / Computo Metrico Estimativo).
- **Tracciabilità**: ogni cella del file esportato deve riportare il `ElementId` Revit come riferimento, per permettere audit trail tra il file .rvt e il computo.

---

## 12. Riferimenti Tecnici

- Revit API Docs: https://www.revitapidocs.com/
- Autodesk Developer Blog – ForgeTypeId and Units Revisited: https://blog.autodesk.io/forgetypeid-and-units-revisited/
- archi-lab.net – Handling Revit 2022 Unit Changes: https://archi-lab.net/handling-the-revit-2022-unit-changes/
- archi-lab.net – Extensible Storage: https://archi-lab.net/what-why-and-how-of-the-extensible-storage/
- GitHub – NCalc: https://github.com/ncalc/ncalc
- EasyRevitAPI.com – MVVM Pattern for Revit: https://easyrevitapi.com/index.php/2023/11/01/m-v-vm-pattern-for-revit-part-2/
- LearnRevitAPI.com – Automate Shared Parameters: https://www.learnrevitapi.com/newsletter/automate-shared-parameters
- LearnRevitAPI.com – Override Graphics in View: https://www.learnrevitapi.com/blog/override-graphics-in-view
- Building Coder – External Events: https://jeremytammik.github.io/tbc/a/0743_external_event.htm
- Revit API Forum – .NET 8 Migration: https://forums.autodesk.com/t5/revit-api-forum/net-8-migration/td-p/13301211
