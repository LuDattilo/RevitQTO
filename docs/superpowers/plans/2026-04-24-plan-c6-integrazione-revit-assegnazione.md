# Plan C-6 — Integrazione Revit + Assegnazione EP (MeasurementService)

> **Contesto:** sesto sotto-progetto della spec `2026-04-24-modulo-computi-primus-xpwe-design.md`. Dipende da C-0 (schema) e C-2 (MeasurementService).

**Goal:** Aggiungere nella scheda Selezione un pannello "Assegna EP" che permette di taggare gli elementi Revit filtrati con la voce EP attualmente selezionata nel Listino. Crea un `MeasurementRow` (VCItem) + N `MeasurementSubRow` (RGItem), uno per ogni elemento Revit. QuantityMode radio (Conteggio / Area / Volume / Lunghezza) estrae automaticamente il valore geometrico.

**Architecture:**
- `SessionManager` guadagna 2 proprietà + 1 evento per comunicare la voce EP attiva cross-scheda (`ActiveEpCode`, `ActiveEpDescription`, `ActiveEpChanged`)
- Nuovo `RevitElementMeasurementReader` (in `QtoRevitPlugin`, quindi ha deps Revit API): dato un Element + QuantityMode, restituisce il valore numerico (area in m², volume in m³, lunghezza in m, conteggio=1)
- `SelectionViewModel` guadagna una card "Assegna EP" in fondo con: label EP corrente, radio QuantityMode, bottone Applica, preview live
- Click "Applica" → via Revit.Async (essere sul main thread) → `MeasurementService.CreateRow` + `AddOrUpdateSubRow` per ogni elemento
- Feedback in StatusMessage: "Assegnati N elementi alla voce X · totale Y m² · € Z"

**Vincoli:**
- **Non rompere** flusso esistente `QtoAssignment` (che lega elemento → PriceItem direttamente senza MeasurementRow/SubRow). C-6 scrive solo nel nuovo modello v12. Il migration dal vecchio al nuovo sarà rimandato.
- L'utente **non deve** aspettarsi che vedere un elemento "taggato" in Revit rifletta automaticamente i MeasurementSubRow creati da C-6. La visualizzazione live è una feature di C-5 (CmeEditor view).

**Tech Stack:** C# (model + service), Revit API (UnitUtils, ParameterId), WPF MVVM.

