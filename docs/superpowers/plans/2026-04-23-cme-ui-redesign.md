# CME UI Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the new task-first CME home, phase-bound selection workflow, selection popout, and hybrid listino/favorites experience without breaking existing listino and export behavior.

**Architecture:** Keep the existing dock and module boundaries, but introduce a new home screen, move phase control into the selection filter panel, and add a small set of testable Core policy classes so the new workflow rules are covered by xUnit. UI changes stay in `QtoRevitPlugin`, while portable favorites and hybrid search rules live in `QtoRevitPlugin.Core`.

**AI Compatibility Guardrails:** The AI layer stays optional and untouched. Do not change `IQtoAiProvider`, provider/factory contracts, embedding cache responsibilities, or deterministic fallback paths. Any new workflow/search policy added in `QtoRevitPlugin.Core` must remain AI-neutral so the AI workstream can compose with it later instead of being coupled to UI state or Revit-facing view models.

**Tech Stack:** WPF, MVVM with `CommunityToolkit.Mvvm`, Revit API, SQLite, xUnit, FluentAssertions

---

## File Structure

### Core logic and persistence

- Create: `QtoRevitPlugin.Core/Models/SelectionComputationMode.cs`
- Create: `QtoRevitPlugin.Core/Models/WorkflowAvailability.cs`
- Create: `QtoRevitPlugin.Core/Search/HybridSearchScope.cs`
- Create: `QtoRevitPlugin.Core/Search/HybridSearchScopeResolver.cs`
- Create: `QtoRevitPlugin.Core/Services/WorkflowStateEvaluator.cs`
- Modify: `QtoRevitPlugin.Core/Data/IQtoRepository.cs`
- Modify: `QtoRevitPlugin.Core/Data/FileFavoritesRepository.cs`
- Modify: `QtoRevitPlugin.Core/Models/FavoriteSet.cs`

### Architectural constraints

- Preserve existing module seams between `QtoRevitPlugin`, `QtoRevitPlugin.Core`, and the optional AI integration layer.
- Keep hybrid search resolution deterministic; AI semantic search may compose later but is not a dependency of this redesign.
- Avoid changing persistence shapes that future AI features are expected to consume unless the change is additive and backward-compatible.
- Prefer additive view-model state over renaming/removing existing public members that adjacent modules may already depend on.

### Plugin UI shell and home

- Create: `QtoRevitPlugin/UI/Views/HomeView.xaml`
- Create: `QtoRevitPlugin/UI/Views/HomeView.xaml.cs`
- Modify: `QtoRevitPlugin/UI/ViewModels/DockablePaneViewModel.cs`
- Modify: `QtoRevitPlugin/UI/Panes/QtoDockablePane.xaml`
- Modify: `QtoRevitPlugin/UI/Panes/QtoDockablePane.xaml.cs`

### Selection and phase integration

- Modify: `QtoRevitPlugin/Services/SelectionService.cs`
- Modify: `QtoRevitPlugin/UI/ViewModels/SelectionViewModel.cs`
- Modify: `QtoRevitPlugin/UI/Views/SelectionView.xaml`
- Modify: `QtoRevitPlugin/UI/Views/SelectionView.xaml.cs`
- Modify: `QtoRevitPlugin/UI/ViewModels/PhaseFilterViewModel.cs`
- Modify: `QtoRevitPlugin/UI/Views/PhaseFilterView.xaml`
- Modify: `QtoRevitPlugin/UI/Views/PhaseFilterView.xaml.cs`

### Listino and favorites UI

- Modify: `QtoRevitPlugin/UI/ViewModels/SetupViewModel.cs`
- Modify: `QtoRevitPlugin/UI/Views/SetupListinoView.xaml`
- Modify: `QtoRevitPlugin/UI/Views/SetupListinoView.xaml.cs`
- Modify: `QtoRevitPlugin/UI/Views/SetupView.xaml`

### Phase-bound refresh

- Modify: `QtoRevitPlugin/UI/Views/MappingView.xaml.cs`
- Modify: `QtoRevitPlugin/UI/ViewModels/MappingViewModel.cs`
- Modify: `QtoRevitPlugin/UI/Views/PreviewView.xaml`
- Modify: `QtoRevitPlugin/UI/Views/PreviewView.xaml.cs`
- Modify: `QtoRevitPlugin/UI/ViewModels/ComputoStructureViewModel.cs`

### Tests

