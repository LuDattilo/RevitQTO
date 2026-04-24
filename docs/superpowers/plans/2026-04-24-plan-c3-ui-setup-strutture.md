# Plan C-3 — UI Setup · Strutture Computi (ChapterNode / CategoryNode / WbsNode)

> **Contesto:** quarto sotto-progetto della spec `2026-04-24-modulo-computi-primus-xpwe-design.md`. Dipende da C-0 (schema) e C-2 (domain services).

**Goal:** Esporre nella scheda Setup del plugin 3 tab aggiuntivi per gestire le nuove strutture classificatorie PriMus-compliant (Capitoli v12, Categorie v12, WBS). Il tab "Struttura Computo" attuale (basato su `ComputoChapter` legacy) resta invariato per backward-compat.

**Architecture:** 3 nuovi UserControl WPF nel pattern MVVM CommunityToolkit già usato nel progetto. Ogni ViewModel riceve il `IQtoRepository` tramite `QtoApplication.Instance`, istanzia i servizi C-2 al volo. TreeView nativa di WPF con `HierarchicalDataTemplate` per la gerarchia (nessuna dipendenza esterna tipo TreeListView).

**Tech Stack:** WPF, CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`), SetupView.xaml TabControl esistente come host.

**File impattati:**
- Create: `QtoRevitPlugin/UI/ViewModels/ChapterNodesViewModel.cs`
- Create: `QtoRevitPlugin/UI/ViewModels/CategoryNodesViewModel.cs`
- Create: `QtoRevitPlugin/UI/ViewModels/WbsNodesViewModel.cs`
- Create: `QtoRevitPlugin/UI/Views/ChapterNodesView.xaml(.cs)`
- Create: `QtoRevitPlugin/UI/Views/CategoryNodesView.xaml(.cs)`
- Create: `QtoRevitPlugin/UI/Views/WbsNodesView.xaml(.cs)`
- Modify: `QtoRevitPlugin/UI/Views/SetupView.xaml` (aggiunta 3 TabItem)

**NO UI test automated:** la verifica è manuale in Revit. Il codice dei ViewModel è coperto dai test C-2 sui servizi sottostanti. I file XAML sono verificati dal build WPF (XAML compile-time errors).

---

## Task 1: ChapterNodesViewModel

**Files:**
- Create: `QtoRevitPlugin/UI/ViewModels/ChapterNodesViewModel.cs`

Responsabilità:
- Espone `ObservableCollection<ChapterNodeVm>` ordinato ad albero
- Comandi: `AddSuperChapter`, `AddChapter` (sub del selezionato SpCap), `AddSubChapter` (sub del selezionato Cap), `DeleteSelected`, `Reload`
- Gestisce `SelectedNode` con calcolo `CanAddChildOfCurrent` basato su Level
- Form inline per nuovo nodo: Codice + DesSintetica

- [ ] **Step 1: Creare file ChapterNodesViewModel.cs**

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QtoRevitPlugin.Application;
using QtoRevitPlugin.Models.Computi;
using QtoRevitPlugin.Services.Computi;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace QtoRevitPlugin.UI.ViewModels
{
    /// <summary>
    /// Gestisce l'albero dei ChapterNode (SpCap → Cap → SbCap) per il Setup.
    /// Usa IChapterService (Plan C-2) per operazioni CRUD con validazioni.
    /// </summary>
    public partial class ChapterNodesViewModel : ViewModelBase
    {
        private int _documentId;

        public ObservableCollection<ChapterNodeVm> RootNodes { get; } = new();

        [ObservableProperty] private ChapterNodeVm? _selectedNode;
        [ObservableProperty] private string _newCodice = "";
        [ObservableProperty] private string _newDesSintetica = "";
        [ObservableProperty] private string _statusMessage = "Nessun documento attivo.";

        public ChapterNodesViewModel()
        {
            if (QtoApplication.Instance?.SessionManager != null)
            {
                QtoApplication.Instance.SessionManager.SessionChanged += (_, _) => Reload();
            }
            Reload();
        }

        partial void OnSelectedNodeChanged(ChapterNodeVm? value)
        {
            OnPropertyChanged(nameof(CanAddChapter));
            OnPropertyChanged(nameof(CanAddSubChapter));
        }

        public bool CanAddChapter => SelectedNode?.Model.Level == "SpCap";
        public bool CanAddSubChapter => SelectedNode?.Model.Level == "Cap";

        [RelayCommand]
        private void Reload()
        {
            RootNodes.Clear();
            var repo = QtoApplication.Instance?.SessionManager?.Repository;
            var sess = QtoApplication.Instance?.SessionManager?.ActiveSession;
            if (repo == null || sess == null)
            {
                _documentId = 0;
                StatusMessage = "Nessuna sessione attiva.";
                return;
            }
            try
            {
                var docSvc = new ComputoDocumentService(repo);
                var doc = docSvc.GetOrCreate(sess.Id);
                _documentId = doc.Id;

                var chapSvc = new ChapterService(repo);
                var all = chapSvc.GetAll(_documentId).ToList();
                BuildTree(all);
                StatusMessage = $"{all.Count} capitoli nel documento #{_documentId}";
            }
            catch (System.Exception ex)
            {
                StatusMessage = $"Errore caricamento: {ex.Message}";
            }
        }

        private void BuildTree(List<ChapterNode> all)
        {
            var lookup = all.ToDictionary(n => n.Id, n => new ChapterNodeVm(n));
            foreach (var kv in lookup)
            {
                var vm = kv.Value;
                if (vm.Model.ParentId.HasValue && lookup.TryGetValue(vm.Model.ParentId.Value, out var parent))
                {
                    parent.Children.Add(vm);
                }
                else
                {
                    RootNodes.Add(vm);
                }
            }
        }

        [RelayCommand]
        private void AddSuperChapter()
        {
            if (_documentId == 0) { StatusMessage = "Nessun documento attivo."; return; }
            TryExecute(() =>
            {
                var svc = new ChapterService(QtoApplication.Instance!.SessionManager!.Repository!);
                svc.AddSuperChapter(_documentId, NewCodice.Trim(), NewDesSintetica.Trim());
                ResetForm();
                Reload();
            });
        }

        [RelayCommand(CanExecute = nameof(CanAddChapter))]
        private void AddChapter()
        {
            if (_documentId == 0 || SelectedNode == null) return;
            TryExecute(() =>
            {
                var svc = new ChapterService(QtoApplication.Instance!.SessionManager!.Repository!);
                svc.AddChapter(_documentId, SelectedNode.Model.Id, NewCodice.Trim(), NewDesSintetica.Trim());
                ResetForm();
                Reload();
            });
        }

        [RelayCommand(CanExecute = nameof(CanAddSubChapter))]
        private void AddSubChapter()
        {
            if (_documentId == 0 || SelectedNode == null) return;
            TryExecute(() =>
            {
                var svc = new ChapterService(QtoApplication.Instance!.SessionManager!.Repository!);
                svc.AddSubChapter(_documentId, SelectedNode.Model.Id, NewCodice.Trim(), NewDesSintetica.Trim());
                ResetForm();
                Reload();
            });
        }

        [RelayCommand]
        private void DeleteSelected()
        {
            if (SelectedNode == null) { StatusMessage = "Seleziona un nodo da eliminare."; return; }
            TryExecute(() =>
            {
                var svc = new ChapterService(QtoApplication.Instance!.SessionManager!.Repository!);
                svc.Delete(SelectedNode.Model.Id);
                SelectedNode = null;
                Reload();
            });
        }

        private void TryExecute(System.Action action)
        {
            try { action(); }
            catch (DomainValidationException dex)
            {
                StatusMessage = $"{dex.RuleCode}: {dex.Message}";
            }
            catch (System.Exception ex)
            {
                StatusMessage = $"Errore: {ex.Message}";
            }
        }

        private void ResetForm()
        {
            NewCodice = "";
            NewDesSintetica = "";
        }
    }

    public class ChapterNodeVm
    {
        public ChapterNode Model { get; }
        public ObservableCollection<ChapterNodeVm> Children { get; } = new();
        public ChapterNodeVm(ChapterNode model) => Model = model;
        public string DisplayLabel => $"{Model.Codice} · {Model.DesSintetica} [{Model.Level}]";
    }
}
```