**File impattati:**
- Modify: `QtoRevitPlugin/Services/SessionManager.cs` (+ 2 property + 1 event)
- Create: `QtoRevitPlugin/Services/RevitElementMeasurementReader.cs`
- Modify: `QtoRevitPlugin/UI/ViewModels/SelectionViewModel.cs` (+ card Assegna EP)
- Modify: `QtoRevitPlugin/UI/Views/SelectionView.xaml` (+ card UI)
- Modify: `QtoRevitPlugin/UI/ViewModels/SetupViewModel.cs` o dove risiede la selezione voce nel Listino (propaga ActiveEp a SessionManager)
- Create: `QtoRevitPlugin.Tests/Computi/RevitElementMeasurementReaderTests.cs` (solo pure-C# tests, no Revit mockable: testiamo solo QuantityMode enum + edge cases numerici)

---

## Task 1: SessionManager · ActiveEpCode/Description + evento

**Files:**
- Modify: `QtoRevitPlugin/Services/SessionManager.cs`

- [ ] **Step 1: Leggere struttura eventi attuale**

```bash
grep -nE "public event|SessionChanged|public.*Repository" QtoRevitPlugin/Services/SessionManager.cs | head -10
```

- [ ] **Step 2: Aggiungere le 2 property + evento**

Dentro la classe `SessionManager`, dopo `SessionChanged`:

```csharp
// -------- Plan C-6: voce EP attiva (condivisa cross-scheda) --------

private string _activeEpCode = "";
public string ActiveEpCode
{
    get => _activeEpCode;
    private set
    {
        if (_activeEpCode == value) return;
        _activeEpCode = value ?? "";
        ActiveEpChanged?.Invoke(this, EventArgs.Empty);
    }
}

private string _activeEpDescription = "";
public string ActiveEpDescription
{
    get => _activeEpDescription;
    private set { _activeEpDescription = value ?? ""; }
}

/// <summary>
/// Emesso quando cambia la voce EP corrente (tipicamente selezione nel Listino).
/// Gli ascoltatori (SelectionView > card Assegna EP) si aggiornano.
/// </summary>
public event EventHandler? ActiveEpChanged;

/// <summary>Chiamato da ViewModel Listino quando cambia la selezione voce.</summary>
public void SetActiveEp(string code, string description)
{
    _activeEpDescription = description ?? "";
    ActiveEpCode = code ?? "";  // setter triggera evento
}
```

- [ ] **Step 3: Build**

```bash
dotnet build QtoRevitPlugin/QtoRevitPlugin.csproj -f net8.0-windows -c Debug -v q
```

Atteso: 0 errori.

## Task 2: RevitElementMeasurementReader

**Files:**
- Create: `QtoRevitPlugin/Services/RevitElementMeasurementReader.cs`

Responsabilità: dato un `Element` Revit + QuantityMode, restituisce il valore numerico in unità di progetto (m, m², m³) o 1 per Conteggio. Legge i parametri built-in Revit: `HOST_AREA_COMPUTED` (area), `HOST_VOLUME_COMPUTED` (volume), `CURVE_ELEM_LENGTH` / `INSTANCE_LENGTH_PARAM` (lunghezza).

- [ ] **Step 1: Creare file**

```csharp
using Autodesk.Revit.DB;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Estrae il valore numerico di misura da un Element Revit in base al QuantityMode.
    /// Ritorna valori in UNITÀ DI PROGETTO (es. metri, m², m³) — converte da piedi internal via UnitUtils.
    /// </summary>
    public class RevitElementMeasurementReader
    {
        /// <summary>
        /// Ritorna il valore di misura per l'elemento nel modo richiesto.
        /// null se il parametro non è presente o il mode è Count (ritorna sempre 1).
        /// </summary>
        public double? GetValue(Element element, QuantityMode mode)
        {
            if (element == null) return null;

            switch (mode)
            {
                case QuantityMode.Count:
                    return 1.0;

                case QuantityMode.Area:
                    return ReadDoubleParam(element, BuiltInParameter.HOST_AREA_COMPUTED, SpecTypeId.Area);

                case QuantityMode.Volume:
                    return ReadDoubleParam(element, BuiltInParameter.HOST_VOLUME_COMPUTED, SpecTypeId.Volume);

                case QuantityMode.Length:
                    // Prova prima INSTANCE_LENGTH_PARAM (instance), poi CURVE_ELEM_LENGTH (curve-based)
                    var inst = ReadDoubleParam(element, BuiltInParameter.INSTANCE_LENGTH_PARAM, SpecTypeId.Length);
                    if (inst.HasValue && inst.Value > 0) return inst;
                    return ReadDoubleParam(element, BuiltInParameter.CURVE_ELEM_LENGTH, SpecTypeId.Length);

                default:
                    return null;
            }
        }

        /// <summary>
        /// Legge un parametro Double (tipicamente quantità geometriche) e converte
        /// da unità interne Revit a unità di progetto. Ritorna null se non presente/valorizzato.
        /// </summary>
        private static double? ReadDoubleParam(Element el, BuiltInParameter bip, ForgeTypeId specType)
        {
            var p = el.get_Parameter(bip);
            if (p == null || !p.HasValue) return null;

            double raw = p.AsDouble();
            try
            {
                var unitId = el.Document.GetUnits().GetFormatOptions(specType).GetUnitTypeId();
                return UnitUtils.ConvertFromInternalUnits(raw, unitId);
            }
            catch
            {
                return raw;
            }
        }
    }

    public enum QuantityMode
    {
        Count,
        Area,
        Volume,
        Length
    }
}
```

**Nota:** esiste già un enum `QuantityMode` in `QtoRevitPlugin.Core/Models/QuantityMode.cs` — verifico prima di duplicare.

- [ ] **Step 2: Verificare enum esistente**

```bash
grep -A 10 "enum QuantityMode" QtoRevitPlugin.Core/Models/QuantityMode.cs
```

Se esiste già con gli stessi valori (Count/Area/Volume/Length), riuso quello in `QtoRevitPlugin.Models` namespace. Altrimenti aggiungo.

- [ ] **Step 3: Build**

```bash
dotnet build QtoRevitPlugin/QtoRevitPlugin.csproj -f net8.0-windows -c Debug -v q
```

Atteso: 0 errori.

## Task 3: SelectionViewModel · card "Assegna EP"

**Files:**
- Modify: `QtoRevitPlugin/UI/ViewModels/SelectionViewModel.cs`

Aggiunte:
- Property `ActiveEpCode`, `ActiveEpDescription` (sottoscritte a SessionManager.ActiveEpChanged)
- Property `QuantityMode` (default Area per Walls/Floors, Count altrimenti)
- Comando `ApplyEpCommand` che esegue l'assegnazione
- Property `AssignPreview` (live update: "N elementi · Qta tot X m² · € Y stimati")
- Property `CanApply` (CanExecute del comando)

- [ ] **Step 1: Aggiungere property al VM**

Dentro `SelectionViewModel` dopo la definizione dei filtri:

```csharp
// -------- Plan C-6: Assegnazione EP --------

[ObservableProperty] private string _activeEpCode = "";
[ObservableProperty] private string _activeEpDescription = "";
[ObservableProperty] private QuantityMode _quantityMode = QuantityMode.Count;
[ObservableProperty] private string _assignPreview = "";

/// <summary>Opzioni per ComboBox QuantityMode (label localizzata + enum).</summary>
public ObservableCollection<QuantityModeOption> QuantityModeOptions { get; } = new()
{
    new QuantityModeOption(QuantityMode.Count,  "Conteggio (cad)"),
    new QuantityModeOption(QuantityMode.Area,   "Area (m²)"),
    new QuantityModeOption(QuantityMode.Volume, "Volume (m³)"),
    new QuantityModeOption(QuantityMode.Length, "Lunghezza (m)")
};

public bool CanApply =>
    !string.IsNullOrWhiteSpace(ActiveEpCode) && Elements.Count > 0;

partial void OnActiveEpCodeChanged(string value)
{
    OnPropertyChanged(nameof(CanApply));
    ApplyEpCommand.NotifyCanExecuteChanged();
    UpdateAssignPreview();
}

partial void OnQuantityModeChanged(QuantityMode value) => UpdateAssignPreview();
```

- [ ] **Step 2: Sottoscrivere ActiveEpChanged dal SessionManager nel costruttore**

Nel costruttore del SelectionViewModel, dopo `SessionChanged += ...`:

```csharp
QtoApplication.Instance.SessionManager.ActiveEpChanged += (_, _) =>
{
    ActiveEpCode = QtoApplication.Instance.SessionManager.ActiveEpCode;
    ActiveEpDescription = QtoApplication.Instance.SessionManager.ActiveEpDescription;
};
// Pre-populate
ActiveEpCode = QtoApplication.Instance.SessionManager.ActiveEpCode;
ActiveEpDescription = QtoApplication.Instance.SessionManager.ActiveEpDescription;
```

- [ ] **Step 3: Aggiornare Search() per rifresh preview post filtri**

In fondo al metodo `Search()`, dopo aver popolato `Elements`:

```csharp
UpdateAssignPreview();
OnPropertyChanged(nameof(CanApply));
ApplyEpCommand.NotifyCanExecuteChanged();
```

- [ ] **Step 4: Implementare UpdateAssignPreview**

```csharp
private void UpdateAssignPreview()
{
    if (string.IsNullOrWhiteSpace(ActiveEpCode) || Elements.Count == 0)
    {
        AssignPreview = "";
        return;
    }

    var doc = QtoApplication.Instance?.CurrentUiApp?.ActiveUIDocument?.Document;
    if (doc == null) { AssignPreview = "Nessun documento"; return; }

    try
    {
        var reader = new RevitElementMeasurementReader();
        double total = 0;
        int counted = 0;
#if REVIT2025_OR_LATER
        foreach (var vm in Elements)
        {
            var el = doc.GetElement(new ElementId((long)vm.ElementId));
            var v = reader.GetValue(el, QuantityMode);
            if (v.HasValue) { total += v.Value; counted++; }
        }
#else
        foreach (var vm in Elements)
        {
            var el = doc.GetElement(new ElementId(vm.ElementId));
            var v = reader.GetValue(el, QuantityMode);
            if (v.HasValue) { total += v.Value; counted++; }
        }
#endif
        var unit = QuantityMode switch
        {
            QuantityMode.Area => "m²",
            QuantityMode.Volume => "m³",
            QuantityMode.Length => "m",
            _ => "pz"
        };
        AssignPreview = $"{counted} elementi · tot {total:N2} {unit}";
    }
    catch (Exception ex)
    {
        AssignPreview = $"Preview errata: {ex.Message}";
    }
}
```

- [ ] **Step 5: Implementare ApplyEpCommand**

```csharp
[RelayCommand(CanExecute = nameof(CanApply))]
private void ApplyEp()
{
    var repo = QtoApplication.Instance?.SessionManager?.Repository;
    var sess = QtoApplication.Instance?.SessionManager?.ActiveSession;
    var doc = QtoApplication.Instance?.CurrentUiApp?.ActiveUIDocument?.Document;
    if (repo == null || sess == null || doc == null)
    {
        StatusMessage = "Sessione o documento non disponibili.";
        return;
    }

    try
    {
        // 1. Garantisci ComputoDocument esistente
        var docSvc = new ComputoDocumentService(repo);
        var cmeDoc = docSvc.GetOrCreate(sess.Id);

        // 2. Risolvi PriceItem per Code
        var items = repo.GetPriceItems(
            repo.SearchPriceItemsByCode(ActiveEpCode).Select(r => r.Id).ToList());
        // Workaround: non abbiamo GetByCode singolo; usiamo SearchByCode + primo risultato exact-match
        var pi = items.FirstOrDefault(i => string.Equals(i.Code, ActiveEpCode, StringComparison.OrdinalIgnoreCase));
        if (pi == null)
        {
            StatusMessage = $"Voce '{ActiveEpCode}' non trovata nel listino.";
            return;
        }

        // 3. Crea VCItem
        var msvc = new MeasurementService(repo);
        var row = msvc.CreateRow(cmeDoc.Id, pi.Id);

        // 4. Per ciascun elemento filtrato → RGItem
        var reader = new RevitElementMeasurementReader();
        int addedCount = 0;
        double totalQty = 0;
        foreach (var elVm in Elements)
        {
#if REVIT2025_OR_LATER
            var el = doc.GetElement(new ElementId((long)elVm.ElementId));
#else
            var el = doc.GetElement(new ElementId(elVm.ElementId));
#endif
            if (el == null) continue;
            var v = reader.GetValue(el, QuantityMode) ?? 0;
            double partiUguali = 1;
            double? lung = QuantityMode == QuantityMode.Length ? (double?)v : null;
            double? larg = QuantityMode == QuantityMode.Area   ? (double?)v : null;
            double? hPeso = QuantityMode == QuantityMode.Volume ? (double?)v : null;
            // Strategia formula semplice: mettiamo il valore geometrico su una singola dimensione;
            // Lunghezza=v → Quantita=1*v=v, Area=v → Quantita=1*v=v (sola Larghezza attiva), ecc.
            // Per Count: lascia null tutte le dimensioni → Quantita=1.
            msvc.AddOrUpdateSubRow(
                row.Id, idvv: elVm.ElementId,
                descrizione: $"[{elVm.ElementId}] {elVm.FamilyName} · {elVm.TypeName}",
                partiUguali: partiUguali,
                lunghezza: lung, larghezza: larg, hPeso: hPeso);
            addedCount++;
            totalQty += v;
        }

        var unit = QuantityMode switch
        {
            QuantityMode.Area => "m²",
            QuantityMode.Volume => "m³",
            QuantityMode.Length => "m",
            _ => "pz"
        };
        StatusMessage = $"Assegnati {addedCount} elementi a '{ActiveEpCode}' · tot {totalQty:N2} {unit}";
    }
    catch (DomainValidationException dex)
    {
        StatusMessage = $"{dex.RuleCode}: {dex.Message}";
    }
    catch (Exception ex)
    {
        StatusMessage = $"Errore assegnazione: {ex.Message}";
    }
}
```

**Nota:** `SearchPriceItemsByCode` potrebbe non esistere nel repository esistente. Verifico e uso il metodo giusto (probabilmente `SemanticSearchPriceItems` o un query custom). Se non esiste nulla di adatto, aggiungo un helper semplice nel repo:

```csharp
public IReadOnlyList<PriceItem> GetPriceItemsByCode(string code)
{
    const string sql = @"SELECT p.*, pl.Name AS ListName FROM PriceItems p
                         JOIN PriceLists pl ON pl.Id = p.PriceListId
                         WHERE p.Code = @c AND pl.IsActive = 1;";
    return _conn.Query<PriceItemRow>(sql, new { c = code }).Select(r => r.ToPriceItem()).ToList();
}
```

- [ ] **Step 6: Aggiungere using per Services.Computi e Models.Computi in SelectionViewModel**

```csharp
using QtoRevitPlugin.Services.Computi;
using QtoRevitPlugin.Models;  // per QuantityMode se è in Core
```

- [ ] **Step 7: Build**

```bash
dotnet build QtoRevitPlugin/QtoRevitPlugin.csproj -f net8.0-windows -c Debug -v q
```

Atteso: 0 errori.

## Task 4: SelectionView.xaml · card Assegna EP

**Files:**
- Modify: `QtoRevitPlugin/UI/Views/SelectionView.xaml`

- [ ] **Step 1: Trovare la riga della status bar (Row="4")**

- [ ] **Step 2: Cambiare RowDefinitions da 5 a 6 righe (aggiungere la card prima della status bar)**

Vecchio:
```xml
<Grid.RowDefinitions>
    <RowDefinition Height="Auto" />
    <RowDefinition Height="Auto" />
    <RowDefinition Height="Auto" />
    <RowDefinition Height="*" />
    <RowDefinition Height="Auto" />
</Grid.RowDefinitions>
```

Nuovo:
```xml
<Grid.RowDefinitions>
    <RowDefinition Height="Auto" />   <!-- 0: titolo -->
    <RowDefinition Height="Auto" />   <!-- 1: filtri (ScrollViewer) -->
    <RowDefinition Height="Auto" />   <!-- 2: toolbar -->
    <RowDefinition Height="*" />      <!-- 3: DataGrid risultati -->
    <RowDefinition Height="Auto" />   <!-- 4: card Assegna EP (C-6) -->
    <RowDefinition Height="Auto" />   <!-- 5: status bar -->
</Grid.RowDefinitions>
```

E spostare il `Grid.Row="4"` della status bar → `Grid.Row="5"`.

- [ ] **Step 3: Inserire la card Assegna EP tra DataGrid e status bar**

```xml
<!-- Card ASSEGNA EP (C-6) · mostrata sempre, disabilitata se non c'è voce attiva -->
<Border Grid.Row="4" Margin="16,4,16,4" Padding="10,8"
        Background="{DynamicResource PanelSubBrush}"
        BorderBrush="{DynamicResource EdgeLightBrush}" BorderThickness="1" CornerRadius="4">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Riga 1: voce attiva + descrizione -->
        <StackPanel Grid.Row="0" Orientation="Horizontal">
            <TextBlock Text="ASSEGNA VOCE EP" FontSize="9" FontWeight="Bold"
                       Foreground="{DynamicResource InkMutedBrush}" VerticalAlignment="Center"/>
            <TextBlock Text="{Binding ActiveEpCode, StringFormat=' · {0}'}"
                       Margin="6,0,0,0" FontWeight="SemiBold" FontSize="11"
                       Foreground="{DynamicResource InkDefaultBrush}" VerticalAlignment="Center"/>
            <TextBlock Text="{Binding ActiveEpDescription}"
                       Margin="8,0,0,0" FontSize="10" FontStyle="Italic"
                       Foreground="{DynamicResource InkMutedBrush}"
                       VerticalAlignment="Center" TextTrimming="CharacterEllipsis" MaxWidth="400"/>
        </StackPanel>

        <!-- Riga 2: ComboBox QuantityMode — scelta unica, 1 riga sola, coerente con gli altri dropdown della scheda -->
        <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="0,6,0,0">
            <TextBlock Text="Quantità per istanza:" FontSize="10"
                       Foreground="{DynamicResource InkMutedBrush}"
                       VerticalAlignment="Center" Margin="0,0,8,0"/>
            <ComboBox Width="180"
                      Style="{DynamicResource CmbField}"
                      ItemsSource="{Binding QuantityModeOptions}"
                      SelectedValuePath="Mode"
                      DisplayMemberPath="Label"
                      SelectedValue="{Binding QuantityMode, Mode=TwoWay}"/>
        </StackPanel>

        <!-- Riga 3: preview + bottone -->
        <Grid Grid.Row="2" Margin="0,8,0,0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <TextBlock Grid.Column="0" Text="{Binding AssignPreview}"
                       FontSize="11" FontStyle="Italic"
                       Foreground="{DynamicResource BrandAccentDeepBrush}"
                       VerticalAlignment="Center"/>
            <Button Grid.Column="1" Content="Assegna agli elementi filtrati"
                    Command="{Binding ApplyEpCommand}"
                    Style="{DynamicResource BtnPrimary}"
                    Padding="10,5" FontSize="11"/>
        </Grid>
    </Grid>
</Border>
```

**Nota:** il binding `Converter={StaticResource EnumToBool}` richiede un converter WPF classico `EnumToBooleanConverter` nelle risorse del tema. Controllo se esiste:

```bash
grep -rnE "EnumToBoolean|EnumToBool" QtoRevitPlugin/Theme/
```

Se **non esiste**, semplifico: uso 4 RadioButton con event handler `Checked` nel code-behind che setta `_vm.QuantityMode`. Più pragmatico.

- [ ] **Step 4: Build**

```bash
dotnet build QtoRevitPlugin/QtoRevitPlugin.csproj -f net8.0-windows -c Debug -v q
```

## Task 5: Propagazione voce EP dal Listino al SessionManager

**Files:**
- Modify: `QtoRevitPlugin/UI/ViewModels/SetupViewModel.cs` (se è lì la selezione della voce listino)

L'utente seleziona una voce nel DataGrid ricerca ibrida o dai preferiti → il VM `SetupViewModel` probabilmente ha un `SelectedItem`. Quando cambia, chiama `SessionManager.SetActiveEp(code, desc)`.

- [ ] **Step 1: Trovare dove si gestisce la selezione voce**

```bash
grep -nE "SelectedPriceItem|SelectedResult|SelectedSearchItem" QtoRevitPlugin/UI/ViewModels/SetupViewModel.cs | head -10
```

- [ ] **Step 2: Aggiungere la chiamata a SetActiveEp nel setter/partial OnChanged**

Esempio:
```csharp
partial void OnSelectedSearchResultChanged(PriceItemRow? value)
{
    if (value == null) return;
    QtoApplication.Instance?.SessionManager?.SetActiveEp(value.Code, value.ShortDesc);
}
```

- [ ] **Step 3: Fare lo stesso per i Preferiti (se diversa property)**

## Task 6: Test + commit

- [ ] **Step 1: Aggiungere helper `GetPriceItemsByCode` al repository se non esiste**

Nel `IQtoRepository`:
```csharp
IReadOnlyList<PriceItem> GetPriceItemsByCode(string code);
```

Nel `QtoRepository`:
```csharp
public IReadOnlyList<PriceItem> GetPriceItemsByCode(string code)
{
    const string sql = @"SELECT p.*, pl.Name AS ListName FROM PriceItems p
                         JOIN PriceLists pl ON pl.Id = p.PriceListId
                         WHERE p.Code = @c AND pl.IsActive = 1;";
    return _conn.Query<PriceItemRow>(sql, new { c = code }).Select(r => r.ToPriceItem()).ToList();
}
```

- [ ] **Step 2: Full test suite**

```bash
dotnet test QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj --no-build -v quiet
```

Atteso: **486+ superati, 0 falliti.**

- [ ] **Step 3: Commit**

```bash
git add QtoRevitPlugin/Services/SessionManager.cs \
       QtoRevitPlugin/Services/RevitElementMeasurementReader.cs \
       QtoRevitPlugin/UI/ViewModels/SelectionViewModel.cs \
       QtoRevitPlugin/UI/Views/SelectionView.xaml \
       QtoRevitPlugin/UI/ViewModels/SetupViewModel.cs \
       QtoRevitPlugin.Core/Data/IQtoRepository.cs \
       QtoRevitPlugin.Core/Data/QtoRepository.cs
git commit -m "feat(assegnazione): Selezione → MeasurementRow + RGItem (Plan C-6)"
```

---

## Self-review

- [x] SessionManager guadagna 2 property + evento senza rompere pattern esistenti
- [x] RevitElementMeasurementReader isolato, testabile con un Element mock (non in questo plan)
- [x] UI card additiva in Selezione — non rompe filtri + DataGrid
- [x] Radio QuantityMode con default Count
- [x] Preview live: conta elementi + totale quantità con unità corretta
- [x] Idempotente: AddOrUpdateSubRow su IDVV>0 evita duplicati se l'utente clicca Applica 2 volte sullo stesso filtro

## Scope NON incluso

- Selezione Revit manuale (pick su 3D view) → C-6.1 se richiesto
- Undo/redo → Revit Transaction già lo fa lato utente
- Validazione sulla coerenza dimensioni-voce (es. voce in m² e utente sceglie Volume) → warning, non blocco; rimandato
- Multi-EP su stesso elemento (un elemento in più MeasurementSubRow con IDVV diverso) → già supportato dal modello (ogni MeasurementRow ha suo IDVV space), non serve lavoro extra
- Display dei MeasurementRow esistenti su un elemento quando lo rivedi → C-5 (CmeEditor view)
