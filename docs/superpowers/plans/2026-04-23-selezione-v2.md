# Selezione v2 — Filtri Parametrici + 3 Sezioni UI

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Estendere la scheda Selezione esistente (v1) con regole parametriche composte (AND), conversione unità Revit, UI a 3 sezioni numerate (1 FASE, 2 CATEGORIA+NOME, 3 FILTRI PARAMETRICI), e cap risultati difensivo.

**Architecture:** Estensione incrementale di `SelectionService` esistente — aggiunta di `ParamFilterRule` + `ParamOperator` enum + logica `PassesAllRules`/`EvaluateRule` con `UnitUtils.ConvertFromInternalUnits`. Il VM guadagna `ObservableCollection<ParamFilterRuleVm>` e comandi `AddParamRule`/`RemoveParamRule` che triggerano la ricerca via debounce esistente. La UI viene riorganizzata in 3 sezioni con Separator tra di esse.

**Tech Stack:** C# netstandard2.0 + net48/net8-windows, WPF + CommunityToolkit.Mvvm, Revit API (ElementPhaseStatusFilter, UnitUtils.ConvertFromInternalUnits, LookupParameter).

---

## File Map

| File | Azione | Responsabilità |
|---|---|---|
| `QtoRevitPlugin/Services/SelectionService.cs` | Modifica | Enum `ParamOperator` + classe `ParamFilterRule` + overload `FindElements(... paramRules)` + logica `PassesAllRules`/`EvaluateRule` con conversione unità |
| `QtoRevitPlugin/UI/ViewModels/SelectionViewModel.cs` | Modifica | `ObservableCollection<ParamFilterRuleVm> ParamRules`, comandi `AddParamRule`/`RemoveParamRule`, property `HasParamRules`, passa rules a `FindElements` |
| `QtoRevitPlugin/UI/ViewModels/ParamFilterRuleVm.cs` | Crea | VM di una regola parametrica: ParameterName, Operator, Value, OperatorLabel bidirezionale, `ToModel()` |
| `QtoRevitPlugin/UI/Views/SelectionView.xaml` | Modifica | UI 3 sezioni numerate (FASE, CATEGORIA, FILTRI PARAMETRICI) con ItemsControl per regole, avviso unità, bottone "+ Aggiungi filtro" |

---

## Task 1: Enum + model `ParamFilterRule` + overload `FindElements`

**Files:**
- Modify: `QtoRevitPlugin/Services/SelectionService.cs`

- [ ] **Step 1: Aggiungi enum e model in testa al namespace (prima della classe)**

Apri `SelectionService.cs`. Individua il namespace `QtoRevitPlugin.Services`. Prima della dichiarazione di `public class SelectionService`, aggiungi:

```csharp
public enum ParamOperator
{
    Contains,
    Equals,
    NotEquals,
    GreaterThan,
    LessThan,
    GreaterOrEqual,
    LessOrEqual
}

/// <summary>
/// Regola filtro parametrica usata in Selezione v2. I valori sono in unità
/// DISPLAY del progetto (es. "0.30" per 30 cm). Il service converte in unità
/// interne Revit prima del confronto per StorageType.Double.
/// </summary>
public class ParamFilterRule
{
    public string ParameterName { get; set; } = "";
    public ParamOperator Operator { get; set; } = ParamOperator.Contains;
    public string Value { get; set; } = "";
}
```

- [ ] **Step 2: Aggiungi nuovo overload `FindElements` con paramRules + maxResults**

Nel metodo esistente `FindElements`, aggiungi un overload che accetta `IReadOnlyList<ParamFilterRule>? paramRules` e `int maxResults = 500`. Mantieni l'overload esistente (backward compat) che chiama il nuovo con `paramRules: null`.

```csharp
public IReadOnlyList<ElementRowInfo> FindElements(
    Document doc,
    BuiltInCategory? category,
    string? nameQuery,
    int? phaseFilterId,
    IReadOnlyList<ParamFilterRule>? paramRules,
    int maxResults = 500)
{
    // ... logica esistente per collector + phase filter ...

    var elements = collector.ToElements();

    var nameQueryLower = nameQuery?.Trim().ToLowerInvariant();
    var activeRules = paramRules?
        .Where(r => !string.IsNullOrWhiteSpace(r.ParameterName)
                 && !string.IsNullOrWhiteSpace(r.Value))
        .ToList();

    var result = new List<ElementRowInfo>(System.Math.Min(elements.Count, maxResults));

    foreach (var el in elements)
    {
        if (result.Count >= maxResults) break;

        if (!string.IsNullOrEmpty(nameQueryLower))
        {
            if (!MatchesNameQuery(el, doc, nameQueryLower)) continue;
        }

        if (activeRules != null && activeRules.Count > 0)
        {
            if (!PassesAllRules(el, activeRules)) continue;
        }

        result.Add(ToRowInfo(el, doc));
    }

    return result;
}
```