- [ ] **Step 2: Verificare compila**

```bash
dotnet build QtoRevitPlugin/QtoRevitPlugin.csproj -f net8.0-windows -c Debug -v q 2>&1 | grep -iE "error CS|Errori"
```

Atteso: 0 errori.

## Task 2: ChapterNodesView XAML

**Files:**
- Create: `QtoRevitPlugin/UI/Views/ChapterNodesView.xaml`
- Create: `QtoRevitPlugin/UI/Views/ChapterNodesView.xaml.cs`

Layout:
```
┌─────────────────────────────────────┐
│ TreeView (albero Sp/Cap/Sb)         │
│                                     │
│ ▼ 01 · Demolizioni [SpCap]          │
│   ▼ 01.01 · Murature [Cap]          │
│     • 01.01.01 · Esterni [SbCap]    │
│                                     │
├─────────────────────────────────────┤
│ Codice: [____] Descrizione: [____]  │
│ [+ SuperCap] [+ Cap] [+ SubCap] [X] │
│ Status: ...                         │
└─────────────────────────────────────┘
```

- [ ] **Step 1: Creare ChapterNodesView.xaml**

```xml
<UserControl x:Class="QtoRevitPlugin.UI.Views.ChapterNodesView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:QtoRevitPlugin.UI.ViewModels">
    <UserControl.DataContext>
        <vm:ChapterNodesViewModel/>
    </UserControl.DataContext>

    <Grid Margin="10">
        <Grid.RowDefinitions>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Tree -->
        <Border Grid.Row="0" BorderBrush="{DynamicResource EdgeLightBrush}" BorderThickness="1" Padding="4">
            <TreeView x:Name="ChapterTree"
                      ItemsSource="{Binding RootNodes}"
                      SelectedItemChanged="OnSelectedItemChanged"
                      Background="White" BorderThickness="0" FontSize="12">
                <TreeView.ItemTemplate>
                    <HierarchicalDataTemplate ItemsSource="{Binding Children}">
                        <TextBlock Text="{Binding DisplayLabel}"/>
                    </HierarchicalDataTemplate>
                </TreeView.ItemTemplate>
            </TreeView>
        </Border>

        <!-- Form inline + azioni -->
        <Grid Grid.Row="1" Margin="0,8,0,0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="120"/>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <TextBlock Text="Codice:" Margin="0,0,6,0" VerticalAlignment="Center"/>
            <TextBox Grid.Column="1" Text="{Binding NewCodice, UpdateSourceTrigger=PropertyChanged}"
                     Style="{DynamicResource TxtField}"/>
            <TextBlock Grid.Column="2" Text="Descrizione:" Margin="10,0,6,0" VerticalAlignment="Center"/>
            <TextBox Grid.Column="3" Text="{Binding NewDesSintetica, UpdateSourceTrigger=PropertyChanged}"
                     Style="{DynamicResource TxtField}"/>
        </Grid>

        <StackPanel Grid.Row="2" Orientation="Horizontal" Margin="0,8,0,0">
            <Button Content="+ SuperCap" Command="{Binding AddSuperChapterCommand}"
                    Style="{DynamicResource BtnTool}" Margin="0,0,6,0"
                    ToolTip="Aggiunge un SuperCapitolo (livello radice)"/>
            <Button Content="+ Cap" Command="{Binding AddChapterCommand}"
                    Style="{DynamicResource BtnTool}" Margin="0,0,6,0"
                    ToolTip="Aggiunge un Capitolo figlio del SuperCap selezionato"/>
            <Button Content="+ SubCap" Command="{Binding AddSubChapterCommand}"
                    Style="{DynamicResource BtnTool}" Margin="0,0,6,0"
                    ToolTip="Aggiunge un SubCapitolo figlio del Cap selezionato"/>
            <Button Content="Elimina" Command="{Binding DeleteSelectedCommand}"
                    Style="{DynamicResource BtnTool}" Margin="0,0,6,0"/>
            <Button Content="Aggiorna" Command="{Binding ReloadCommand}"
                    Style="{DynamicResource BtnTool}"
                    Foreground="{DynamicResource InkDimBrush}"/>
            <TextBlock Text="{Binding StatusMessage}" Margin="12,0,0,0"
                       VerticalAlignment="Center" FontSize="10" FontStyle="Italic"
                       Foreground="{DynamicResource InkMutedBrush}"/>
        </StackPanel>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Creare ChapterNodesView.xaml.cs**

```csharp
using QtoRevitPlugin.UI.ViewModels;
using System.Windows.Controls;