- Create: `QtoRevitPlugin.Tests/Sprint11/WorkflowStateEvaluatorTests.cs`
- Create: `QtoRevitPlugin.Tests/Sprint11/HybridSearchScopeResolverTests.cs`
- Modify: `QtoRevitPlugin.Tests/Sprint7/FavoritesRepositoryTests.cs`

---

### Task 1: Add Core workflow and search policy classes

**Files:**
- Create: `QtoRevitPlugin.Core/Models/SelectionComputationMode.cs`
- Create: `QtoRevitPlugin.Core/Models/WorkflowAvailability.cs`
- Create: `QtoRevitPlugin.Core/Search/HybridSearchScope.cs`
- Create: `QtoRevitPlugin.Core/Search/HybridSearchScopeResolver.cs`
- Create: `QtoRevitPlugin.Core/Services/WorkflowStateEvaluator.cs`
- Test: `QtoRevitPlugin.Tests/Sprint11/WorkflowStateEvaluatorTests.cs`
- Test: `QtoRevitPlugin.Tests/Sprint11/HybridSearchScopeResolverTests.cs`

- [ ] **Step 1: Write the failing workflow-state tests**

```csharp
using FluentAssertions;
using QtoRevitPlugin.Services;
using Xunit;

namespace QtoRevitPlugin.Tests.Sprint11
{
    public class WorkflowStateEvaluatorTests
    {
        [Fact]
        public void Evaluate_NoSession_EnablesOnlyStartupActions()
        {
            var evaluator = new WorkflowStateEvaluator();

            var state = evaluator.Evaluate(hasActiveSession: false, hasActivePriceList: false);

            state.CanOpenSetup.Should().BeFalse();
            state.CanOpenSelection.Should().BeFalse();
            state.PrimaryMessage.Should().Be("Per iniziare serve un computo attivo");
        }

        [Fact]
        public void Evaluate_SessionWithoutPriceList_BlocksSelectionButNotListino()
        {
            var evaluator = new WorkflowStateEvaluator();

            var state = evaluator.Evaluate(hasActiveSession: true, hasActivePriceList: false);

            state.CanOpenSetup.Should().BeTrue();
            state.CanOpenListino.Should().BeTrue();
            state.CanOpenSelection.Should().BeFalse();
            state.SecondaryMessage.Should().Contain("listino");
        }
    }
}
```

- [ ] **Step 2: Run workflow-state tests to verify they fail**

Run: `dotnet test QtoRevitPlugin.Tests --filter FullyQualifiedName~WorkflowStateEvaluatorTests`
Expected: FAIL with missing `WorkflowStateEvaluator` and `WorkflowAvailability`

- [ ] **Step 3: Write the failing hybrid-search tests**

```csharp
using FluentAssertions;
using QtoRevitPlugin.Search;
using Xunit;

namespace QtoRevitPlugin.Tests.Sprint11
{
    public class HybridSearchScopeResolverTests
    {
        [Fact]
        public void Resolve_NoActivePriceList_AllUsesProjectAndPersonalFavoritesOnly()
        {
            var resolver = new HybridSearchScopeResolver();

            var resolved = resolver.Resolve(HybridSearchScope.All, hasActivePriceList: false);

            resolved.UseActivePriceList.Should().BeFalse();
            resolved.UseProjectFavorites.Should().BeTrue();
            resolved.UsePersonalFavorites.Should().BeTrue();
        }

        [Fact]
        public void Resolve_ActivePriceList_AllUsesEveryAvailableCorpus()
        {
            var resolver = new HybridSearchScopeResolver();

            var resolved = resolver.Resolve(HybridSearchScope.All, hasActivePriceList: true);

            resolved.UseActivePriceList.Should().BeTrue();
            resolved.UseProjectFavorites.Should().BeTrue();
            resolved.UsePersonalFavorites.Should().BeTrue();
        }
    }
}
```

- [ ] **Step 4: Run hybrid-search tests to verify they fail**

Run: `dotnet test QtoRevitPlugin.Tests --filter FullyQualifiedName~HybridSearchScopeResolverTests`
Expected: FAIL with missing `HybridSearchScopeResolver` and `HybridSearchScope`

- [ ] **Step 5: Write minimal Core implementation**

