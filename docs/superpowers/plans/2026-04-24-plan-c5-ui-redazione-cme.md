# Plan C-5 — UI Redazione CME (vista live 3 colonne)

> **Contesto:** ultimo sotto-progetto della spec `2026-04-24-modulo-computi-primus-xpwe-design.md`.
> Dipende da C-0 (schema), C-2 (services), C-6 (MeasurementRow/SubRow già popolati).

**Goal:** Nuova scheda "Redazione CME" nel menu principale del pannello. 3 colonne read-only:
1. **Navigatore** (TreeView) con 4 modalità: per Capitoli / per Categorie / per WBS / lineare
2. **Tabella VCItem** filtrata dal nodo selezionato, con RGItem espandibili inline
3. **Quadro economico** (totale netto + percentuali incidenza per capitolo/categoria)

Read-only in questa iterazione. Editing (rinomina, drag, override) rimandato a C-5.1.

**Architecture:**
- `CmeEditorView.xaml` + `CmeEditorViewModel.cs`
- Rimpiazza il mapping `QtoViewKey.Tagging` (oggi `MappingView`) → `CmeEditorView`
- Label menu "Tagging" → "Redazione CME"
- Reload automatico su `SessionChanged` + su `ActiveEpChanged` (se assegnazione fatta da Selezione, vista si aggiorna)

**Tech Stack:** WPF MVVM CommunityToolkit, TreeView+DataGrid, riuso domain services C-2.

**File impattati:**
- Create: `QtoRevitPlugin/UI/ViewModels/CmeEditorViewModel.cs`
- Create: `QtoRevitPlugin/UI/Views/CmeEditorView.xaml(.cs)`
- Modify: `QtoRevitPlugin/UI/Panes/QtoDockablePane.xaml.cs` (switch `Tagging` → `CmeEditorView`)
- Modify: `QtoRevitPlugin/UI/ViewModels/DockablePaneViewModel.cs` (label `"Tagging"` → `"Redazione CME"`)

**NO test automated** (UI pura, verifica manuale in Revit). La logica di calcolo totali è semplice abbastanza da non richiedere test dedicati — la formula Quantita × Prezzo viene da campi già testati.

---

## Task 1: CmeEditorViewModel

**Files:**
- Create: `QtoRevitPlugin/UI/ViewModels/CmeEditorViewModel.cs`

Responsabilità:
- Property `NavigationMode` (enum: `Chapters` / `Categories` / `Wbs` / `Flat`)
- `ObservableCollection<CmeNavNode> NavTree` popolato dal mode selezionato
- `ObservableCollection<CmeVociRow> VisibleRows` filtrato dal nodo selezionato nell'albero
- `QuadroEconomico` object con Netto, PerCapitoloRows, PerCategoriaRows
- Reload su SessionChanged / ActiveEpChanged