namespace QtoRevitPlugin.UI.Views
{
    public partial class ChapterNodesView : UserControl
    {
        public ChapterNodesView()
        {
            InitializeComponent();
        }

        private void OnSelectedItemChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is ChapterNodesViewModel vm)
                vm.SelectedNode = e.NewValue as ChapterNodeVm;
        }
    }
}
```

- [ ] **Step 3: Build**

```bash
dotnet build QtoRevitPlugin/QtoRevitPlugin.csproj -f net8.0-windows -c Debug -v q 2>&1 | grep -iE "error CS|Errori"
```

Atteso: 0 errori.

## Task 3: CategoryNodesViewModel + View

Stessa struttura di Task 1+2, ma su `CategoryNode` / `ICategoryService`.

**Files:**
- Create: `QtoRevitPlugin/UI/ViewModels/CategoryNodesViewModel.cs`
- Create: `QtoRevitPlugin/UI/Views/CategoryNodesView.xaml(.cs)`

- [ ] **Step 1: Crea CategoryNodesViewModel.cs**

Identico a ChapterNodesViewModel con queste sostituzioni (clone + replace):
- `ChapterNode` → `CategoryNode`
- `ChapterService` → `CategoryService`
- `IChapterService` → `ICategoryService`
- `AddSuperChapter` → `AddSuperCategory`
- `AddChapter` → `AddCategory`
- `AddSubChapter` → `AddSubCategory`
- `ChapterNodeVm` → `CategoryNodeVm`
- `"SpCap"` → `"SpCat"`, `"Cap"` → `"Cat"`, `"SbCap"` → `"SbCat"`
- Bottone labels: "SuperCap"→"SuperCat", "Cap"→"Cat", "SubCap"→"SubCat"

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QtoRevitPlugin.Application;
using QtoRevitPlugin.Models.Computi;
using QtoRevitPlugin.Services.Computi;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace QtoRevitPlugin.UI.ViewModels
{
    /// <summary>
    /// Gestisce l'albero dei CategoryNode (SpCat → Cat → SbCat) per il Setup.
    /// Runtime-defined per documento (non c'è standard SOA).
    /// </summary>
    public partial class CategoryNodesViewModel : ViewModelBase
    {
        private int _documentId;

        public ObservableCollection<CategoryNodeVm> RootNodes { get; } = new();

        [ObservableProperty] private CategoryNodeVm? _selectedNode;
        [ObservableProperty] private string _newCodice = "";
        [ObservableProperty] private string _newDesSintetica = "";
        [ObservableProperty] private string _statusMessage = "Nessun documento attivo.";

        public CategoryNodesViewModel()
        {
            if (QtoApplication.Instance?.SessionManager != null)
                QtoApplication.Instance.SessionManager.SessionChanged += (_, _) => Reload();
            Reload();
        }

        partial void OnSelectedNodeChanged(CategoryNodeVm? value)
        {
            OnPropertyChanged(nameof(CanAddCategory));
            OnPropertyChanged(nameof(CanAddSubCategory));
        }

        public bool CanAddCategory => SelectedNode?.Model.Level == "SpCat";
        public bool CanAddSubCategory => SelectedNode?.Model.Level == "Cat";

        [RelayCommand]
        private void Reload()
        {
            RootNodes.Clear();
            var repo = QtoApplication.Instance?.SessionManager?.Repository;
            var sess = QtoApplication.Instance?.SessionManager?.ActiveSession;
            if (repo == null || sess == null) { _documentId = 0; StatusMessage = "Nessuna sessione."; return; }
            try
            {
                var docSvc = new ComputoDocumentService(repo);
                var doc = docSvc.GetOrCreate(sess.Id);
                _documentId = doc.Id;
                var svc = new CategoryService(repo);
                var all = svc.GetAll(_documentId).ToList();
                BuildTree(all);
                StatusMessage = $"{all.Count} categorie nel documento #{_documentId}";
            }
            catch (System.Exception ex) { StatusMessage = $"Errore: {ex.Message}"; }
        }

        private void BuildTree(List<CategoryNode> all)
        {
            var lookup = all.ToDictionary(n => n.Id, n => new CategoryNodeVm(n));
            foreach (var kv in lookup)
            {
                var vm = kv.Value;
                if (vm.Model.ParentId.HasValue && lookup.TryGetValue(vm.Model.ParentId.Value, out var parent))
                    parent.Children.Add(vm);
                else
                    RootNodes.Add(vm);
            }
        }

        [RelayCommand]
        private void AddSuperCategory()
        {
            if (_documentId == 0) return;
            TryExecute(() =>
            {
                new CategoryService(QtoApplication.Instance!.SessionManager!.Repository!)
                    .AddSuperCategory(_documentId, NewCodice.Trim(), NewDesSintetica.Trim());
                ResetForm(); Reload();
            });
        }

        [RelayCommand(CanExecute = nameof(CanAddCategory))]
        private void AddCategory()
        {
            if (_documentId == 0 || SelectedNode == null) return;
            TryExecute(() =>
            {
                new CategoryService(QtoApplication.Instance!.SessionManager!.Repository!)
                    .AddCategory(_documentId, SelectedNode.Model.Id, NewCodice.Trim(), NewDesSintetica.Trim());
                ResetForm(); Reload();
            });
        }

        [RelayCommand(CanExecute = nameof(CanAddSubCategory))]
        private void AddSubCategory()
        {
            if (_documentId == 0 || SelectedNode == null) return;
            TryExecute(() =>
            {
                new CategoryService(QtoApplication.Instance!.SessionManager!.Repository!)
                    .AddSubCategory(_documentId, SelectedNode.Model.Id, NewCodice.Trim(), NewDesSintetica.Trim());
                ResetForm(); Reload();
            });
        }

        [RelayCommand]
        private void DeleteSelected()
        {
            if (SelectedNode == null) { StatusMessage = "Seleziona un nodo."; return; }
            TryExecute(() =>
            {
                new CategoryService(QtoApplication.Instance!.SessionManager!.Repository!)
                    .Delete(SelectedNode.Model.Id);
                SelectedNode = null; Reload();
            });
        }

        private void TryExecute(System.Action action)
        {
            try { action(); }
            catch (DomainValidationException dex) { StatusMessage = $"{dex.RuleCode}: {dex.Message}"; }
            catch (System.Exception ex) { StatusMessage = $"Errore: {ex.Message}"; }
        }

        private void ResetForm() { NewCodice = ""; NewDesSintetica = ""; }
    }

    public class CategoryNodeVm
    {
        public CategoryNode Model { get; }
        public ObservableCollection<CategoryNodeVm> Children { get; } = new();
        public CategoryNodeVm(CategoryNode model) => Model = model;
        public string DisplayLabel => $"{Model.Codice} · {Model.DesSintetica} [{Model.Level}]";
    }
}
```

