using QtoRevitPlugin.UI.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace QtoRevitPlugin.UI.Views
{
    /// <summary>
    /// Sprint 4 Task 1 (§I9): UI per scegliere la fase Revit attiva del computo.
    /// Single-select RadioButton, count elementi lazy on-demand, persistenza su WorkSession.
    /// </summary>
    public partial class PhaseFilterView : UserControl
    {
        private readonly PhaseFilterViewModel _vm;

        public PhaseFilterView()
        {
            _vm = new PhaseFilterViewModel();
            DataContext = _vm;
            InitializeComponent();
        }

        /// <summary>Click su RadioButton → aggiorna SelectedPhase del VM via Tag.</summary>
        private void OnPhaseRadioClick(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is PhaseItemVm item)
            {
                _vm.SelectedPhase = item;
            }
        }

        /// <summary>Click "Calcola conteggio elementi" → popolazione lazy dei count.</summary>
        private void OnCountElementsClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                _vm.ComputeElementCounts();
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        /// <summary>Apre la scheda in finestra separata (workflow multi-monitor).</summary>
        private void OnPopoutClick(object sender, RoutedEventArgs e)
        {
            PopoutWindow.Popout(new PhaseFilterView(), "CME · Fasi Revit");
        }

        /// <summary>Click "Conferma fase" → salva sulla sessione.</summary>
        private void OnConfirmClick(object sender, RoutedEventArgs e)
        {
            if (!_vm.HasSessionActive)
            {
                TaskDialog.Show("CME – Fasi",
                    "Apri un computo .cme dal menu «Sessione ▾» prima di confermare la fase.");
                return;
            }
            if (_vm.SelectedPhase == null)
            {
                TaskDialog.Show("CME – Fasi",
                    "Seleziona una fase nella lista prima di confermare.");
                return;
            }

            _vm.ConfirmSelectedPhase();
        }
    }
}
