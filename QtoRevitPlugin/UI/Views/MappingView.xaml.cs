using QtoRevitPlugin.Services;
using QtoRevitPlugin.UI.ViewModels;
using System.Windows;
using System.Windows.Controls;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using TaskDialogCommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons;
using TaskDialogResult = Autodesk.Revit.UI.TaskDialogResult;

namespace QtoRevitPlugin.UI.Views
{
    /// <summary>
    /// Sprint 4 Task 3 — MappingView: 3 tab per la configurazione delle sorgenti di quantità.
    ///   • Tab 0 (Famiglie)  → preview read-only FamilyType aggregate (Sprint 5: assegnazione EP reale)
    ///   • Tab 1 (Locali)    → CRUD in-memory di RoomMappingConfig + formula NCalc + test su Room
    ///   • Tab 2 (Manuali)   → CRUD in-memory di voci ManualQuantityEntry + lookup UserLibrary
    ///
    /// La view è registrata sotto il tag QtoViewKey.Tagging finché non avremo una TaggingView dedicata in Sprint 5.
    /// </summary>
    public partial class MappingView : UserControl
    {
        private readonly MappingViewModel _vm;

        public MappingView()
        {
            _vm = new MappingViewModel();
            DataContext = _vm;
            InitializeComponent();
        }

        /// <summary>Apre la scheda in finestra separata (workflow multi-monitor).</summary>
        private void OnPopoutClick(object sender, RoutedEventArgs e)
            => PopoutWindow.Popout(new MappingView(), "CME · Mapping Sorgenti");

        // =====================================================================
        // Tab 1 — Famiglie
        // =====================================================================

        private void OnRefreshFamiliesClick(object sender, RoutedEventArgs e) => _vm.RefreshFamilyTypes();

        private void OnAssignEpClick(object sender, RoutedEventArgs e)
        {
            var row = _vm.SelectedFamilyRow;
            if (row == null || _vm.SelectedFamilyCategory == null)
            {
                TaskDialog.Show("CME — Assegna EP",
                    "Seleziona una riga dalla tabella famiglie prima di assegnare una voce EP.");
                return;
            }

            try
            {
                var runner = new AssignEpCommandRunner();
                var result = runner.Run(_vm.SelectedFamilyCategory.Bic, row.Family, row.Type);

                if (!result.Cancelled)
                {
                    _vm.FamilyStatus = result.UserMessage;
                    _vm.RefreshFamilyTypes(); // rinfresca la tabella (conta istanze invariate, ma ok per visual feedback)
                }
            }
            catch (System.Exception ex)
            {
                TaskDialog.Show("CME — Errore assegnazione", ex.Message);
            }
        }

        // =====================================================================
        // Tab 2 — Locali (Sorgente B)
        // =====================================================================

        private void OnAddRoomMappingClick(object sender, RoutedEventArgs e) => _vm.BeginAddRoomMapping();

        private void OnEditRoomMappingClick(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedRoomMapping == null)
            {
                TaskDialog.Show("CME — Mapping", "Seleziona una formula dalla lista da modificare.");
                return;
            }
            _vm.BeginEditRoomMapping();
        }

        private void OnDeleteRoomMappingClick(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedRoomMapping == null)
            {
                TaskDialog.Show("CME — Mapping", "Seleziona una formula dalla lista da eliminare.");
                return;
            }

            var td = new TaskDialog("Elimina formula Room")
            {
                MainInstruction = $"Eliminare la formula «{_vm.SelectedRoomMapping.EpCode}»?",
                MainContent = "La configurazione sarà rimossa dalla sessione corrente.\n" +
                              "Per ora la persistenza è in-memory (Sprint 5 aggiungerà il save DB).",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No
            };
            if (td.Show() == TaskDialogResult.Yes) _vm.DeleteRoomMapping();
        }

        private void OnSaveRoomMappingClick(object sender, RoutedEventArgs e) => _vm.SaveRoomMapping();

        private void OnCancelRoomMappingClick(object sender, RoutedEventArgs e) => _vm.CancelRoomMapping();

        private void OnTestFormulaClick(object sender, RoutedEventArgs e) => _vm.TestRoomFormula();

        // =====================================================================
        // Tab 3 — Voci manuali (Sorgente C)
        // =====================================================================

        private void OnAddManualItemClick(object sender, RoutedEventArgs e) => _vm.BeginAddManualItem();

        private void OnEditManualItemClick(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedManualItem == null)
            {
                TaskDialog.Show("CME — Voci manuali", "Seleziona una voce dalla tabella da modificare.");
                return;
            }
            _vm.BeginEditManualItem();
        }

        private void OnDuplicateManualItemClick(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedManualItem == null)
            {
                TaskDialog.Show("CME — Voci manuali", "Seleziona una voce dalla tabella da duplicare.");
                return;
            }
            _vm.DuplicateManualItem();
        }

        private void OnDeleteManualItemClick(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedManualItem == null)
            {
                TaskDialog.Show("CME — Voci manuali", "Seleziona una voce dalla tabella da eliminare.");
                return;
            }

            var td = new TaskDialog("Elimina voce manuale")
            {
                MainInstruction = $"Eliminare la voce «{_vm.SelectedManualItem.EpCode}»?",
                MainContent = "La voce sarà rimossa dalla sessione corrente.\n" +
                              "Per ora la persistenza è in-memory (Sprint 5 aggiungerà il save DB).",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No
            };
            if (td.Show() == TaskDialogResult.Yes) _vm.DeleteManualItem();
        }

        private void OnSaveManualItemClick(object sender, RoutedEventArgs e) => _vm.SaveManualItem();

        private void OnCancelManualItemClick(object sender, RoutedEventArgs e) => _vm.CancelManualItem();

        /// <summary>
        /// Mini-dialog per cercare un codice EP nella UserLibrary (listini globali) e auto-compilare
        /// i campi dell'editor voce manuale. Usa la ricerca 3-livelli di PriceItemSearchService.
        /// </summary>
        private void OnLookupUserLibraryClick(object sender, RoutedEventArgs e)
        {
            if (_vm.EditingManualItem == null)
            {
                TaskDialog.Show("CME — UserLibrary", "Apri prima una voce in editing (+ Nuova voce o Modifica).");
                return;
            }

            var query = InputDialog.Prompt(
                "Cerca voce EP nella UserLibrary",
                "Codice esatto o parola chiave (ricerca 3 livelli: Exact → FTS5 → Fuzzy):",
                string.IsNullOrWhiteSpace(_vm.EditingManualItem.EpCode) ? "" : _vm.EditingManualItem.EpCode);

            if (string.IsNullOrWhiteSpace(query)) return;

            var msg = _vm.LookupFromUserLibrary(query);
            TaskDialog.Show("CME — UserLibrary", msg);
        }
    }
}