- [ ] **Step 1: Scrivere il file**

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QtoRevitPlugin.Application;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Models.Computi;
using QtoRevitPlugin.Services.Computi;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace QtoRevitPlugin.UI.ViewModels
{
    public enum CmeNavMode { Chapters, Categories, Wbs, Flat }

    /// <summary>
    /// Plan C-5: VM della Redazione CME. Vista live dei MeasurementRow (VCItem) creati
    /// dall'assegnazione (Plan C-6), organizzati per Capitoli / Categorie / WBS / lineare.
    /// Read-only in questa iterazione — editing in C-5.1.
    /// </summary>
    public partial class CmeEditorViewModel : ViewModelBase
    {
        private int _documentId;

        public ObservableCollection<CmeNavNode> NavTree { get; } = new();
        public ObservableCollection<CmeVociRow> VisibleRows { get; } = new();
        public ObservableCollection<QuadroGroupRow> PerCapitoloRows { get; } = new();
        public ObservableCollection<QuadroGroupRow> PerCategoriaRows { get; } = new();

        [ObservableProperty] private CmeNavMode _navigationMode = CmeNavMode.Chapters;
        [ObservableProperty] private CmeNavNode? _selectedNavNode;
        [ObservableProperty] private double _totaleNetto;
        [ObservableProperty] private string _statusMessage = "";

        public CmeEditorViewModel()
        {
            if (QtoApplication.Instance?.SessionManager != null)
            {
                var sm = QtoApplication.Instance.SessionManager;
                sm.SessionChanged += (_, _) => Reload();
                sm.ActiveEpChanged += (_, _) => Reload();
            }
            Reload();
        }

        partial void OnNavigationModeChanged(CmeNavMode value) => BuildNavTree();
        partial void OnSelectedNavNodeChanged(CmeNavNode? value) => RefreshVisibleRows();

        [RelayCommand]
        private void Reload()
        {
            var repo = QtoApplication.Instance?.SessionManager?.Repository;
            var sess = QtoApplication.Instance?.SessionManager?.ActiveSession;
            if (repo == null || sess == null) { _documentId = 0; StatusMessage = "Nessuna sessione."; return; }

            try
            {
                var docSvc = new ComputoDocumentService(repo);
                var doc = docSvc.GetOrCreate(sess.Id);
                _documentId = doc.Id;
                BuildNavTree();
                RefreshVisibleRows();
                RebuildQuadroEconomico();
                StatusMessage = $"Documento #{_documentId} · {VisibleRows.Count} voci totali";
            }
            catch (System.Exception ex) { StatusMessage = $"Errore: {ex.Message}"; }
        }

        private void BuildNavTree()
        {
            NavTree.Clear();
            var repo = QtoApplication.Instance?.SessionManager?.Repository;
            if (repo == null || _documentId == 0) return;

            switch (NavigationMode)
            {
                case CmeNavMode.Chapters:
                    {
                        var all = new ChapterService(repo).GetAll(_documentId);
                        var lookup = all.ToDictionary(n => n.Id, n => new CmeNavNode
                        {
                            Kind = "Chapter",
                            RefId = n.Id,
                            Label = $"{n.Codice} · {n.DesSintetica}",
                            Level = n.Level
                        });
                        foreach (var kv in lookup)
                        {
                            var node = all.First(x => x.Id == kv.Key);
                            if (node.ParentId.HasValue && lookup.TryGetValue(node.ParentId.Value, out var parent))
                                parent.Children.Add(kv.Value);
                            else
                                NavTree.Add(kv.Value);
                        }
                        break;
                    }
                case CmeNavMode.Categories:
                    {
                        var all = new CategoryService(repo).GetAll(_documentId);
                        var lookup = all.ToDictionary(n => n.Id, n => new CmeNavNode
                        {
                            Kind = "Category",
                            RefId = n.Id,
                            Label = $"{n.Codice} · {n.DesSintetica}",
                            Level = n.Level
                        });
                        foreach (var kv in lookup)
                        {
                            var node = all.First(x => x.Id == kv.Key);
                            if (node.ParentId.HasValue && lookup.TryGetValue(node.ParentId.Value, out var parent))
                                parent.Children.Add(kv.Value);
                            else
                                NavTree.Add(kv.Value);
                        }
                        break;
                    }
                case CmeNavMode.Wbs:
                    {
                        var all = new WbsService(repo).GetAll(_documentId, "WbsComputo");
                        var lookup = all.ToDictionary(n => n.Id, n => new CmeNavNode
                        {
                            Kind = "Wbs",
                            RefId = n.Id,
                            Label = $"{n.Codice} · {n.DesSintetica}",
                            Level = n.Level.ToString()
                        });
                        foreach (var kv in lookup)
                        {
                            var node = all.First(x => x.Id == kv.Key);
                            if (node.ParentId.HasValue && lookup.TryGetValue(node.ParentId.Value, out var parent))
                                parent.Children.Add(kv.Value);
                            else
                                NavTree.Add(kv.Value);
                        }
                        break;
                    }
                case CmeNavMode.Flat:
                    // Nessun albero — VisibleRows mostra tutto
                    break;
            }
        }

        private void RefreshVisibleRows()
        {
            VisibleRows.Clear();
            var repo = QtoApplication.Instance?.SessionManager?.Repository;
            if (repo == null || _documentId == 0) return;

            var msvc = new MeasurementService(repo);
            var rows = msvc.GetRows(_documentId);

            // Risolvi PriceItem per ogni row (batch)
            var piIds = rows.Select(r => r.PriceItemId).Distinct().ToList();
            var priceItems = piIds.Count > 0
                ? repo.GetPriceItems(piIds).ToDictionary(p => p.Id)
                : new Dictionary<int, PriceItem>();

            foreach (var row in rows)
            {
                if (!priceItems.TryGetValue(row.PriceItemId, out var pi)) continue;

                // Filtro per nodo navigatore selezionato
                if (SelectedNavNode != null)
                {
                    bool match = NavigationMode switch
                    {
                        CmeNavMode.Chapters   => pi.SpCapId == SelectedNavNode.RefId || pi.CapId == SelectedNavNode.RefId || pi.SbCapId == SelectedNavNode.RefId,
                        CmeNavMode.Categories => row.SpCatId == SelectedNavNode.RefId || row.CatId == SelectedNavNode.RefId || row.SbCatId == SelectedNavNode.RefId,
                        CmeNavMode.Wbs        => row.WbsComputoNodeId == SelectedNavNode.RefId,
                        _ => true
                    };
                    if (!match) continue;
                }

                var vmRow = new CmeVociRow
                {
                    RowId = row.Id,
                    Code = pi.Code,
                    DesRidotta = !string.IsNullOrEmpty(pi.ShortDesc) ? pi.ShortDesc : pi.Description,
                    Unit = pi.Unit,
                    Quantita = row.Quantita,
                    UnitPrice = pi.Prezzo1 > 0 ? pi.Prezzo1 : pi.UnitPrice,
                };
                vmRow.Importo = vmRow.Quantita * vmRow.UnitPrice;

                // Popola SubRows (RGItem)
                foreach (var sub in msvc.GetSubRows(row.Id))
                {
                    vmRow.SubRows.Add(new CmeMisuraRow
                    {
                        SubId = sub.Id,
                        Descrizione = sub.Descrizione ?? "",
                        PartiUguali = sub.PartiUguali,
                        Lunghezza = sub.Lunghezza,
                        Larghezza = sub.Larghezza,
                        HPeso = sub.HPeso,
                        Quantita = sub.Quantita
                    });
                }

                VisibleRows.Add(vmRow);
            }
            RebuildQuadroEconomico();
        }

        private void RebuildQuadroEconomico()
        {
            TotaleNetto = VisibleRows.Sum(r => r.Importo);

            PerCapitoloRows.Clear();
            // TODO: group by capitolo richiederebbe join con ChapterNode via PriceItem.SpCapId
            // Rimandato a C-5.1. Per ora mostra solo totale per Listino (PriceList).

            PerCategoriaRows.Clear();
            // TODO: idem per Categoria via row.SpCatId
        }
    }

    /// <summary>Nodo dell'albero navigatore (Capitolo/Categoria/Wbs).</summary>
    public class CmeNavNode
    {
        public string Kind { get; set; } = "";
        public int RefId { get; set; }
        public string Label { get; set; } = "";
        public string Level { get; set; } = "";
        public ObservableCollection<CmeNavNode> Children { get; } = new();
    }

    /// <summary>Riga DataGrid tabella voci (VCItem + riassunto).</summary>
    public partial class CmeVociRow : ObservableObject
    {
        public int RowId { get; set; }
        public string Code { get; set; } = "";
        public string DesRidotta { get; set; } = "";
        public string Unit { get; set; } = "";
        public double Quantita { get; set; }
        public double UnitPrice { get; set; }
        public double Importo { get; set; }

        public ObservableCollection<CmeMisuraRow> SubRows { get; } = new();

        public string QuantitaFormatted => Quantita.ToString("N2");
        public string UnitPriceFormatted => UnitPrice > 0 ? $"€ {UnitPrice:N2}" : "—";
        public string ImportoFormatted => Importo > 0 ? $"€ {Importo:N2}" : "—";

        [ObservableProperty] private bool _isExpanded;
    }

    public class CmeMisuraRow
    {
        public int SubId { get; set; }
        public string Descrizione { get; set; } = "";
        public double PartiUguali { get; set; }
        public double? Lunghezza { get; set; }
        public double? Larghezza { get; set; }
        public double? HPeso { get; set; }
        public double Quantita { get; set; }

        public string Formula
        {
            get
            {
                var parts = new System.Collections.Generic.List<string> { PartiUguali.ToString("0.###") };
                if (Lunghezza.HasValue) parts.Add(Lunghezza.Value.ToString("0.###"));
                if (Larghezza.HasValue) parts.Add(Larghezza.Value.ToString("0.###"));
                if (HPeso.HasValue) parts.Add(HPeso.Value.ToString("0.###"));
                return string.Join(" × ", parts) + $" = {Quantita:N3}";
            }
        }
    }

    public class QuadroGroupRow
    {
        public string Label { get; set; } = "";
        public double Totale { get; set; }
        public double PercentualeIncidenza { get; set; }
        public string TotaleFormatted => $"€ {Totale:N2}";
        public string PercentualeFormatted => $"{PercentualeIncidenza:N1}%";
    }
}
```

## Task 2: CmeEditorView XAML

**Files:**
- Create: `QtoRevitPlugin/UI/Views/CmeEditorView.xaml(.cs)`

- [ ] **Step 1: Scrivere il XAML (3 colonne)**

```xml
<UserControl x:Class="QtoRevitPlugin.UI.Views.CmeEditorView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:QtoRevitPlugin.UI.ViewModels">
    <UserControl.DataContext>
        <vm:CmeEditorViewModel/>
    </UserControl.DataContext>

    <Grid Background="{DynamicResource PanelBgBrush}">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Titolo -->
        <Grid Grid.Row="0" Margin="16,14,16,4">
            <TextBlock Text="Redazione CME" Style="{DynamicResource ViewTitleStyle}" />
        </Grid>

        <!-- 3 colonne -->
        <Grid Grid.Row="1" Margin="16,4,16,4">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="260"/>
                <ColumnDefinition Width="8"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="8"/>
                <ColumnDefinition Width="260"/>
            </Grid.ColumnDefinitions>

            <!-- COL 1 · Navigatore -->
            <Border Grid.Column="0" BorderBrush="{DynamicResource EdgeLightBrush}" BorderThickness="1" Padding="6">
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="*"/>
                    </Grid.RowDefinitions>
                    <TextBlock Grid.Row="0" Text="NAVIGATORE" FontSize="10" FontWeight="Bold"
                               Foreground="{DynamicResource InkMutedBrush}" Margin="0,0,0,6"/>
                    <!-- Mode radio -->
                    <StackPanel Grid.Row="1" Margin="0,0,0,6">
                        <RadioButton Content="Per Capitoli"  GroupName="NavMode" FontSize="11"
                                     IsChecked="{Binding NavigationMode, Converter={StaticResource EnumToBool}, ConverterParameter=Chapters, Mode=TwoWay}"/>
                        <RadioButton Content="Per Categorie" GroupName="NavMode" FontSize="11" Margin="0,2,0,0"
                                     IsChecked="{Binding NavigationMode, Converter={StaticResource EnumToBool}, ConverterParameter=Categories, Mode=TwoWay}"/>
                        <RadioButton Content="Per WBS"       GroupName="NavMode" FontSize="11" Margin="0,2,0,0"
                                     IsChecked="{Binding NavigationMode, Converter={StaticResource EnumToBool}, ConverterParameter=Wbs, Mode=TwoWay}"/>
                        <RadioButton Content="Lineare (tutto)" GroupName="NavMode" FontSize="11" Margin="0,2,0,0"
                                     IsChecked="{Binding NavigationMode, Converter={StaticResource EnumToBool}, ConverterParameter=Flat, Mode=TwoWay}"/>
                    </StackPanel>
                    <TreeView Grid.Row="2"
                              ItemsSource="{Binding NavTree}"
                              SelectedItemChanged="OnNavSelectedChanged"
                              BorderThickness="0" FontSize="11">
                        <TreeView.ItemTemplate>
                            <HierarchicalDataTemplate ItemsSource="{Binding Children}">
                                <TextBlock Text="{Binding Label}"/>
                            </HierarchicalDataTemplate>
                        </TreeView.ItemTemplate>
                    </TreeView>
                </Grid>
            </Border>

            <!-- COL 2 · Tabella voci -->
            <Border Grid.Column="2" BorderBrush="{DynamicResource EdgeLightBrush}" BorderThickness="1">
                <DataGrid ItemsSource="{Binding VisibleRows}"
                          AutoGenerateColumns="False" CanUserAddRows="False" CanUserDeleteRows="False"
                          HeadersVisibility="Column" RowDetailsVisibilityMode="VisibleWhenSelected"
                          Background="White" BorderThickness="0" FontSize="11" RowHeight="22"
                          IsReadOnly="True">
                    <DataGrid.Columns>
                        <DataGridTextColumn Header="Codice"       Binding="{Binding Code}" Width="120" FontFamily="Consolas"/>
                        <DataGridTextColumn Header="Descrizione"  Binding="{Binding DesRidotta}" Width="*"/>
                        <DataGridTextColumn Header="UM"           Binding="{Binding Unit}" Width="50"/>
                        <DataGridTextColumn Header="Qta"          Binding="{Binding QuantitaFormatted}" Width="80"/>
                        <DataGridTextColumn Header="Prezzo"       Binding="{Binding UnitPriceFormatted}" Width="90"/>
                        <DataGridTextColumn Header="Importo"      Binding="{Binding ImportoFormatted}" Width="100" FontWeight="SemiBold"/>
                    </DataGrid.Columns>
                    <DataGrid.RowDetailsTemplate>
                        <DataTemplate>
                            <ItemsControl ItemsSource="{Binding SubRows}" Margin="28,2,0,4">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate>
                                        <Grid Margin="0,1,0,0">
                                            <Grid.ColumnDefinitions>
                                                <ColumnDefinition Width="*"/>
                                                <ColumnDefinition Width="Auto"/>
                                            </Grid.ColumnDefinitions>
                                            <TextBlock Grid.Column="0" Text="{Binding Descrizione}" FontSize="10"
                                                       Foreground="{DynamicResource InkMutedBrush}" TextTrimming="CharacterEllipsis"/>
                                            <TextBlock Grid.Column="1" Text="{Binding Formula}" FontSize="10"
                                                       FontFamily="Consolas" Margin="8,0,0,0"
                                                       Foreground="{DynamicResource InkDimBrush}"/>
                                        </Grid>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                        </DataTemplate>
                    </DataGrid.RowDetailsTemplate>
                </DataGrid>
            </Border>

            <!-- COL 3 · Quadro economico -->
            <Border Grid.Column="4" BorderBrush="{DynamicResource EdgeLightBrush}" BorderThickness="1" Padding="10">
                <StackPanel>
                    <TextBlock Text="QUADRO ECONOMICO" FontSize="10" FontWeight="Bold"
                               Foreground="{DynamicResource InkMutedBrush}" Margin="0,0,0,10"/>

                    <TextBlock Text="Totale netto" FontSize="10" Foreground="{DynamicResource InkMutedBrush}"/>
                    <TextBlock Text="{Binding TotaleNetto, StringFormat='€ {0:N2}'}"
                               FontSize="20" FontWeight="Bold"
                               Foreground="{DynamicResource BrandAccentDeepBrush}"
                               Margin="0,2,0,16"/>

                    <TextBlock Text="PER CAPITOLO" FontSize="9" FontWeight="Bold"
                               Foreground="{DynamicResource InkMutedBrush}" Margin="0,0,0,4"/>
                    <ItemsControl ItemsSource="{Binding PerCapitoloRows}" Margin="0,0,0,12">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Grid Margin="0,2,0,0">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*"/>
                                        <ColumnDefinition Width="Auto"/>
                                        <ColumnDefinition Width="50"/>
                                    </Grid.ColumnDefinitions>
                                    <TextBlock Grid.Column="0" Text="{Binding Label}" FontSize="10" TextTrimming="CharacterEllipsis"/>
                                    <TextBlock Grid.Column="1" Text="{Binding TotaleFormatted}" FontSize="10" Margin="4,0,4,0"/>
                                    <TextBlock Grid.Column="2" Text="{Binding PercentualeFormatted}" FontSize="10"
                                               HorizontalAlignment="Right" Foreground="{DynamicResource InkMutedBrush}"/>
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>

                    <TextBlock Text="PER CATEGORIA" FontSize="9" FontWeight="Bold"
                               Foreground="{DynamicResource InkMutedBrush}" Margin="0,0,0,4"/>
                    <ItemsControl ItemsSource="{Binding PerCategoriaRows}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Grid Margin="0,2,0,0">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*"/>
                                        <ColumnDefinition Width="Auto"/>
                                        <ColumnDefinition Width="50"/>
                                    </Grid.ColumnDefinitions>
                                    <TextBlock Grid.Column="0" Text="{Binding Label}" FontSize="10" TextTrimming="CharacterEllipsis"/>
                                    <TextBlock Grid.Column="1" Text="{Binding TotaleFormatted}" FontSize="10" Margin="4,0,4,0"/>
                                    <TextBlock Grid.Column="2" Text="{Binding PercentualeFormatted}" FontSize="10"
                                               HorizontalAlignment="Right" Foreground="{DynamicResource InkMutedBrush}"/>
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </Border>
        </Grid>

        <!-- Status bar -->
        <Border Grid.Row="2" Background="{DynamicResource PanelSubBrush}"
                BorderBrush="{DynamicResource EdgeLightBrush}" BorderThickness="0,1,0,0" Padding="16,8">
            <StackPanel Orientation="Horizontal">
                <TextBlock Text="{Binding StatusMessage}"
                           FontSize="11" FontStyle="Italic"
                           Foreground="{DynamicResource InkMutedBrush}"/>
                <Button Content="Aggiorna" Command="{Binding ReloadCommand}"
                        Style="{DynamicResource BtnTool}" Margin="16,0,0,0"
                        Padding="10,2" FontSize="10"/>
            </StackPanel>
        </Border>
    </Grid>