```csharp
// QtoRevitPlugin.Core/Models/SelectionComputationMode.cs
namespace QtoRevitPlugin.Models
{
    public enum SelectionComputationMode
    {
        NewAndExisting,
        Demolitions
    }
}

// QtoRevitPlugin.Core/Models/WorkflowAvailability.cs
namespace QtoRevitPlugin.Models
{
    public class WorkflowAvailability
    {
        public bool CanOpenSetup { get; set; }
        public bool CanOpenListino { get; set; }
        public bool CanOpenSelection { get; set; }
        public string PrimaryMessage { get; set; } = "";
        public string SecondaryMessage { get; set; } = "";
    }
}

// QtoRevitPlugin.Core/Search/HybridSearchScope.cs
namespace QtoRevitPlugin.Search
{
    public enum HybridSearchScope
    {
        All,
        ActivePriceList,
        ProjectFavorites,
        PersonalFavorites
    }

    public class ResolvedHybridSearchScope
    {
        public bool UseActivePriceList { get; set; }
        public bool UseProjectFavorites { get; set; }
        public bool UsePersonalFavorites { get; set; }
    }
}

// QtoRevitPlugin.Core/Search/HybridSearchScopeResolver.cs
namespace QtoRevitPlugin.Search
{
    public class HybridSearchScopeResolver
    {
        public ResolvedHybridSearchScope Resolve(HybridSearchScope scope, bool hasActivePriceList)
        {
            return scope switch
            {
                HybridSearchScope.ActivePriceList => new ResolvedHybridSearchScope
                {
                    UseActivePriceList = hasActivePriceList
                },
                HybridSearchScope.ProjectFavorites => new ResolvedHybridSearchScope
                {
                    UseProjectFavorites = true
                },
                HybridSearchScope.PersonalFavorites => new ResolvedHybridSearchScope
                {
                    UsePersonalFavorites = true
                },
                _ => new ResolvedHybridSearchScope
                {
                    UseActivePriceList = hasActivePriceList,
                    UseProjectFavorites = true,
                    UsePersonalFavorites = true
                }
            };
        }
    }
}

// QtoRevitPlugin.Core/Services/WorkflowStateEvaluator.cs
using QtoRevitPlugin.Models;

namespace QtoRevitPlugin.Services
{
    public class WorkflowStateEvaluator
    {
        public WorkflowAvailability Evaluate(bool hasActiveSession, bool hasActivePriceList)
        {
            if (!hasActiveSession)
            {
                return new WorkflowAvailability
                {
                    PrimaryMessage = "Per iniziare serve un computo attivo",
                    SecondaryMessage = "Crea o apri un file .cme per attivare il workflow CME"
                };
            }

            return new WorkflowAvailability
            {
                CanOpenSetup = true,
                CanOpenListino = true,
                CanOpenSelection = hasActivePriceList,
                PrimaryMessage = hasActivePriceList
                    ? "Workflow pronto per selezione e tagging"
                    : "Attiva un listino per procedere con la selezione",
                SecondaryMessage = hasActivePriceList
                    ? "Puoi procedere con selezione, tagging e verifica"
                    : "Importa o attiva un listino prima di selezionare gli elementi"
            };
        }
    }
}
```

- [ ] **Step 6: Run the new tests to verify they pass**

Run: `dotnet test QtoRevitPlugin.Tests --filter FullyQualifiedName~Sprint11`
Expected: PASS for `WorkflowStateEvaluatorTests` and `HybridSearchScopeResolverTests`

- [ ] **Step 7: Commit**

```bash
git add QtoRevitPlugin.Core/Models/SelectionComputationMode.cs QtoRevitPlugin.Core/Models/WorkflowAvailability.cs QtoRevitPlugin.Core/Search/HybridSearchScope.cs QtoRevitPlugin.Core/Search/HybridSearchScopeResolver.cs QtoRevitPlugin.Core/Services/WorkflowStateEvaluator.cs QtoRevitPlugin.Tests/Sprint11/WorkflowStateEvaluatorTests.cs QtoRevitPlugin.Tests/Sprint11/HybridSearchScopeResolverTests.cs
git commit -m "feat: add workflow and hybrid search policies"
```

### Task 2: Extend favorites persistence to project and personal scopes

**Files:**
- Modify: `QtoRevitPlugin.Core/Models/FavoriteSet.cs`
- Modify: `QtoRevitPlugin.Core/Data/FileFavoritesRepository.cs`
- Modify: `QtoRevitPlugin.Core/Data/IQtoRepository.cs`
- Modify: `QtoRevitPlugin.Tests/Sprint7/FavoritesRepositoryTests.cs`

- [ ] **Step 1: Write the failing favorites persistence tests**

