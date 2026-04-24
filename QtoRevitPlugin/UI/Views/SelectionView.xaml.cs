using QtoRevitPlugin.Services;
using QtoRevitPlugin.UI.ViewModels;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace QtoRevitPlugin.UI.Views
{
    /// <summary>
    /// Selezione elementi per categoria + filtri parametrici + colonne personalizzate.
    /// Il menu contestuale sulle intestazioni permette di mostrare/nascondere le colonne standard
    /// e aggiungere/rimuovere colonne basate sui parametri della categoria attiva.
    /// </summary>
    public partial class SelectionView : UserControl
    {
        private readonly SelectionViewModel _vm;

        // Colonne custom generate dinamicamente: mapping DisplayName → DataGridColumn
        // per poterle rimuovere su richiesta.
        private readonly Dictionary<string, DataGridColumn> _customColumnMap =
            new Dictionary<string, DataGridColumn>();

        public SelectionView()
        {
            _vm = new SelectionViewModel();
            DataContext = _vm;
            InitializeComponent();
            _vm.CustomColumns.CollectionChanged += OnCustomColumnsChanged;
            // Plan C-6: sincronizza la selezione DataGrid → VM.SelectedElements
            GridElements.SelectionChanged += OnGridSelectionChanged;
        }

        /// <summary>
        /// Plan C-6: quando l'utente seleziona/deseleziona righe nel DataGrid, il VM riceve
        /// la lista aggiornata e ricalcola preview + CanApply. L'assegnazione tagga SOLO
        /// le righe selezionate, non tutti gli elementi filtrati.
        /// </summary>
        private void OnGridSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _vm.SetSelectedElements(GridElements.SelectedItems.Cast<ElementRowVm>());
        }

        private void OnPopoutClick(object sender, RoutedEventArgs e)
            => PopoutWindow.Popout(new SelectionView(), "CME · Selezione Elementi");

        private void OnIsolateClick(object sender, RoutedEventArgs e) => _vm.IsolateCurrent();
        private void OnHideClick(object sender, RoutedEventArgs e)    => _vm.HideCurrent();
        private void OnResetClick(object sender, RoutedEventArgs e)   => _vm.ResetView();
        private void OnRefreshClick(object sender, RoutedEventArgs e) => _vm.Search();

        /// <summary>Double-click su riga → seleziona elemento in Revit (mostra nei Properties).</summary>
        private void OnRowDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (GridElements.SelectedItem is ElementRowVm row)
                _vm.SelectInRevit(row.ElementId);
        }

        /// <summary>
        /// Lazy-load dei valori distinti quando l'utente apre la combo del campo Valore
        /// di una regola parametrica (= o ≠). Chiamato da SelectionView.xaml via DropDownOpened.
        /// </summary>
        private void OnValueComboDropDownOpened(object sender, System.EventArgs e)
        {
            if (sender is ComboBox combo && combo.DataContext is ParamFilterRuleVm rule)
                _vm.LoadParamValuesCommand.Execute(rule);
        }

        // -------------------------------------------------------------------
        // Colonne personalizzate
        // -------------------------------------------------------------------

        /// <summary>
        /// Sync dinamico: quando il VM aggiunge/rimuove una FilterableParam in CustomColumns,
        /// creiamo/rimuoviamo la DataGridTextColumn corrispondente (binding sull'indexer
        /// ElementRowVm[DisplayName]).
        /// </summary>
        private void OnCustomColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (FilterableParam fp in e.NewItems)
                    AddDataGridColumn(fp);
            }
            else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
            {
                foreach (FilterableParam fp in e.OldItems)
                    RemoveDataGridColumn(fp.DisplayName);
            }
            else if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var col in _customColumnMap.Values.ToList())
                    GridElements.Columns.Remove(col);
                _customColumnMap.Clear();
            }
        }

        private void AddDataGridColumn(FilterableParam fp)
        {
            if (_customColumnMap.ContainsKey(fp.DisplayName)) return;
            var col = new DataGridTextColumn
            {
                Header = fp.DisplayName,
                Binding = new Binding("[" + fp.DisplayName + "]"),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                MinWidth = 60
            };
            GridElements.Columns.Add(col);
            _customColumnMap[fp.DisplayName] = col;
        }

        private void RemoveDataGridColumn(string displayName)
        {
            if (_customColumnMap.TryGetValue(displayName, out var col))
            {
                GridElements.Columns.Remove(col);
                _customColumnMap.Remove(displayName);
            }
        }

        /// <summary>
        /// Right-click su un'intestazione → costruisce e mostra un ContextMenu con:
        /// - Visibili: checkbox per ogni colonna (standard e custom)
        /// - "Aggiungi colonna parametro…" con sub-menu dei parametri disponibili
        /// - "Rimuovi colonna" (solo se la colonna cliccata è custom)
        /// </summary>
        private void OnColumnHeaderRightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not DataGridColumnHeader header) return;
            var clickedColumn = header.Column;
            var menu = new ContextMenu();

            // --- Sezione 1: visibilità colonne attuali ---
            foreach (var col in GridElements.Columns.OrderBy(c => c.DisplayIndex))
            {
                var headerText = col.Header?.ToString() ?? "(vuoto)";
                var mi = new MenuItem
                {
                    Header = headerText,
                    IsCheckable = true,
                    IsChecked = col.Visibility == Visibility.Visible,
                    StaysOpenOnClick = true
                };
                var columnRef = col;
                mi.Click += (_, _) =>
                    columnRef.Visibility = mi.IsChecked ? Visibility.Visible : Visibility.Collapsed;
                menu.Items.Add(mi);
            }

            menu.Items.Add(new Separator());

            // --- Sezione 2: aggiungi colonna parametro ---
            var addItem = new MenuItem { Header = "Aggiungi colonna parametro…" };
            var addable = _vm.GetAddableColumnParams();
            if (addable.Count == 0)
            {
                addItem.IsEnabled = false;
                addItem.ToolTip = "Seleziona prima una categoria";
            }
            else
            {
                foreach (var fp in addable)
                {
                    var sub = new MenuItem { Header = fp.DisplayName };
                    var fpRef = fp;
                    sub.Click += (_, _) => _vm.AddCustomColumn(fpRef);
                    addItem.Items.Add(sub);
                }
            }
            menu.Items.Add(addItem);

            // --- Sezione 3: rimuovi colonna (solo se custom) ---
            if (clickedColumn != null && _customColumnMap.ContainsValue(clickedColumn))
            {
                var removeItem = new MenuItem
                {
                    Header = "Rimuovi colonna «" + clickedColumn.Header + "»"
                };
                var displayName = clickedColumn.Header?.ToString() ?? "";
                removeItem.Click += (_, _) => _vm.RemoveCustomColumn(displayName);
                menu.Items.Add(removeItem);
            }

            menu.PlacementTarget = header;
            menu.IsOpen = true;
            e.Handled = true;
        }
    }
}
