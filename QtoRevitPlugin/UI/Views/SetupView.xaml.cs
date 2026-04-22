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
            // UserLibrary è sempre disponibile dopo OnStartup — nessun pre-requisito di computo aperto
            if (!_vm.HasSessionActive)
            {
                TaskDialog.Show("CME – Setup",
                    "UserLibrary non inizializzata. Riavvia Revit.");
                return;
            }

            // Priorità formati (spec):
            //   1. XML (prezzari regionali open data, es. Regione Toscana EASY schema)
            //   2. XPWE (DEI, PriMus-net interscambio tra software professionali)
            //   3. Excel/CSV (import universale da qualsiasi fonte)
            //   ❌ DCF binario ACCA proprietario: NON supportato. Warning guidato nel parser.
            var dlg = new OpenFileDialog
            {
                Title = "Importa listino prezzi",
                Filter = "Listini supportati (*.xml;*.xpwe;*.xlsx;*.xlsm;*.csv;*.tsv;*.txt)|" +
                         "*.xml;*.xpwe;*.xlsx;*.xlsm;*.csv;*.tsv;*.txt|" +
                         "XML - Prezzari regionali (*.xml)|*.xml|" +
                         "XPWE - DEI / PriMus-net (*.xpwe)|*.xpwe|" +
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

                if (result == null) return;

                if (result.Items.Count == 0)
                {
                    // Fallimento totale: spiega all'utente cosa è successo
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

        // ---------------------------------------------------------------------
        // Elimina listino
        // ---------------------------------------------------------------------

        // ---------------------------------------------------------------------
        // Pop-out: apre questa scheda in finestra separata (workflow multi-monitor)
        // ---------------------------------------------------------------------

        private void OnPopoutClick(object sender, RoutedEventArgs e)
        {
            PopoutWindow.Popout(new SetupView(), "CME · Setup");
        }

        // ---------------------------------------------------------------------
        // Sfoglia listino — apre Window standalone con TreeView gerarchico
        // ---------------------------------------------------------------------

        private CatalogBrowserWindow? _catalogWindow;

        private void OnBrowseCatalogClick(object sender, RoutedEventArgs e)
        {
            if (_vm.PriceLists.Count == 0)
            {
                TaskDialog.Show("CME – Setup",
                    "Nessun listino caricato nella UserLibrary. Importane uno con «+ Importa listino…» prima di sfogliare.");
                return;
            }

            // Riusa la window se già aperta, altrimenti ne crea una nuova non-modal.
            if (_catalogWindow != null && _catalogWindow.IsLoaded)
            {
                _catalogWindow.Activate();
                return;
            }
            _catalogWindow = new CatalogBrowserWindow();
            _catalogWindow.Closed += (_, _) => _catalogWindow = null;
            _catalogWindow.Show();
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
                _vm.DeleteSelected();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("CME – Errore eliminazione", ex.Message);
            }
        }
    }
}