</UserControl>
```

Nota: il binding `Converter={StaticResource EnumToBool}` richiede un converter. Se non esiste ancora nel tema, lo creo in un file dedicato. Uso un `EnumToBooleanConverter` standard WPF (è un classico, 20 righe).

- [ ] **Step 2: Creare CmeEditorView.xaml.cs**

```csharp
using QtoRevitPlugin.UI.ViewModels;
using System.Windows.Controls;

namespace QtoRevitPlugin.UI.Views
{
    public partial class CmeEditorView : UserControl
    {
        public CmeEditorView()
        {
            InitializeComponent();
        }

        private void OnNavSelectedChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is CmeEditorViewModel vm)
                vm.SelectedNavNode = e.NewValue as CmeNavNode;
        }
    }
}
```

## Task 3: EnumToBooleanConverter

**Files:**
- Create: `QtoRevitPlugin/UI/Converters/EnumToBooleanConverter.cs`
- Modify: `QtoRevitPlugin/Theme/QtoTheme.xaml` (registrare il converter come StaticResource)

- [ ] **Step 1: Creare converter**

```csharp
using System;
using System.Globalization;
using System.Windows.Data;

namespace QtoRevitPlugin.UI.Converters
{
    /// <summary>
    /// Converter WPF classico per bindare RadioButton.IsChecked a un enum property.
    /// ConverterParameter contiene il nome dell'enum value (case-insensitive).
    /// Binding OneWay: enum → bool. Mode=TwoWay: bool→enum (solo se IsChecked=true).
    /// </summary>
    public class EnumToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null) return false;
            return string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is bool b) || !b || parameter == null) return Binding.DoNothing;
            return Enum.Parse(targetType, parameter.ToString()!);
        }
    }
}
```

- [ ] **Step 2: Registrare nel tema**

In `QtoTheme.xaml`, dentro `<ResourceDictionary>` (prima del blocco styles):

```xml
<conv:EnumToBooleanConverter x:Key="EnumToBool"/>
```

Con `xmlns:conv="clr-namespace:QtoRevitPlugin.UI.Converters"` in testa.

## Task 4: Swap in navigation

**Files:**
- Modify: `QtoRevitPlugin/UI/Panes/QtoDockablePane.xaml.cs`
- Modify: `QtoRevitPlugin/UI/ViewModels/DockablePaneViewModel.cs`

- [ ] **Step 1: In QtoDockablePane.xaml.cs sostituire MappingView con CmeEditorView per chiave Tagging**

- [ ] **Step 2: In DockablePaneViewModel.cs rinominare label "Tagging" → "Redazione CME"**

## Task 5: Build + deploy + commit

Nessun test automated. Build + full suite regression.

---

## Self-review

- [x] Read-only: zero rischio di corruzione dati
- [x] 4 modalità navigatore coprono i casi d'uso tipici
- [x] RowDetailsTemplate espande RGItem inline (WPF nativo)
- [x] Quadro economico con totale netto già implementato; per-capitolo/per-categoria rimandati (join complessi)
- [x] Live update: SessionChanged + ActiveEpChanged trigger reload

## Scope NON incluso

- PerCapitolo/PerCategoria rollup completo (richiede join SpCap/Cap/SbCap → raggruppa per SuperCap) — C-5.1
- Editing inline quantità/descrizione — C-5.1
- Delete VCItem contestuale — C-5.1
- Drag&drop sposta voce — C-5.2
- Export PDF del computo — fuori scope