- [ ] **Step 2: Crea CategoryNodesView.xaml** (copia di ChapterNodesView.xaml con rename comandi)

```xml
<UserControl x:Class="QtoRevitPlugin.UI.Views.CategoryNodesView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:QtoRevitPlugin.UI.ViewModels">
    <UserControl.DataContext>
        <vm:CategoryNodesViewModel/>
    </UserControl.DataContext>

    <Grid Margin="10">
        <Grid.RowDefinitions>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <Border Grid.Row="0" BorderBrush="{DynamicResource EdgeLightBrush}" BorderThickness="1" Padding="4">
            <TreeView x:Name="CategoryTree"
                      ItemsSource="{Binding RootNodes}"
                      SelectedItemChanged="OnSelectedItemChanged"
                      Background="White" BorderThickness="0" FontSize="12">
                <TreeView.ItemTemplate>
                    <HierarchicalDataTemplate ItemsSource="{Binding Children}">
                        <TextBlock Text="{Binding DisplayLabel}"/>
                    </HierarchicalDataTemplate>
                </TreeView.ItemTemplate>
            </TreeView>
        </Border>

        <Grid Grid.Row="1" Margin="0,8,0,0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="120"/>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            <TextBlock Text="Codice:" Margin="0,0,6,0" VerticalAlignment="Center"/>
            <TextBox Grid.Column="1" Text="{Binding NewCodice, UpdateSourceTrigger=PropertyChanged}"
                     Style="{DynamicResource TxtField}"/>
            <TextBlock Grid.Column="2" Text="Descrizione:" Margin="10,0,6,0" VerticalAlignment="Center"/>
            <TextBox Grid.Column="3" Text="{Binding NewDesSintetica, UpdateSourceTrigger=PropertyChanged}"
                     Style="{DynamicResource TxtField}"/>
        </Grid>

        <StackPanel Grid.Row="2" Orientation="Horizontal" Margin="0,8,0,0">
            <Button Content="+ SuperCat" Command="{Binding AddSuperCategoryCommand}"
                    Style="{DynamicResource BtnTool}" Margin="0,0,6,0"/>
            <Button Content="+ Cat" Command="{Binding AddCategoryCommand}"
                    Style="{DynamicResource BtnTool}" Margin="0,0,6,0"/>
            <Button Content="+ SubCat" Command="{Binding AddSubCategoryCommand}"
                    Style="{DynamicResource BtnTool}" Margin="0,0,6,0"/>
            <Button Content="Elimina" Command="{Binding DeleteSelectedCommand}"
                    Style="{DynamicResource BtnTool}" Margin="0,0,6,0"/>
            <Button Content="Aggiorna" Command="{Binding ReloadCommand}"
                    Style="{DynamicResource BtnTool}"
                    Foreground="{DynamicResource InkDimBrush}"/>
            <TextBlock Text="{Binding StatusMessage}" Margin="12,0,0,0"
                       VerticalAlignment="Center" FontSize="10" FontStyle="Italic"
                       Foreground="{DynamicResource InkMutedBrush}"/>
        </StackPanel>
    </Grid>
</UserControl>
```