CRITICAL: adatta questo alla struttura esistente del metodo. Se v1 già ha il loop con `MatchesNameQuery`, aggiungi solo il check `PassesAllRules` e il cap `maxResults`. Non duplicare codice.

- [ ] **Step 3: Aggiungi `MatchesNameQuery` (se mancante) e `PassesAllRules`/`ResolveParameter`/`ScanParameters`/`EvaluateRule`**

In SelectionService.cs, aggiungi i seguenti metodi privati statici:

```csharp
private static bool MatchesNameQuery(Element el, Document doc, string queryLower)
{
    string family = "", type = "";
    if (el is FamilyInstance fi)
    {
        family = fi.Symbol?.FamilyName ?? "";
        type = fi.Symbol?.Name ?? "";
    }
    else
    {
        var typeEl = doc.GetElement(el.GetTypeId()) as ElementType;
        family = typeEl?.FamilyName ?? "";
        type = typeEl?.Name ?? el.Name ?? "";
    }
    return (family + " " + type).ToLowerInvariant().Contains(queryLower);
}

private static bool PassesAllRules(Element el, List<ParamFilterRule> rules)
{
    foreach (var rule in rules)
    {
        var param = ResolveParameter(el, rule.ParameterName);
        if (param == null || !param.HasValue) return false;
        if (!EvaluateRule(param, rule)) return false;
    }
    return true;
}

private static Parameter? ResolveParameter(Element el, string name)
{
    var p = el.LookupParameter(name);
    if (p != null) return p;

    p = ScanParameters(el.Parameters, name);
    if (p != null) return p;

    var typeEl = el.Document.GetElement(el.GetTypeId());
    if (typeEl != null)
    {
        p = typeEl.LookupParameter(name);
        if (p != null) return p;

        p = ScanParameters(typeEl.Parameters, name);
        if (p != null) return p;
    }

    return null;
}

private static Parameter? ScanParameters(ParameterSet pset, string name)
{
    foreach (Parameter p in pset)
    {
        if (p.Definition?.Name != null &&
            string.Equals(p.Definition.Name, name, System.StringComparison.OrdinalIgnoreCase))
            return p;
    }
    return null;
}

private static bool EvaluateRule(Parameter param, ParamFilterRule rule)
{
    switch (param.StorageType)
    {
        case StorageType.String:
            return EvalString(param.AsString() ?? "", rule);

        case StorageType.Double:
        {
            if (!TryParseDouble(rule.Value, out double target)) return false;

            double rawVal = param.AsDouble();
            double displayVal = rawVal;
            try
            {
                var unitId = param.GetUnitTypeId();
                if (unitId != null && unitId != UnitTypeId.Custom)
                    displayVal = UnitUtils.ConvertFromInternalUnits(rawVal, unitId);
            }
            catch
            {
                displayVal = rawVal;
            }
            return EvalDouble(displayVal, rule.Operator, target);
        }

        case StorageType.Integer:
        {
            if (!int.TryParse(rule.Value, out int target)) return false;
            return EvalInt(param.AsInteger(), rule.Operator, target);
        }

        case StorageType.ElementId:
        {
            var refEl = param.Element.Document.GetElement(param.AsElementId());
            var name = refEl?.Name ?? param.AsValueString() ?? "";
            return EvalString(name, rule);
        }

        default:
            return true;
    }
}

private static bool EvalString(string val, ParamFilterRule rule) =>
    rule.Operator switch
    {
        ParamOperator.Contains  => val.IndexOf(rule.Value, System.StringComparison.OrdinalIgnoreCase) >= 0,
        ParamOperator.Equals    => string.Equals(val, rule.Value, System.StringComparison.OrdinalIgnoreCase),
        ParamOperator.NotEquals => !string.Equals(val, rule.Value, System.StringComparison.OrdinalIgnoreCase),
        _ => true
    };

private static bool EvalDouble(double val, ParamOperator op, double target) =>
    op switch
    {
        ParamOperator.Equals         => System.Math.Abs(val - target) < 1e-6,
        ParamOperator.NotEquals      => System.Math.Abs(val - target) >= 1e-6,
        ParamOperator.GreaterThan    => val > target,
        ParamOperator.LessThan       => val < target,
        ParamOperator.GreaterOrEqual => val >= target,
        ParamOperator.LessOrEqual    => val <= target,
        _ => true
    };

private static bool EvalInt(int val, ParamOperator op, int target) =>
    op switch
    {
        ParamOperator.Equals         => val == target,
        ParamOperator.NotEquals      => val != target,
        ParamOperator.GreaterThan    => val > target,
        ParamOperator.LessThan       => val < target,
        ParamOperator.GreaterOrEqual => val >= target,
        ParamOperator.LessOrEqual    => val <= target,
        _ => true
    };

private static bool TryParseDouble(string s, out double val) =>
    double.TryParse(s, System.Globalization.NumberStyles.Any,
                   System.Globalization.CultureInfo.InvariantCulture, out val);
```