```csharp
[Fact]
public void SaveForProject_StoresProjectFavoritesInProjectScopedFile()
{
    var globalDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    var projectDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    Directory.CreateDirectory(globalDir);
    Directory.CreateDirectory(projectDir);
    var cmePath = Path.Combine(projectDir, "demo.cme");

    try
    {
        var repo = new FileFavoritesRepository(globalDir);
        repo.SaveForProject(cmePath, new FavoriteSet { Name = "Progetto" });

        File.Exists(Path.Combine(projectDir, "favorites.project.json")).Should().BeTrue();
    }
    finally
    {
        Directory.Delete(globalDir, true);
        Directory.Delete(projectDir, true);
    }
}

[Fact]
public void SaveGlobal_StoresPersonalFavoritesInPersonalFile()
{
    var globalDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    Directory.CreateDirectory(globalDir);

    try
    {
        var repo = new FileFavoritesRepository(globalDir);
        repo.SaveGlobal(new FavoriteSet { Name = "Personali" });

        File.Exists(Path.Combine(globalDir, "favorites.personal.json")).Should().BeTrue();
    }
    finally
    {
        Directory.Delete(globalDir, true);
    }
}
```

- [ ] **Step 2: Run favorites tests to verify they fail**

Run: `dotnet test QtoRevitPlugin.Tests --filter FullyQualifiedName~FavoritesRepositoryTests`
Expected: FAIL because files are currently `default.json` and `favorites.json`

- [ ] **Step 3: Implement the new file layout and minimal model metadata**

```csharp
// QtoRevitPlugin.Core/Models/FavoriteSet.cs
namespace QtoRevitPlugin.Models
{
    public enum FavoriteScope
    {
        Project,
        Personal
    }

    public class FavoriteSet
    {
        public string Name { get; set; } = "";
        public FavoriteScope Scope { get; set; } = FavoriteScope.Personal;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public List<FavoriteItem> Items { get; set; } = new();
    }
}

// QtoRevitPlugin.Core/Data/FileFavoritesRepository.cs
public FavoriteSet LoadGlobal()
{
    var path = Path.Combine(_globalDir, "favorites.personal.json");
    return LoadFromFile(path) ?? new FavoriteSet { Scope = FavoriteScope.Personal };
}

public void SaveGlobal(FavoriteSet set)
{
    Directory.CreateDirectory(_globalDir);
    set.Scope = FavoriteScope.Personal;
    var path = Path.Combine(_globalDir, "favorites.personal.json");
    File.WriteAllText(path, JsonSerializer.Serialize(set, JsonOptions));
}

public FavoriteSet? LoadForProject(string cmePath)
{
    var dir = Path.GetDirectoryName(cmePath);
    if (string.IsNullOrEmpty(dir)) return null;
    var path = Path.Combine(dir, "favorites.project.json");
    return LoadFromFile(path);
}

public void SaveForProject(string cmePath, FavoriteSet set)
{
    var dir = Path.GetDirectoryName(cmePath);
    if (string.IsNullOrEmpty(dir)) return;
    Directory.CreateDirectory(dir);
    set.Scope = FavoriteScope.Project;
    var path = Path.Combine(dir, "favorites.project.json");
    File.WriteAllText(path, JsonSerializer.Serialize(set, JsonOptions));
}
```

- [ ] **Step 4: Run favorites tests to verify they pass**

Run: `dotnet test QtoRevitPlugin.Tests --filter FullyQualifiedName~FavoritesRepositoryTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add QtoRevitPlugin.Core/Models/FavoriteSet.cs QtoRevitPlugin.Core/Data/FileFavoritesRepository.cs QtoRevitPlugin.Core/Data/IQtoRepository.cs QtoRevitPlugin.Tests/Sprint7/FavoritesRepositoryTests.cs
git commit -m "feat: split project and personal favorites persistence"
```

### Task 3: Introduce the new home screen and dock workflow state

**Files:**
- Create: `QtoRevitPlugin/UI/Views/HomeView.xaml`
- Create: `QtoRevitPlugin/UI/Views/HomeView.xaml.cs`
- Modify: `QtoRevitPlugin/UI/ViewModels/DockablePaneViewModel.cs`
- Modify: `QtoRevitPlugin/UI/Panes/QtoDockablePane.xaml`
- Modify: `QtoRevitPlugin/UI/Panes/QtoDockablePane.xaml.cs`
- Modify: `QtoRevitPlugin/UI/Views/SetupView.xaml`

