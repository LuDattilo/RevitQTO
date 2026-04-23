using Microsoft.Win32;
using QtoRevitPlugin.Services;
using QtoRevitPlugin.UI.ViewModels;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using TaskDialogCommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons;
using TaskDialogResult = Autodesk.Revit.UI.TaskDialogResult;

namespace QtoRevitPlugin.UI.Views
{
    /// <summary>
    /// Sezione "Listino" del Setup: gestione listini (import DCF/XPWE/XML/XLSX/CSV)
    /// + ricerca FTS5 voci EP. Estratta da SetupView come UserControl dedicata per
    /// supportare sub-tab interni e popout multi-monitor.
    /// </summary>
    public partial class SetupListinoView : UserControl
    {
        private SetupViewModel Vm => (SetupViewModel)DataContext;
        private PopoutWindow? _popoutWindow;

        public SetupListinoView() : this(null, showPopoutButton: true)
        {
        }

        private SetupListinoView(SetupViewModel? viewModel, bool showPopoutButton)
        {
            DataContext = viewModel ?? new SetupViewModel();
            InitializeComponent();
            BtnPopout.Visibility = showPopoutButton ? Visibility.Visible : Visibility.Collapsed;

            // Connetti eventi VM → handlers view (per ContextMenu B1: Browse + Delete
            // richiedono interazione con dialog WPF/Revit che vivono nella view).
            Vm.BrowseRequested += (_, _) => OnBrowseCatalogClick(this, new RoutedEventArgs());
            Vm.DeleteRequested += (_, _) => OnDeleteClick(this, new RoutedEventArgs());
        }

        // =====================================================================
        // Drag & drop: trascina una voce dai risultati ricerca all'Expander
        // "I Miei Preferiti" per aggiungerla ai preferiti.
        // =====================================================================

        /// <summary>Formato dati usato per serializzare l'oggetto trascinato.</summary>
        private const string DragFormatPriceItemRow = "QtoRevitPlugin.PriceItemRow";

        /// <summary>Posizione del mouse al click — per calcolare soglia drag (SystemParameters).</summary>
        private System.Windows.Point? _dragStartPoint;

        private void OnSearchResultMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Registra la posizione, ma NON iniziare il drag qui — altrimenti blocchiamo
            // il click singolo (selezione riga) e il doppio click (toggle).
            _dragStartPoint = e.GetPosition(null);
        }

