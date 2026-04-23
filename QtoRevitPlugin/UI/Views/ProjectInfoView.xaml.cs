using QtoRevitPlugin.Application;
using QtoRevitPlugin.UI.ViewModels;
using System.Windows;
using System.Windows.Controls;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace QtoRevitPlugin.UI.Views
{
    /// <summary>
    /// Sezione "Informazioni Progetto" del Setup: metadati persistiti in .cme
    /// (tabella ProjectInfo) + bottone "Eredita da Revit" per popolare i campi
    /// dal Project Information del modello Revit attivo (work-in-progress).
    /// </summary>
    public partial class ProjectInfoView : UserControl
    {
        public ProjectInfoView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Importa i campi disponibili dai Project Information + parametri di progetto
        /// del modello Revit attivo. Work-in-progress — mapping completo verrà
        /// raffinato in iterazione successiva.
        /// </summary>
        private void OnImportFromRevitClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ProjectInfoViewModel vm)
            {
                TaskDialog.Show("CME – Informazioni Progetto",
                    "ViewModel non disponibile.");
                return;
            }

            var uiApp = QtoApplication.Instance?.CurrentUiApp;
            var doc = uiApp?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                TaskDialog.Show("CME – Informazioni Progetto",
                    "Nessun documento Revit attivo. Apri un progetto prima di ereditare i dati.");
                return;
            }

            try
            {
                var pi = doc.ProjectInformation;
                if (pi == null)
                {
                    TaskDialog.Show("CME – Informazioni Progetto",
                        "Il documento non espone ProjectInformation.");
                    return;
                }

                // Mapping standard Revit → ProjectInfo:
                // - ProjectName        → DenominazioneOpera
                // - ClientName         → Committente
                // - Address            → Luogo (via/piazza)
                // - BuildingName       → (info disponibile ma non mappata qui)
                // - ProjectNumber      → (info non mappata ora — servirà per CIG/CUP custom?)
                // - Author             → (info non mappata)
                if (string.IsNullOrWhiteSpace(vm.DenominazioneOpera) && !string.IsNullOrWhiteSpace(pi.Name))
                    vm.DenominazioneOpera = pi.Name;
                if (string.IsNullOrWhiteSpace(vm.Committente) && !string.IsNullOrWhiteSpace(pi.ClientName))
                    vm.Committente = pi.ClientName;
                if (string.IsNullOrWhiteSpace(vm.Luogo) && !string.IsNullOrWhiteSpace(pi.Address))
                    vm.Luogo = pi.Address;

                // Parametri custom opzionali: cerca nel ProjectInformation parametri
                // con nomi noti (case-insensitive). Se l'utente ha un template con
                // "CME_RUP" o "RUP" o "CIG" ecc., li eredita automaticamente.
                TryImportCustomParam(pi, new[] { "CME_RUP", "RUP", "Responsabile" }, v => { if (string.IsNullOrWhiteSpace(vm.Rup)) vm.Rup = v; });
                TryImportCustomParam(pi, new[] { "CME_DL", "DirettoreLavori", "Direttore Lavori" }, v => { if (string.IsNullOrWhiteSpace(vm.DirettoreLavori)) vm.DirettoreLavori = v; });
                TryImportCustomParam(pi, new[] { "CME_Impresa", "Impresa" }, v => { if (string.IsNullOrWhiteSpace(vm.Impresa)) vm.Impresa = v; });
                TryImportCustomParam(pi, new[] { "CME_CIG", "CIG" }, v => { if (string.IsNullOrWhiteSpace(vm.Cig)) vm.Cig = v; });
                TryImportCustomParam(pi, new[] { "CME_CUP", "CUP" }, v => { if (string.IsNullOrWhiteSpace(vm.Cup)) vm.Cup = v; });
                TryImportCustomParam(pi, new[] { "CME_Comune", "Comune" }, v => { if (string.IsNullOrWhiteSpace(vm.Comune)) vm.Comune = v; });
                TryImportCustomParam(pi, new[] { "CME_Provincia", "Provincia" }, v => { if (string.IsNullOrWhiteSpace(vm.Provincia)) vm.Provincia = v; });

                vm.StatusMessage = "Campi ereditati dal modello Revit. Verifica e salva.";

                TaskDialog.Show("CME – Eredita da Revit",
                    "Campi ereditati dal Project Information del modello.\n\n" +
                    "Parametri Revit mappati:\n" +
                    "• Name → Denominazione opera\n" +
                    "• Client Name → Committente\n" +
                    "• Address → Luogo\n\n" +
                    "Parametri custom cercati (se presenti):\n" +
                    "CME_RUP, CME_DL, CME_Impresa, CME_CIG, CME_CUP, CME_Comune, CME_Provincia\n\n" +
                    "Non ho sovrascritto campi già compilati. Rivedi e premi Salva.");
            }
            catch (System.Exception ex)
            {
                QtoRevitPlugin.Services.CrashLogger.WriteException("ProjectInfoView.OnImportFromRevit", ex);
                TaskDialog.Show("CME – Errore", $"Errore durante l'import da Revit: {ex.Message}");
            }
        }

        /// <summary>
        /// Cerca un parametro custom con uno dei nomi possibili (case-insensitive)
        /// in ProjectInformation e invoca <paramref name="apply"/> con il valore.
        /// </summary>
        private static void TryImportCustomParam(
            Autodesk.Revit.DB.ProjectInfo pi,
            string[] candidateNames,
            System.Action<string> apply)
        {
            foreach (var name in candidateNames)
            {
                var p = pi.LookupParameter(name);
                if (p != null && p.HasValue)
                {
                    var val = p.AsString() ?? p.AsValueString();
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        apply(val);
                        return;  // primo match vince
                    }
                }
            }
        }
    }
}