**NOTE CROSS-TARGET**: `GetUnitTypeId()` è disponibile da Revit 2021+. Se il progetto targeta net48 con Revit 2022/2023, `GetUnitTypeId()` c'è. Il plan è safe. Se il compilatore lamenta assenza di `UnitTypeId.Custom` su qualche target, usa un try/catch più largo.

- [ ] **Step 4: Build cross-target**

```bash
cd "C:/Users/luigi.dattilo/OneDrive - GPA Ingegneria Srl/Documenti/RevitQTO"
dotnet build QtoRevitPlugin/QtoRevitPlugin.csproj -c "Debug R25"
```

Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add QtoRevitPlugin/Services/SelectionService.cs
git commit -m "feat(selezione T1): ParamOperator enum + ParamFilterRule + FindElements overload con filtri parametrici"
```

---

## Task 2: `ParamFilterRuleVm` + collezione + comandi in VM

**Files:**
- Create: `QtoRevitPlugin/UI/ViewModels/ParamFilterRuleVm.cs`
- Modify: `QtoRevitPlugin/UI/ViewModels/SelectionViewModel.cs`

- [ ] **Step 1: Crea `ParamFilterRuleVm`**

Crea `QtoRevitPlugin/UI/ViewModels/ParamFilterRuleVm.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using QtoRevitPlugin.Services;
using System;

namespace QtoRevitPlugin.UI.ViewModels
{
    /// <summary>
    /// VM di una regola parametrica composta. Osservabile per reagire ai cambiamenti
    /// dei campi con debounce nella ricerca. Converte l'enum ParamOperator in una
    /// label leggibile (contiene / = / ≠ / &gt; / &lt; / ≥ / ≤) per la ComboBox UI.
    /// </summary>
    public partial class ParamFilterRuleVm : ObservableObject
    {
        [ObservableProperty] private string _parameterName = "";
        [ObservableProperty] private ParamOperator _operator = ParamOperator.Contains;
        [ObservableProperty] private string _value = "";

        public static string[] OperatorLabels { get; } =
        {
            "contiene",
            "=",
            "≠",
            ">",
            "<",
            "≥",
            "≤"
        };

        /// <summary>Binding bidirezionale per la ComboBox operatore nel XAML.</summary>
        public string OperatorLabel
        {
            get => OperatorLabels[(int)Operator];
            set
            {
                var idx = Array.IndexOf(OperatorLabels, value);
                if (idx >= 0) Operator = (ParamOperator)idx;
            }
        }

        partial void OnOperatorChanged(ParamOperator value) => OnPropertyChanged(nameof(OperatorLabel));

        public ParamFilterRule ToModel() => new ParamFilterRule
        {
            ParameterName = ParameterName?.Trim() ?? "",
            Operator = Operator,
            Value = Value?.Trim() ?? ""
        };
    }
}
```

- [ ] **Step 2: Integra in `SelectionViewModel`**

Apri `SelectionViewModel.cs`. Aggiungi:

```csharp
public ObservableCollection<ParamFilterRuleVm> ParamRules { get; } = new ObservableCollection<ParamFilterRuleVm>();