- [ ] **Step 3: Crea CategoryNodesView.xaml.cs**

```csharp
using QtoRevitPlugin.UI.ViewModels;
using System.Windows.Controls;

namespace QtoRevitPlugin.UI.Views
{
    public partial class CategoryNodesView : UserControl
    {
        public CategoryNodesView() { InitializeComponent(); }

        private void OnSelectedItemChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is CategoryNodesViewModel vm)
                vm.SelectedNode = e.NewValue as CategoryNodeVm;
        }
    }
}
```

## Task 4: WbsNodesViewModel + View (profondità libera)

**Files:**
- Create: `QtoRevitPlugin/UI/ViewModels/WbsNodesViewModel.cs`
- Create: `QtoRevitPlugin/UI/Views/WbsNodesView.xaml(.cs)`

WBS è più semplice: un solo comando "AddChild" che aggiunge un figlio al selezionato, oppure un root se nessuno è selezionato. Un ComboBox permette di scegliere Kind (WbsCap / WbsComputo).

- [ ] **Step 1: WbsNodesViewModel.cs**

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QtoRevitPlugin.Application;
using QtoRevitPlugin.Models.Computi;
using QtoRevitPlugin.Services.Computi;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace QtoRevitPlugin.UI.ViewModels
{
    /// <summary>
    /// Gestisce l'albero WBS a profondità libera. Due alberi indipendenti per Kind
    /// (WbsCap = sul Prezziario, WbsComputo = sulle righe di computo).
    /// </summary>
    public partial class WbsNodesViewModel : ViewModelBase
    {
        private int _documentId;

        public ObservableCollection<WbsNodeVm> RootNodes { get; } = new();
        public ObservableCollection<string> Kinds { get; } = new() { "WbsCap", "WbsComputo" };

        [ObservableProperty] private WbsNodeVm? _selectedNode;
        [ObservableProperty] private string _selectedKind = "WbsComputo";
        [ObservableProperty] private string _newDesSintetica = "";
        [ObservableProperty] private string _statusMessage = "";

        public WbsNodesViewModel()
        {
            if (QtoApplication.Instance?.SessionManager != null)
                QtoApplication.Instance.SessionManager.SessionChanged += (_, _) => Reload();
            Reload();
        }

        partial void OnSelectedKindChanged(string value) => Reload();

        [RelayCommand]
        private void Reload()
        {
            RootNodes.Clear();
            var repo = QtoApplication.Instance?.SessionManager?.Repository;
            var sess = QtoApplication.Instance?.SessionManager?.ActiveSession;
            if (repo == null || sess == null) { _documentId = 0; StatusMessage = "Nessuna sessione."; return; }
            try
            {
                var docSvc = new ComputoDocumentService(repo);
                var doc = docSvc.GetOrCreate(sess.Id);
                _documentId = doc.Id;
                var svc = new WbsService(repo);
                var all = svc.GetAll(_documentId, SelectedKind).ToList();
                BuildTree(all);
                StatusMessage = $"{all.Count} nodi ({SelectedKind})";
            }
            catch (System.Exception ex) { StatusMessage = $"Errore: {ex.Message}"; }
        }

        private void BuildTree(List<WbsNode> all)
        {
            var lookup = all.ToDictionary(n => n.Id, n => new WbsNodeVm(n));
            foreach (var kv in lookup)
            {
                var vm = kv.Value;
                if (vm.Model.ParentId.HasValue && lookup.TryGetValue(vm.Model.ParentId.Value, out var parent))
                    parent.Children.Add(vm);
                else
                    RootNodes.Add(vm);
            }
        }

        [RelayCommand]
        private void AddRoot()
        {
            if (_documentId == 0) return;
            TryExecute(() =>
            {
                new WbsService(QtoApplication.Instance!.SessionManager!.Repository!)
                    .Add(_documentId, SelectedKind, null, NewDesSintetica.Trim());
                NewDesSintetica = ""; Reload();
            });
        }

        [RelayCommand]
        private void AddChild()
        {
            if (_documentId == 0 || SelectedNode == null) { StatusMessage = "Seleziona un nodo padre."; return; }
            TryExecute(() =>
            {
                new WbsService(QtoApplication.Instance!.SessionManager!.Repository!)
                    .Add(_documentId, SelectedKind, SelectedNode.Model.Id, NewDesSintetica.Trim());
                NewDesSintetica = ""; Reload();
            });
        }

        [RelayCommand]
        private void DeleteSelected()
        {
            if (SelectedNode == null) { StatusMessage = "Seleziona un nodo."; return; }
            TryExecute(() =>
            {
                new WbsService(QtoApplication.Instance!.SessionManager!.Repository!)
                    .Delete(SelectedNode.Model.Id);
                SelectedNode = null; Reload();
            });
        }

        private void TryExecute(System.Action action)
        {
            try { action(); }
            catch (DomainValidationException dex) { StatusMessage = $"{dex.RuleCode}: {dex.Message}"; }
            catch (System.Exception ex) { StatusMessage = $"Errore: {ex.Message}"; }
        }
    }

    public class WbsNodeVm
    {
        public WbsNode Model { get; }
        public ObservableCollection<WbsNodeVm> Children { get; } = new();
        public WbsNodeVm(WbsNode model) => Model = model;
        public string DisplayLabel => $"{Model.Codice} · {Model.DesSintetica}";
    }
}
```

- [ ] **Step 2: WbsNodesView.xaml**

```xml
<UserControl x:Class="QtoRevitPlugin.UI.Views.WbsNodesView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:QtoRevitPlugin.UI.ViewModels">
    <UserControl.DataContext>
        <vm:WbsNodesViewModel/>
    </UserControl.DataContext>

    <Grid Margin="10">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Kind switcher -->
        <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,0,0,8">
            <TextBlock Text="WBS:" VerticalAlignment="Center" Margin="0,0,6,0"/>
            <ComboBox ItemsSource="{Binding Kinds}" SelectedItem="{Binding SelectedKind, Mode=TwoWay}"
                      Width="160" Style="{DynamicResource CmbField}"/>
        </StackPanel>

        <Border Grid.Row="1" BorderBrush="{DynamicResource EdgeLightBrush}" BorderThickness="1" Padding="4">
            <TreeView x:Name="WbsTree"
                      ItemsSource="{Binding RootNodes}"
                      SelectedItemChanged="OnSelectedItemChanged"
                      Background="White" BorderThickness="0" FontSize="12">
                <TreeView.ItemTemplate>
                    <HierarchicalDataTemplate ItemsSource="{Binding Children}">
                        <TextBlock Text="{Binding DisplayLabel}"/>
                    </HierarchicalDataTemplate>
                </TreeView.ItemTemplate>
            </TreeView>
        </Border>

        <Grid Grid.Row="2" Margin="0,8,0,0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            <TextBlock Text="Descrizione:" Margin="0,0,6,0" VerticalAlignment="Center"/>
            <TextBox Grid.Column="1" Text="{Binding NewDesSintetica, UpdateSourceTrigger=PropertyChanged}"
                     Style="{DynamicResource TxtField}"/>
        </Grid>

        <StackPanel Grid.Row="3" Orientation="Horizontal" Margin="0,8,0,0">
            <Button Content="+ Root" Command="{Binding AddRootCommand}"
                    Style="{DynamicResource BtnTool}" Margin="0,0,6,0"/>
            <Button Content="+ Figlio" Command="{Binding AddChildCommand}"
                    Style="{DynamicResource BtnTool}" Margin="0,0,6,0"/>
            <Button Content="Elimina" Command="{Binding DeleteSelectedCommand}"
                    Style="{DynamicResource BtnTool}" Margin="0,0,6,0"/>
            <Button Content="Aggiorna" Command="{Binding ReloadCommand}"
                    Style="{DynamicResource BtnTool}"
                    Foreground="{DynamicResource InkDimBrush}"/>
            <TextBlock Text="{Binding StatusMessage}" Margin="12,0,0,0"
                       VerticalAlignment="Center" FontSize="10" FontStyle="Italic"
                       Foreground="{DynamicResource InkMutedBrush}"/>
        </StackPanel>
    </Grid>