- [ ] **Step 1: Build the failing shell by switching the empty-state target to a dedicated `HomeView`**

```csharp
// QtoRevitPlugin/UI/ViewModels/DockablePaneViewModel.cs
public enum QtoViewKey
{
    Home,
    Preview,
    Setup,
    Phase,
    Selection,
    Tagging,
    QtoViews,
    FilterManager,
    Health,
    Np,
    Export,
    ComputoStructure
}
```

Run: `dotnet build QtoRevitPlugin/QtoRevitPlugin.csproj -f net8.0-windows -c Debug /p:DeployToRevit=false`
Expected: FAIL because `HomeView` and updated switcher/view creation are missing

- [ ] **Step 2: Implement the minimal home view and dock integration**

```xml
<!-- QtoRevitPlugin/UI/Views/HomeView.xaml -->
<UserControl x:Class="QtoRevitPlugin.UI.Views.HomeView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid Background="{DynamicResource PanelBgBrush}">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <StackPanel Grid.Row="0" Margin="16,14,16,4">
            <TextBlock Text="Benvenuto in CME" Style="{DynamicResource ViewTitleStyle}" />
            <TextBlock Text="Scegli come iniziare"
                       Foreground="{DynamicResource InkMutedBrush}"
                       FontFamily="{DynamicResource FontMono}"
                       FontSize="10" />
        </StackPanel>

        <Border Grid.Row="1" Style="{DynamicResource CardStyle}" Margin="16,8,16,16">
            <StackPanel>
                <TextBlock Text="{Binding StatusMessage}" Style="{DynamicResource SectionLabelStyle}" />
                <StackPanel Orientation="Horizontal" Margin="0,12,0,0">
                    <Button x:Name="BtnNewSession" Content="Nuovo computo" Click="OnNewSessionClick" />
                    <Button x:Name="BtnOpenSession" Content="Apri computo esistente" Margin="8,0,0,0" Click="OnOpenSessionClick" />
                    <Button x:Name="BtnResumeLast" Content="Riprendi ultimo" Margin="8,0,0,0" Click="OnResumeLastClick" />
                </StackPanel>
            </StackPanel>
        </Border>
    </Grid>
</UserControl>
```

```csharp
// QtoRevitPlugin/UI/Panes/QtoDockablePane.xaml.cs
private UserControl CreateHomeView()
{
    return new HomeView();
}

private void UpdateActiveView()
{
    if (!_vm.HasActiveSession)
    {
        _noSessionView ??= CreateHomeView();
        ViewHost.Content = _noSessionView;
        foreach (var btn in _buttonCache.Values)
            btn.IsChecked = false;
        return;
    }
    // existing branch unchanged
}
```

- [ ] **Step 3: Rebuild to verify the shell compiles**

Run: `dotnet build QtoRevitPlugin/QtoRevitPlugin.csproj -f net8.0-windows -c Debug /p:DeployToRevit=false`
Expected: BUILD SUCCEEDED

- [ ] **Step 4: Manually verify the dock home in Revit**

Run: `dotnet build QtoRevitPlugin/QtoRevitPlugin.csproj -f net8.0-windows -c Debug`
Expected: plugin deploy completes and Revit shows the new home screen when no `.cme` is open

Manual checklist:
- `Nuovo computo`, `Apri computo esistente`, `Riprendi ultimo` are visible
- switcher is still present but visually secondary
- no old placeholder text references `Sessione ▾` as the only onboarding

- [ ] **Step 5: Commit**

```bash
git add QtoRevitPlugin/UI/Views/HomeView.xaml QtoRevitPlugin/UI/Views/HomeView.xaml.cs QtoRevitPlugin/UI/ViewModels/DockablePaneViewModel.cs QtoRevitPlugin/UI/Panes/QtoDockablePane.xaml QtoRevitPlugin/UI/Panes/QtoDockablePane.xaml.cs QtoRevitPlugin/UI/Views/SetupView.xaml
git commit -m "feat: add task-first CME home screen"
```

### Task 4: Move phase into Selection and add computation mode

**Files:**
- Modify: `QtoRevitPlugin/Services/SelectionService.cs`
- Modify: `QtoRevitPlugin/UI/ViewModels/SelectionViewModel.cs`
- Modify: `QtoRevitPlugin/UI/Views/SelectionView.xaml`
- Modify: `QtoRevitPlugin/UI/Views/SelectionView.xaml.cs`
- Modify: `QtoRevitPlugin/UI/ViewModels/PhaseFilterViewModel.cs`
- Modify: `QtoRevitPlugin/UI/Panes/QtoDockablePane.xaml.cs`