[ObservableProperty] private bool _hasParamRules;
```

Nel constructor, dopo l'inizializzazione esistente, aggiungi:

```csharp
ParamRules.CollectionChanged += (_, _) => UpdateHasParamRules();
```

Aggiungi i metodi:

```csharp
[RelayCommand]
private void AddParamRule()
{
    var rule = new ParamFilterRuleVm();
    rule.PropertyChanged += (_, _) =>
    {
        // Usa lo stesso debounce della search (se esiste un _searchDebounce)
        // Altrimenti chiama Search() direttamente
        _searchDebounce?.Stop();
        _searchDebounce?.Start();
    };
    ParamRules.Add(rule);
    UpdateHasParamRules();
}

[RelayCommand]
private void RemoveParamRule(ParamFilterRuleVm? rule)
{
    if (rule == null) return;
    ParamRules.Remove(rule);
    UpdateHasParamRules();
    Search();
}

private void UpdateHasParamRules() =>
    HasParamRules = ParamRules.Any(r =>
        !string.IsNullOrWhiteSpace(r.ParameterName) &&
        !string.IsNullOrWhiteSpace(r.Value));
```

- [ ] **Step 3: Passa le rules a `_service.FindElements` in `Search()`**

Nel metodo `Search()` del VM, trova la chiamata `_service.FindElements(doc, ...)`. Estrai le regole attive:

```csharp
var rules = ParamRules
    .Where(r => !string.IsNullOrWhiteSpace(r.ParameterName) && !string.IsNullOrWhiteSpace(r.Value))
    .Select(r => r.ToModel())
    .ToList();

var results = _service.FindElements(
    doc,
    SelectedCategory?.Bic,
    NameQuery,
    phaseId,
    rules);
```

Aggiungi anche la label `rulesLabel` allo StatusMessage esistente:

```csharp
var rulesLabel = rules.Count > 0 ? $" · {rules.Count} filtro/i param." : "";
StatusMessage = $"...{phaseLabel}{rulesLabel} · {sw.ElapsedMilliseconds} ms";
```

Adatta al codice esistente di `Search()` — se il messaggio è formattato diversamente, integra solo la variabile `rulesLabel`.

- [ ] **Step 4: Build**

```bash
dotnet build QtoRevitPlugin/QtoRevitPlugin.csproj -c "Debug R25"
```

Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add QtoRevitPlugin/UI/ViewModels/ParamFilterRuleVm.cs QtoRevitPlugin/UI/ViewModels/SelectionViewModel.cs
git commit -m "feat(selezione T2): ParamFilterRuleVm + collezione ParamRules + comandi Add/Remove"
```

---

## Task 3: UI 3 sezioni in `SelectionView.xaml`

**Files:**
- Modify: `QtoRevitPlugin/UI/Views/SelectionView.xaml`

- [ ] **Step 1: Leggi struttura attuale**

Apri `SelectionView.xaml` per capire la struttura attuale (Grid.RowDefinitions, dove sono i controlli esistenti).

- [ ] **Step 2: Riorganizza il pannello filtri in 3 sezioni**

All'interno del `Border` o `StackPanel` che contiene i filtri (NON il DataGrid dei risultati), riorganizza il contenuto in:

