using Microsoft.Win32;
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
    /// Sprint 2 Task 5: gestione listini caricati (import DCF/XPWE/XML/XLSX/CSV) +
    /// ricerca voci EP a 3 livelli (Exact → FTS5 → Fuzzy Levenshtein) con debounce.
    /// </summary>
    public partial class SetupView : UserControl
    {
        private readonly SetupViewModel _vm;

        public SetupView()
        {
            _vm = new SetupViewModel();
            DataContext = _vm;
            InitializeComponent();
        }

        // ---------------------------------------------------------------------
        // Import listino
        // ---------------------------------------------------------------------

        private void OnImportClick(object sender, RoutedEventArgs e)
        {
            if (!_vm.HasSessionActive)
            {
                TaskDialog.Show("CME – Setup",
                    "Apri un computo .cme dal menu «Sessione ▾» prima di importare listini.");
                return;
            }

            var dlg = new OpenFileDialog
            {
                Title = "Importa listino prezzi",
                Filter = "Listini supportati (*.dcf;*.xpwe;*.xml;*.xlsx;*.xlsm;*.csv;*.tsv;*.txt)|" +
                         "*.dcf;*.xpwe;*.xml;*.xlsx;*.xlsm;*.csv;*.tsv;*.txt|" +
                         "ACCA PriMus XML (*.dcf;*.xpwe;*.xml)|*.dcf;*.xpwe;*.xml|" +
                         "Excel (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|" +
                         "CSV/TSV (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt|" +
                         "Tutti i file (*.*)|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                var result = _vm.ImportFromFile(dlg.FileName);

                if (result != null && result.Warnings.Count > 0)
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

        // ---------------------------------------------------------------------
        // Elimina listino
        // ---------------------------------------------------------------------

        private void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            var selected = _vm.SelectedPriceList;
            if (selected == null)
            {
                TaskDialog.Show("CME – Setup", "Seleziona un listino nella tabella da eliminare.");
                return;
            }

            var td = new TaskDialog("Elimina listino")
            {
                MainInstruction = $"Eliminare il listino «{selected.Name}»?",
                MainContent = $"{selected.RowCount} voci saranno rimosse dal computo corrente.\n" +
                              "L'operazione non è reversibile ma non tocca il file sorgente.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No
            };
            if (td.Show() != TaskDialogResult.Yes) return;

            try
            {
                _vm.DeleteSelected();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("CME – Errore eliminazione", ex.Message);
            }
        }
    }
}