</UserControl>
```

- [ ] **Step 3: WbsNodesView.xaml.cs**

```csharp
using QtoRevitPlugin.UI.ViewModels;
using System.Windows.Controls;

namespace QtoRevitPlugin.UI.Views
{
    public partial class WbsNodesView : UserControl
    {
        public WbsNodesView() { InitializeComponent(); }

        private void OnSelectedItemChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is WbsNodesViewModel vm)
                vm.SelectedNode = e.NewValue as WbsNodeVm;
        }
    }
}
```

## Task 5: Integrazione in SetupView

**Files:**
- Modify: `QtoRevitPlugin/UI/Views/SetupView.xaml`

- [ ] **Step 1: Aprire SetupView.xaml e individuare il TabControl**

Struttura attuale (`SetupView.xaml`):
```xml
<TabControl ...>
    <TabItem Header="Informazioni">...</TabItem>
    <TabItem Header="Listino">...</TabItem>
    <TabItem Header="Struttura Computo">...</TabItem>
    <TabItem Header="Nuovi Prezzi">...</TabItem>
</TabControl>
```

- [ ] **Step 2: Aggiungere 3 TabItem nuovi PRIMA di "Nuovi Prezzi"**

Dopo `<TabItem Header="Struttura Computo">` inserire:

```xml
<TabItem Header="Capitoli (v12)">
    <views:ChapterNodesView />
