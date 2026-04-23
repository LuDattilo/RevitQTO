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
    /// Sezione "Listino" del Setup: gestione listini (import DCF/XPWE/XML/XLSX/CSV)
    /// + ricerca FTS5 voci EP. Estratta da SetupView come UserControl dedicata per
    /// supportare popout multi-monitor.
    /// </summary>
    public partial class SetupListinoView : UserControl
    {
        private SetupViewModel Vm => (SetupViewModel)DataContext;
        private CatalogBrowserWindow? _catalogWindow;

        public SetupListinoView()
        {
            InitializeComponent();
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
            PopoutWindow.Popout(new SetupListinoView(), "CME · Setup · Listino");
        }

        private void OnBrowseCatalogClick(object sender, RoutedEventArgs e)
        {
            if (Vm.PriceLists.Count == 0)
            {
                TaskDialog.Show("CME – Setup",
                    "Nessun listino caricato nella UserLibrary. Importane uno con «+ Importa listino…» prima di sfogliare.");
                return;
            }

            if (_catalogWindow != null && _catalogWindow.IsLoaded)
            {
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

        private void OnAddProjectFavoriteClick(object sender, RoutedEventArgs e)
        {
            Vm.AddSelectedToProjectFavorites();
        }

        private void OnAddPersonalFavoriteClick(object sender, RoutedEventArgs e)
        {
            Vm.AddSelectedToPersonalFavorites();
        }

        private void OnRemoveFavoriteClick(object sender, RoutedEventArgs e)
        {
            Vm.RemoveSelectedFavorite();
        }
    }
}