```xml
<StackPanel>

    <!-- ── Livello 1: FASE ────────────────────────── -->
    <TextBlock Text="1 — FASE" Style="{DynamicResource SectionLabelStyle}" />
    <StackPanel Orientation="Horizontal" Margin="0,6,0,0">
        <CheckBox IsChecked="{Binding FilterByActivePhase, Mode=TwoWay}"
                  VerticalAlignment="Center" Margin="0,0,8,0" />
        <TextBlock Text="Filtra per fase attiva:" FontSize="11" VerticalAlignment="Center" Margin="0,0,6,0" />
        <Border Background="{DynamicResource PanelSubBrush}"
                BorderBrush="{DynamicResource EdgeLightBrush}" BorderThickness="1"
                CornerRadius="3" Padding="8,2">
            <TextBlock Text="{Binding ActivePhaseName}" FontSize="11" FontWeight="SemiBold"
                       Foreground="{DynamicResource BrandAccentDeepBrush}" />
        </Border>
    </StackPanel>

    <Border Height="1" Background="{DynamicResource EdgeLightBrush}" Margin="0,10,0,8" />

    <!-- ── Livello 2: CATEGORIA + NOME ─────────────── -->
    <TextBlock Text="2 — CATEGORIA" Style="{DynamicResource SectionLabelStyle}" />
    <Grid Margin="0,6,0,0">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="200" />
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>
        <TextBlock Grid.Column="0" Text="Categoria:" Margin="0,0,8,0" VerticalAlignment="Center" FontSize="11" />
        <ComboBox Grid.Column="1"
                  ItemsSource="{Binding Categories}"
                  SelectedItem="{Binding SelectedCategory, Mode=TwoWay}"
                  DisplayMemberPath="Label"
                  FontSize="11" Padding="6,3" />
        <TextBlock Grid.Column="2" Text="Famiglia/tipo:" Margin="12,0,8,0" VerticalAlignment="Center" FontSize="11" />
        <TextBox Grid.Column="3"
                 Text="{Binding NameQuery, UpdateSourceTrigger=PropertyChanged}"
                 FontSize="11" Padding="6,3" Background="White"
                 BorderBrush="{DynamicResource EdgeLightBrush}" BorderThickness="1" />
    </Grid>

    <Border Height="1" Background="{DynamicResource EdgeLightBrush}" Margin="0,10,0,8" />

    <!-- ── Livello 3: FILTRI PARAMETRICI ──────────── -->
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>
        <TextBlock Grid.Column="0" Text="3 — FILTRI PARAMETRICI"
                   Style="{DynamicResource SectionLabelStyle}" VerticalAlignment="Center" />
        <Button Grid.Column="1"
                Command="{Binding AddParamRuleCommand}"
                Content="+ Aggiungi filtro"
                Padding="8,3" FontSize="10" Cursor="Hand"
                Background="Transparent" Foreground="{DynamicResource InkDefaultBrush}"
                BorderBrush="{DynamicResource EdgeLightBrush}" BorderThickness="1" />
    </Grid>

    <ItemsControl ItemsSource="{Binding ParamRules}" Margin="0,6,0,0">
        <ItemsControl.ItemTemplate>
            <DataTemplate>
                <Grid Margin="0,3,0,0">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" MinWidth="120" />
                        <ColumnDefinition Width="90" />
                        <ColumnDefinition Width="*" MinWidth="80" />
                        <ColumnDefinition Width="22" />
                    </Grid.ColumnDefinitions>
                    <TextBox Grid.Column="0"
                             Text="{Binding ParameterName, UpdateSourceTrigger=PropertyChanged}"
                             FontSize="11" Padding="5,3"
                             BorderBrush="{DynamicResource EdgeLightBrush}" BorderThickness="1"
                             ToolTip="Nome esatto del parametro Revit (es: Spessore, Area, Materiale struttura)" />
                    <ComboBox Grid.Column="1" Margin="4,0"
                              ItemsSource="{x:Static vm:ParamFilterRuleVm.OperatorLabels}"
                              SelectedItem="{Binding OperatorLabel, Mode=TwoWay}"
                              FontSize="11" Padding="5,2" />
                    <TextBox Grid.Column="2"
                             Text="{Binding Value, UpdateSourceTrigger=PropertyChanged}"
                             FontSize="11" Padding="5,3"
                             BorderBrush="{DynamicResource EdgeLightBrush}" BorderThickness="1"
                             ToolTip="Valore in unità di progetto (es: 0.30 per 30 cm, 10.5 per 10.5 m²)" />
                    <Button Grid.Column="3" Margin="4,0,0,0"
                            Command="{Binding DataContext.RemoveParamRuleCommand,
                                      RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                            CommandParameter="{Binding}"
                            Content="×" FontSize="13" FontWeight="Bold"
                            Background="Transparent" Foreground="{DynamicResource InkMutedBrush}"
                            BorderThickness="0" Cursor="Hand" ToolTip="Rimuovi filtro" />
                </Grid>
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>

    <TextBlock Margin="0,5,0,0" FontSize="10" FontStyle="Italic"
               Foreground="{DynamicResource InkMutedBrush}" TextWrapping="Wrap">
        <TextBlock.Style>
            <Style TargetType="TextBlock">
                <Setter Property="Visibility" Value="Collapsed" />
                <Style.Triggers>
                    <DataTrigger Binding="{Binding HasParamRules}" Value="True">
                        <Setter Property="Visibility" Value="Visible" />
                    </DataTrigger>
                </Style.Triggers>
            </Style>
        </TextBlock.Style>
        ⚠ Valori numerici in unità di progetto (lunghezze in m, aree in m²). Testi case-insensitive.
    </TextBlock>

</StackPanel>
```