</TabItem>
<TabItem Header="Categorie (v12)">
    <views:CategoryNodesView />
</TabItem>
<TabItem Header="WBS (v12)">
    <views:WbsNodesView />
</TabItem>
```

Nota: il suffisso `(v12)` è volutamente visibile per distinguere dalle strutture legacy (Struttura Computo) durante il periodo di transizione. Verrà rimosso dopo che i tab v12 saranno quelli ufficiali (Plan C-5+).

- [ ] **Step 3: Build**

```bash
dotnet build QtoRevitPlugin/QtoRevitPlugin.csproj -f net8.0-windows -c Debug -v q 2>&1 | grep -iE "error CS|Errori"
```

Atteso: 0 errori.

## Task 6: Verifica regressione piena

- [ ] **Step 1: Full test suite**

```bash
dotnet test QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj --no-build -v quiet
```

Atteso: **tutti i 484 test precedenti + eventuali nuovi passano.** Nessun nuovo test di UI (sono verificati a mano in Revit).

## Task 7: Commit

```bash
git add QtoRevitPlugin/UI/ViewModels/ChapterNodesViewModel.cs \
       QtoRevitPlugin/UI/ViewModels/CategoryNodesViewModel.cs \
       QtoRevitPlugin/UI/ViewModels/WbsNodesViewModel.cs \
       QtoRevitPlugin/UI/Views/ChapterNodesView.xaml \
       QtoRevitPlugin/UI/Views/ChapterNodesView.xaml.cs \
       QtoRevitPlugin/UI/Views/CategoryNodesView.xaml \
       QtoRevitPlugin/UI/Views/CategoryNodesView.xaml.cs \
       QtoRevitPlugin/UI/Views/WbsNodesView.xaml \
       QtoRevitPlugin/UI/Views/WbsNodesView.xaml.cs \
       QtoRevitPlugin/UI/Views/SetupView.xaml