- [ ] **Step 1: Write the failing selection-mode compile change**

```csharp
// QtoRevitPlugin/UI/ViewModels/SelectionViewModel.cs
[ObservableProperty] private SelectionComputationMode _computationMode = SelectionComputationMode.NewAndExisting;
```

Run: `dotnet build QtoRevitPlugin/QtoRevitPlugin.csproj -f net8.0-windows -c Debug /p:DeployToRevit=false`
Expected: FAIL because `SelectionService.FindElements` does not accept computation mode yet

- [ ] **Step 2: Implement the minimal filter integration**

```csharp
// QtoRevitPlugin/Services/SelectionService.cs
public IReadOnlyList<ElementRowInfo> FindElements(
    Document doc,
    BuiltInCategory category,
    string? nameQuery,
    int? phaseFilterId,
    SelectionComputationMode computationMode)
{
    var collector = new FilteredElementCollector(doc)
        .OfCategory(category)
        .WhereElementIsNotElementType();

    if (phaseFilterId.HasValue && phaseFilterId.Value > 0)
    {
        var allowedStatuses = computationMode == SelectionComputationMode.Demolitions
            ? new List<ElementOnPhaseStatus> { ElementOnPhaseStatus.Demolished }
            : new List<ElementOnPhaseStatus> { ElementOnPhaseStatus.New, ElementOnPhaseStatus.Existing };

        var phaseFilter = new ElementPhaseStatusFilter(new ElementId((long)phaseFilterId.Value), allowedStatuses);
        collector = collector.WherePasses(phaseFilter);
    }

    // existing result materialization unchanged
}
```

```xml
<!-- QtoRevitPlugin/UI/Views/SelectionView.xaml -->
<TextBlock Grid.Row="0" Text="Fase Revit attiva" Style="{DynamicResource SectionLabelStyle}" />
<ComboBox Grid.Row="1"
          ItemsSource="{Binding AvailablePhases}"
          SelectedItem="{Binding SelectedPhase, Mode=TwoWay}"
          DisplayMemberPath="Name" />
<TextBlock Grid.Row="2" Text="Modalità computo" Style="{DynamicResource SectionLabelStyle}" />
<ComboBox Grid.Row="3"
          SelectedItem="{Binding ComputationMode, Mode=TwoWay}">
    <ComboBoxItem Content="Nuovo + Esistente" />
    <ComboBoxItem Content="Demolizioni" />
</ComboBox>
```

- [ ] **Step 3: Remove `Fasi` from the top-level switcher**

```csharp
// QtoRevitPlugin/UI/ViewModels/DockablePaneViewModel.cs
private void BuildViewList()
{
    Views.Add(new QtoViewItem(QtoViewKey.Home, "Home", "Avvio", 1));
    Views.Add(new QtoViewItem(QtoViewKey.Setup, "Setup", "§Fase 1", 2));
    Views.Add(new QtoViewItem(QtoViewKey.Selection, "Selezione", "§I3", 4));
    Views.Add(new QtoViewItem(QtoViewKey.Preview, "Preview", "§Fase 11", 1));
    Views.Add(new QtoViewItem(QtoViewKey.Tagging, "Tagging", "§I1·I2·I12·I13", 5));
    Views.Add(new QtoViewItem(QtoViewKey.Health, "Health", "§I5", 6));
    Views.Add(new QtoViewItem(QtoViewKey.FilterManager, "Filtri Vista", "§I11", 9));
    Views.Add(new QtoViewItem(QtoViewKey.QtoViews, "Viste CME", "§I14", 9));
    Views.Add(new QtoViewItem(QtoViewKey.Export, "Export", "§Fase 12", 9));
}
```

- [ ] **Step 4: Build to verify Selection compiles**

Run: `dotnet build QtoRevitPlugin/QtoRevitPlugin.csproj -f net8.0-windows -c Debug /p:DeployToRevit=false`
Expected: BUILD SUCCEEDED

- [ ] **Step 5: Manual verification in Revit**

Run: `dotnet build QtoRevitPlugin/QtoRevitPlugin.csproj -f net8.0-windows -c Debug`
Expected: plugin deploy completes