IMPORTANTE: adatta alla struttura esistente. Se il view esistente ha già checkbox FASE + ComboBox CATEGORIA + TextBox NOME separatamente senza header numerati, semplicemente:
1. Aggiungi i TextBlock header "1 — FASE", "2 — CATEGORIA", "3 — FILTRI PARAMETRICI"
2. Aggiungi i Separator Border tra le sezioni
3. Aggiungi la sezione 3 completa (bottone + ItemsControl + avviso) — è nuova

Preserva tutto ciò che c'era prima nelle sezioni 1 e 2.

Assicurati che il namespace `vm` sia dichiarato sul root `<UserControl>`:
```xml
xmlns:vm="clr-namespace:QtoRevitPlugin.UI.ViewModels"
```

- [ ] **Step 3: Build**

```bash
dotnet build QtoRevitPlugin/QtoRevitPlugin.csproj -c "Debug R25"
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add QtoRevitPlugin/UI/Views/SelectionView.xaml
git commit -m "feat(selezione T3): UI 3 sezioni numerate (Fase / Categoria / Filtri parametrici) + avviso unità"
```

---

## Task 4: Build finale + test regression

- [ ] **Step 1: Run full test suite**

```bash
cd "C:/Users/luigi.dattilo/OneDrive - GPA Ingegneria Srl/Documenti/RevitQTO"
dotnet test QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj --nologo
```

Expected: 189 passed (no regression). Selezione v2 non ha test automatici nuovi — filtri parametrici richiedono documento Revit per testare realisticamente.

- [ ] **Step 2: Build Core Release**

```bash
dotnet build QtoRevitPlugin.Core/QtoRevitPlugin.Core.csproj -c Release
```

Expected: 0 errors.

- [ ] **Step 3: Commit finale**

```bash
git commit --allow-empty -m "chore(selezione): build finale - 189/189 test, 0 errors"
```

- [ ] **Step 4: Verifica manuale in Revit (separata)**

Dall'utente con Visual Studio + Revit 2025:

1. Apri un modello con muri (OST_Walls)
2. Tab Selezione → sezione 1 FASE: checkbox spuntata, verifica nome fase attiva mostrato
3. Sezione 2: scegli "Muri" → la DataGrid si popola
4. Digita un nome famiglia → filtra
5. Sezione 3: click "+ Aggiungi filtro" → appare una riga con TextBox + ComboBox + TextBox + "×"
6. Digita `Spessore` nel primo, scegli `>`, digita `0.25` nel valore → DataGrid mostra solo muri spessi > 25 cm
7. Aggiungi una seconda regola: `Materiale struttura` + `contiene` + `CLS` → AND tra le due
8. Click "×" su una regola → sparisce, ricerca si aggiorna
9. Verifica avviso unità visibile solo quando c'è almeno una regola attiva
10. Bottone "Isola"/"Nascondi"/"Reset" continuano a funzionare

---

## Note implementative

### Cross-target Revit
- `UnitUtils.ConvertFromInternalUnits(double, ForgeTypeId)` disponibile da Revit 2021+ — entrambi i target (net48/R2024 e net8/R2025+) lo supportano.
- `Parameter.GetUnitTypeId()` idem — no condizionali cross-target necessari.

### Performance
- Cap `maxResults = 500` protegge la UI da scan multi-migliaia su `OST_GenericModel`.
- Le regole parametriche si eseguono IN-MEMORY dopo collector → cost O(n·r) dove n=elementi, r=regole. 500 elementi × 3 regole = 1500 `LookupParameter` = trascurabile (< 100 ms).

### Debounce
- Il VM esistente ha già un `DispatcherTimer` per la ricerca testuale. Le regole parametriche scatenano lo stesso debounce via `rule.PropertyChanged` handler. No nuovo timer.

### Test
- Logica `EvaluateRule`/`EvalDouble`/`EvalString`/`EvalInt` è pura (no Revit API sul path dei test). Potrebbero essere testati in isolamento. Per ora si privilegia la verifica manuale in Revit.