        private void OnSearchResultMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragStartPoint == null) return;
            if (!(sender is DataGrid grid)) return;

            var pos = e.GetPosition(null);
            var dx = System.Math.Abs(pos.X - _dragStartPoint.Value.X);
            var dy = System.Math.Abs(pos.Y - _dragStartPoint.Value.Y);
            if (dx < SystemParameters.MinimumHorizontalDragDistance &&
                dy < SystemParameters.MinimumVerticalDragDistance) return;

            if (grid.SelectedItem is not ViewModels.PriceItemRow row) return;

            // Guardia: non avviare drag su header
            var source = e.OriginalSource as DependencyObject;
            while (source != null && source != grid)
            {
                if (source is System.Windows.Controls.Primitives.DataGridColumnHeader) return;
                source = System.Windows.Media.VisualTreeHelper.GetParent(source);
            }

            try
            {
                var data = new DataObject(DragFormatPriceItemRow, row);
                DragDrop.DoDragDrop(grid, data, DragDropEffects.Copy);
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("SetupListinoView.OnSearchResultMouseMove", ex);
            }
            finally
            {
                _dragStartPoint = null;
            }
        }

        private void OnFavoritesDragEnter(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DragFormatPriceItemRow)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            // Feedback visivo: bordo dorato + sfondo leggermente giallo mentre il drag sorvola
            if (sender is Expander exp && e.Effects == DragDropEffects.Copy)
            {
                exp.BorderBrush = System.Windows.Media.Brushes.Goldenrod;
                exp.BorderThickness = new Thickness(2);
            }
            e.Handled = true;
        }

        private void OnFavoritesDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DragFormatPriceItemRow)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnFavoritesDragLeave(object sender, DragEventArgs e)
        {
            if (sender is Expander exp)
            {
                // Ripristina stile originale dell'Expander
                exp.ClearValue(Expander.BorderBrushProperty);
                exp.ClearValue(Expander.BorderThicknessProperty);
            }
            e.Handled = true;
        }

        private void OnFavoritesDrop(object sender, DragEventArgs e)
        {
            if (sender is Expander exp)
            {
                exp.ClearValue(Expander.BorderBrushProperty);
                exp.ClearValue(Expander.BorderThicknessProperty);
            }

            if (!e.Data.GetDataPresent(DragFormatPriceItemRow)) return;
            if (e.Data.GetData(DragFormatPriceItemRow) is not ViewModels.PriceItemRow row) return;

            Vm.AddFavoriteFromDrop(row);
            e.Handled = true;
        }

        private void OnImportClick(object sender, RoutedEventArgs e)
        {
            if (!Vm.HasUserLibrary)
            {
                TaskDialog.Show("CME – Setup", "UserLibrary non inizializzata. Riavvia Revit.");
                return;
            }

            var dlg = new OpenFileDialog
            {
                Title = "Importa listino prezzi",
                Filter = "Listini supportati (*.xml;*.xpwe;*.xlsx;*.xlsm;*.csv;*.tsv;*.txt;*.dcf)|" +
                         "*.xml;*.xpwe;*.xlsx;*.xlsm;*.csv;*.tsv;*.txt;*.dcf|" +
                         "XML - Prezzari regionali (*.xml)|*.xml|" +
                         "XPWE - DEI / PriMus-net (*.xpwe)|*.xpwe|" +
                         "DCF - ACCA PriMus (*.dcf)|*.dcf|" +
                         "Excel (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|" +
                         "CSV/TSV (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt|" +
                         "Tutti i file (*.*)|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                var result = Vm.ImportFromFile(dlg.FileName);
                if (result == null) return;

                if (result.Items.Count == 0)
                {
                    var detail = result.Warnings.Count > 0
                        ? string.Join("\n\n", result.Warnings.Take(3))
                        : "Il file non contiene voci EP leggibili o è in un formato non riconosciuto.";
                    TaskDialog.Show("CME – Import non completato",
                        $"Nessuna voce importata da «{System.IO.Path.GetFileName(dlg.FileName)}».\n\n{detail}");
                }
                else if (result.Warnings.Count > 0)
                {
                    var preview = string.Join("\n", result.Warnings.Take(5));
                    var suffix = result.Warnings.Count > 5 ? $"\n\n(+ altre {result.Warnings.Count - 5} warning)" : "";
                    TaskDialog.Show("CME – Import completato con warning",
                        $"Importate {result.Items.Count} voci.\n\nPrime warning:\n{preview}{suffix}");
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("CME – Errore import", ex.Message);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void OnPopoutClick(object sender, RoutedEventArgs e)
        {
            try
            {
                switch (FloatingWindowReusePolicy.Decide(_popoutWindow != null, _popoutWindow?.IsVisible == true))
                {
                    case FloatingWindowReuseAction.ActivateVisible:
                        _popoutWindow!.Activate();
                        return;
                    case FloatingWindowReuseAction.ShowHidden:
                        _popoutWindow!.Show();
                        _popoutWindow.Activate();
                        return;
                }

                _popoutWindow = PopoutWindow.Popout(
                    new SetupListinoView(Vm, showPopoutButton: false),
                    "CME · Setup · Listino");
                _popoutWindow.Closed += (_, _) => _popoutWindow = null;
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("SetupListinoView.Popout", ex);
                TaskDialog.Show("CME – Listino", $"Impossibile aprire la finestra Listino:\n{ex.Message}");
            }
        }

        private CatalogBrowserWindow? _catalogWindow;

        private void OnBrowseCatalogClick(object sender, RoutedEventArgs e)
        {
            if (Vm.PriceLists.Count == 0)
            {
                TaskDialog.Show("CME – Setup",
                    "Nessun listino caricato nella UserLibrary. Importane uno con «+ Importa listino…» prima di sfogliare.");
                return;
            }

            switch (FloatingWindowReusePolicy.Decide(_catalogWindow != null, _catalogWindow?.IsVisible == true))
            {
                case FloatingWindowReuseAction.ActivateVisible:
                    _catalogWindow!.Activate();
                    return;
                case FloatingWindowReuseAction.ShowHidden:
                    _catalogWindow!.Show();
                    _catalogWindow.Activate();
                    return;
            }

            _catalogWindow = new CatalogBrowserWindow();
            _catalogWindow.Closed += (_, _) => _catalogWindow = null;
            _catalogWindow.Show();
        }

        private void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            var selected = Vm.SelectedPriceList;
            if (selected == null)
            {
                TaskDialog.Show("CME – Setup", "Seleziona un listino nella tabella da eliminare.");
                return;
            }

            var td = new TaskDialog("Elimina listino dalla UserLibrary")
            {
                MainInstruction = $"Eliminare il listino «{selected.Name}» dalla libreria?",
                MainContent = $"{selected.RowCount} voci saranno rimosse dalla UserLibrary globale.\n" +
                              "L'operazione riguarda TUTTI i computi futuri e non è reversibile.\n" +
                              "Il file sorgente non viene toccato — potrai re-importarlo.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No
            };
            if (td.Show() != TaskDialogResult.Yes) return;

            try
            {
                Vm.DeleteSelected();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("CME – Errore eliminazione", ex.Message);
            }
        }

        /// <summary>
        /// Doppio click su una riga della DataGrid risultati ricerca → toggle preferito.
        /// UX shortcut richiesto dall'utente per evitare il click sul bottone ★ piccolo.
        /// Guardia: ignoriamo click sull'header (OriginalSource risale a DataGridColumnHeader)
        /// e click su celle vuote dopo l'ultimo risultato.
        /// </summary>
        private void OnSearchResultDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is DataGrid grid)) return;
            if (grid.SelectedItem is not PriceItemRow row) return;

            // Click sull'header non deve fare toggle
            var source = e.OriginalSource as DependencyObject;
            while (source != null && source != grid)
            {
                if (source is System.Windows.Controls.Primitives.DataGridColumnHeader) return;
                source = System.Windows.Media.VisualTreeHelper.GetParent(source);
            }

            Vm.ToggleFavoriteForRowCommand.Execute(row);
        }
    }
}