git commit -m "feat(ui): Setup tab per Capitoli/Categorie/WBS v12 (Plan C-3)"
```

---

## Self-review

- [x] Approccio additivo: il tab "Struttura Computo" legacy resta intoccato
- [x] 3 nuovi UserControl indipendenti: Chapter, Category, Wbs
- [x] Pattern MVVM già usato nel progetto (CommunityToolkit)
- [x] Riuso stili del tema (TxtField, CmbField, BtnTool, EdgeLightBrush, InkMutedBrush)
- [x] Gestione errori: `DomainValidationException` mostrata in StatusMessage con RuleCode
- [x] Reload su `SessionChanged` per tenere la vista aggiornata
- [x] CanExecute sui comandi Add (SpCap, Cap, SbCap coerenti con parent selezionato)

## Scope NON incluso

- Drag&drop riordino nodi (rimandato a C-5 dove servirà per "sposta voce" nella Redazione CME)
- Rinumerazione massiva codici (idem C-5)
- Editing inline dei campi (doppio click su nodo → modifica): per ora solo Add/Delete + Aggiorna
- Migrazione automatica da `ComputoChapter` legacy a `ChapterNode` v12: decisione rimandata a Plan C-5
- Eliminazione cascade con conferma: oggi il DB fa `ON DELETE CASCADE` e il servizio non chiede conferma UI
- Assegnazione capitolo→EP (collegamento): rimandato a C-4 UI Listino
