using Microsoft.Win32;
using QtoRevitPlugin.Application;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.UI.ViewModels;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using TaskDialogCommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons;
using TaskDialogResult = Autodesk.Revit.UI.TaskDialogResult;

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
        /// Copia le Informazioni Progetto da un altro file .cme di progetto.
        /// Utile per duplicare l'intestazione tra computi simili (es. fasi diverse
        /// dello stesso progetto, o computi template). NON sovrascrive campi
        /// già compilati nel VM corrente (pattern "merge non invasivo" come Eredita).
        /// </summary>
        private void OnCopyFromOtherCmeClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ProjectInfoViewModel vm)
            {
                TaskDialog.Show("CME – Informazioni Progetto", "ViewModel non disponibile.");
                return;
            }

            var dlg = new OpenFileDialog
            {
                Title = "Copia Informazioni Progetto da un altro .cme",
                Filter = "File CME (*.cme)|*.cme|Tutti i file (*.*)|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog() != true) return;

            var sourcePath = dlg.FileName;

            // Evita di copiare dallo stesso file attualmente aperto
            var activeRepo = QtoApplication.Instance?.SessionManager?.Repository;
            var activeDbPath = activeRepo?.DatabasePath;
            if (!string.IsNullOrEmpty(activeDbPath) &&
                string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(activeDbPath!),
                    System.StringComparison.OrdinalIgnoreCase))
            {
                TaskDialog.Show("CME – Copia Informazioni Progetto",
                    "Hai selezionato lo stesso file attualmente aperto. Scegli un .cme diverso.");
                return;
            }

            // Guard aggiuntivo: impedisce l'apertura di un DB già aperto dalla
            // UserLibrary globale (aprirebbe una seconda connessione in scrittura
            // sullo stesso file e attiverebbe MigrateIfNeeded → locking potenziale).
            var userLibPath = QtoApplication.Instance?.UserLibrary?.Library?.DatabasePath;
            if (!string.IsNullOrEmpty(userLibPath) &&
                string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(userLibPath!),
                    System.StringComparison.OrdinalIgnoreCase))
            {
                TaskDialog.Show("CME – Copia Informazioni Progetto",
                    "Il file selezionato è la UserLibrary globale del plugin, non un file .cme di progetto.\n\n" +
                    "Scegli un file .cme creato dal plugin CME.");
                return;
            }

            try
            {
                using var srcRepo = new QtoRepository(sourcePath);

                // Prendi la prima sessione del DB sorgente (tipicamente c'è una sola
                // sessione attiva per file). Se ci sono più sessioni, usiamo la prima
                // con ProjectInfo valorizzato.
                QtoRevitPlugin.Models.ProjectInfo? srcInfo = null;
                using (var conn = new Microsoft.Data.Sqlite.SqliteConnection(
                    $"Data Source={sourcePath};Mode=ReadOnly;Pooling=False"))
                {
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT SessionId FROM ProjectInfo LIMIT 1";
                    var sid = cmd.ExecuteScalar();
                    if (sid != null && sid != System.DBNull.Value)
                        srcInfo = srcRepo.GetProjectInfo(System.Convert.ToInt32(sid));
                }

                if (srcInfo == null)
                {
                    TaskDialog.Show("CME – Copia Informazioni Progetto",
                        "Il file selezionato non contiene Informazioni Progetto da copiare.\n\n" +
                        "Probabilmente è un .cme creato prima dell'introduzione della scheda\n" +
                        "Informazioni Progetto, oppure non è mai stata salvata.");
                    return;
                }

                // Preview + conferma (10 campi principali)
                var preview = BuildPreviewText(srcInfo);
                var td = new TaskDialog("Copia Informazioni Progetto")
                {
                    MainInstruction = $"Copiare le informazioni da «{Path.GetFileName(sourcePath)}»?",
                    MainContent = preview +
                        "\n\nI campi già compilati nel .cme corrente NON saranno sovrascritti.",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton = TaskDialogResult.Yes
                };
                if (td.Show() != TaskDialogResult.Yes) return;

                // Merge non invasivo — solo campi vuoti del VM corrente
                int copied = 0;
                copied += CopyIfEmpty(srcInfo.DenominazioneOpera, vm.DenominazioneOpera, v => vm.DenominazioneOpera = v);
                copied += CopyIfEmpty(srcInfo.Committente, vm.Committente, v => vm.Committente = v);
                copied += CopyIfEmpty(srcInfo.Impresa, vm.Impresa, v => vm.Impresa = v);
                copied += CopyIfEmpty(srcInfo.RUP, vm.Rup, v => vm.Rup = v);
                copied += CopyIfEmpty(srcInfo.DirettoreLavori, vm.DirettoreLavori, v => vm.DirettoreLavori = v);
                copied += CopyIfEmpty(srcInfo.Luogo, vm.Luogo, v => vm.Luogo = v);
                copied += CopyIfEmpty(srcInfo.Comune, vm.Comune, v => vm.Comune = v);
                copied += CopyIfEmpty(srcInfo.Provincia, vm.Provincia, v => vm.Provincia = v);
                copied += CopyIfEmpty(srcInfo.CIG, vm.Cig, v => vm.Cig = v);
                copied += CopyIfEmpty(srcInfo.CUP, vm.Cup, v => vm.Cup = v);
                copied += CopyIfEmpty(srcInfo.RiferimentoPrezzario, vm.RiferimentoPrezzario, v => vm.RiferimentoPrezzario = v);

                vm.StatusMessage = copied == 0
                    ? "Nessun campo copiato (tutti quelli di origine erano vuoti o già presenti qui)."
                    : $"Copiati {copied} camp{(copied == 1 ? "o" : "i")} da «{Path.GetFileName(sourcePath)}». Verifica e salva.";
            }
            catch (System.Exception ex)
            {
                QtoRevitPlugin.Services.CrashLogger.WriteException("ProjectInfoView.OnCopyFromOtherCme", ex);
                TaskDialog.Show("CME – Errore",
                    $"Impossibile leggere il file .cme selezionato:\n{ex.Message}");
            }
        }

        /// <summary>Copia il valore solo se il campo destinazione è vuoto/whitespace. Ritorna 1 se ha copiato, 0 altrimenti.</summary>
        private static int CopyIfEmpty(string source, string current, System.Action<string> apply)
        {
            if (string.IsNullOrWhiteSpace(source)) return 0;
            if (!string.IsNullOrWhiteSpace(current)) return 0;
            apply(source);
            return 1;
        }

        /// <summary>Formatta le prime righe di anteprima per il TaskDialog di conferma.</summary>
        private static string BuildPreviewText(QtoRevitPlugin.Models.ProjectInfo info)
        {
            var rows = new System.Collections.Generic.List<string>();
            void Add(string label, string val)
            {
                if (!string.IsNullOrWhiteSpace(val)) rows.Add($"  • {label}: {val}");
            }
            Add("Denominazione", info.DenominazioneOpera);
            Add("Committente", info.Committente);
            Add("Impresa", info.Impresa);
            Add("RUP", info.RUP);
            Add("DL", info.DirettoreLavori);
            Add("Luogo", info.Luogo);
            Add("Comune", info.Comune);
            Add("CIG", info.CIG);
            Add("CUP", info.CUP);

            return rows.Count == 0
                ? "Il file sorgente ha i campi Informazioni Progetto vuoti."
                : "Campi disponibili nel file sorgente:\n" + string.Join("\n", rows);
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