Manual checklist:
- no `Fasi` tab in the main switcher
- `Selezione` shows `Fase Revit attiva` inside the filter panel
- switching phase refreshes selection results
- changing `Modalità computo` toggles between `New + Existing` and `Demolizioni`

- [ ] **Step 6: Commit**

```bash
git add QtoRevitPlugin/Services/SelectionService.cs QtoRevitPlugin/UI/ViewModels/SelectionViewModel.cs QtoRevitPlugin/UI/Views/SelectionView.xaml QtoRevitPlugin/UI/Views/SelectionView.xaml.cs QtoRevitPlugin/UI/ViewModels/PhaseFilterViewModel.cs QtoRevitPlugin/UI/Panes/QtoDockablePane.xaml.cs
git commit -m "feat: move phase into selection workflow"
```

### Task 5: Add Selection popout and phase-bound refresh wiring

**Files:**
- Modify: `QtoRevitPlugin/UI/Views/SelectionView.xaml.cs`
- Modify: `QtoRevitPlugin/UI/ViewModels/MappingViewModel.cs`
- Modify: `QtoRevitPlugin/UI/Views/MappingView.xaml.cs`
- Modify: `QtoRevitPlugin/UI/Views/PreviewView.xaml`
- Modify: `QtoRevitPlugin/UI/Views/PreviewView.xaml.cs`
- Modify: `QtoRevitPlugin/UI/ViewModels/ComputoStructureViewModel.cs`

- [ ] **Step 1: Add the Selection popout regression target**

```csharp
// QtoRevitPlugin/UI/Views/SelectionView.xaml.cs
private void OnPopoutClick(object sender, RoutedEventArgs e)
    => PopoutWindow.Popout(new SelectionView(), "CME · Selezione");
```

Run: `dotnet build QtoRevitPlugin/QtoRevitPlugin.csproj -f net8.0-windows -c Debug /p:DeployToRevit=false`
Expected: BUILD SUCCEEDED

- [ ] **Step 2: Wire phase-bound refresh hooks**

```csharp
// QtoRevitPlugin/UI/ViewModels/MappingViewModel.cs
public void RefreshFromSession()
{
    var session = QtoApplication.Instance?.SessionManager?.ActiveSession;
    CurrentPhaseName = session?.ActivePhaseName ?? "";
    FamilyStatus = string.IsNullOrWhiteSpace(CurrentPhaseName)
        ? "Nessuna fase attiva."
        : $"Contesto fase: «{CurrentPhaseName}».";
}
```

```csharp
// QtoRevitPlugin/UI/ViewModels/ComputoStructureViewModel.cs
public void RefreshFromSession()
{
    LoadTree();
    StatusMessage = string.IsNullOrWhiteSpace(_sessionManager.ActiveSession?.ActivePhaseName)
        ? "Nessuna fase attiva."
        : $"Capitoli riallineati alla fase «{_sessionManager.ActiveSession.ActivePhaseName}».";
}
```

- [ ] **Step 3: Manually verify phase-bound refresh**

Run: `dotnet build QtoRevitPlugin/QtoRevitPlugin.csproj -f net8.0-windows -c Debug`
Expected: plugin deploy completes

Manual checklist:
- `Selezione` popout opens and stays synchronized with session state
- `Mapping/Tagging` shows the active phase context after phase switch
- `Preview` updates session-facing copy after phase switch
- `Struttura Computo` refreshes counts or status message after phase switch

- [ ] **Step 4: Commit**

```bash
git add QtoRevitPlugin/UI/Views/SelectionView.xaml.cs QtoRevitPlugin/UI/ViewModels/MappingViewModel.cs QtoRevitPlugin/UI/Views/MappingView.xaml.cs QtoRevitPlugin/UI/Views/PreviewView.xaml QtoRevitPlugin/UI/Views/PreviewView.xaml.cs QtoRevitPlugin/UI/ViewModels/ComputoStructureViewModel.cs
git commit -m "feat: add selection popout and phase-bound refresh"
```

### Task 6: Implement hybrid listino search and favorites UI

**Files:**
- Modify: `QtoRevitPlugin/UI/ViewModels/SetupViewModel.cs`
- Modify: `QtoRevitPlugin/UI/Views/SetupListinoView.xaml`
- Modify: `QtoRevitPlugin/UI/Views/SetupListinoView.xaml.cs`
- Modify: `QtoRevitPlugin/UI/Views/SetupView.xaml`

- [ ] **Step 1: Add scope selection and failing compile target**

```csharp
// QtoRevitPlugin/UI/ViewModels/SetupViewModel.cs
[ObservableProperty] private HybridSearchScope _selectedScope = HybridSearchScope.All;
public ObservableCollection<FavoriteItemRow> FavoriteResults { get; } = new();
```

Run: `dotnet build QtoRevitPlugin/QtoRevitPlugin.csproj -f net8.0-windows -c Debug /p:DeployToRevit=false`
Expected: FAIL because the new scope and favorite result types are not handled in `ExecuteSearch`

- [ ] **Step 2: Implement minimal hybrid search and favorite actions**

```csharp
private void ExecuteSearch()
{
    var resolver = new HybridSearchScopeResolver();
    var resolved = resolver.Resolve(SelectedScope, hasActivePriceList: PriceLists.Any(x => x.IsActive));

    SearchResults.Clear();
    FavoriteResults.Clear();

    if (resolved.UseActivePriceList && _searchService != null)
    {
        var result = _searchService.Search(SearchQuery, maxResults: 50);
        foreach (var item in result.Items)
            SearchResults.Add(new PriceItemRow(item));
    }

    if (resolved.UseProjectFavorites)
    {
        foreach (var item in LoadProjectFavorites().Items)
            FavoriteResults.Add(new FavoriteItemRow(item, "Proj"));
    }

    if (resolved.UsePersonalFavorites)
    {
        foreach (var item in LoadPersonalFavorites().Items)
            FavoriteResults.Add(new FavoriteItemRow(item, "Pers"));
    }
}
```

```xml
<!-- QtoRevitPlugin/UI/Views/SetupListinoView.xaml -->
<ComboBox ItemsSource="{Binding AvailableScopes}"
          SelectedItem="{Binding SelectedScope, Mode=TwoWay}" />
<TabControl>
    <TabItem Header="Ricerca">
        <DataGrid ItemsSource="{Binding SearchResults}" />
    </TabItem>
    <TabItem Header="Preferiti">
        <DataGrid ItemsSource="{Binding FavoriteResults}" />
    </TabItem>
</TabControl>
```

- [ ] **Step 3: Rebuild to verify the module compiles**

Run: `dotnet build QtoRevitPlugin/QtoRevitPlugin.csproj -f net8.0-windows -c Debug /p:DeployToRevit=false`
Expected: BUILD SUCCEEDED

- [ ] **Step 4: Manual verification of listino workflow**

Run: `dotnet build QtoRevitPlugin/QtoRevitPlugin.csproj -f net8.0-windows -c Debug`
Expected: plugin deploy completes

Manual checklist:
- `Listino` stays available as a standalone module
- `Ricerca` and `Preferiti` are always visible
- without active listino, scope `Tutti` searches favorites only
- with active listino, scope `Tutti` returns listino and favorite sources
- project and personal favorite actions save to separate files

- [ ] **Step 5: Commit**

```bash
git add QtoRevitPlugin/UI/ViewModels/SetupViewModel.cs QtoRevitPlugin/UI/Views/SetupListinoView.xaml QtoRevitPlugin/UI/Views/SetupListinoView.xaml.cs QtoRevitPlugin/UI/Views/SetupView.xaml
git commit -m "feat: add hybrid listino search and persistent favorites"
```

### Task 7: Full verification and cleanup

**Files:**
- Modify: any touched files from Tasks 1-6 only if verification exposes real regressions
- Test: `QtoRevitPlugin.Tests`

- [ ] **Step 1: Run the full Core test suite**

Run: `dotnet test QtoRevitPlugin.Tests`
Expected: PASS with all tests green

- [ ] **Step 2: Run plugin build verification**

Run: `dotnet build QtoRevitPlugin/QtoRevitPlugin.csproj -f net8.0-windows -c Debug /p:DeployToRevit=false`
Expected: BUILD SUCCEEDED

- [ ] **Step 3: Run manual smoke test in Revit**

Run: `dotnet build QtoRevitPlugin/QtoRevitPlugin.csproj -f net8.0-windows -c Debug`
Expected: deploy completes without MSBuild errors

Manual checklist:
- dock opens to the new home when no `.cme` is active
- `Listino` popout still works
- `Selezione` popout works
- `Fase` no longer exists as standalone tab
- phase switch updates `Selezione`, `Tagging`, `Preview`, `Struttura Computo`
- export window still opens from ribbon

- [ ] **Step 4: Commit final verification fixes**

```bash
git add .
git commit -m "feat: finalize CME UI redesign"
```