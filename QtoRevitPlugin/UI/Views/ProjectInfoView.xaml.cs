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
    /// Sezione "Informazioni Progetto" del Setup. Sprint UI-6: il legacy bottone
    /// "⬇ Eredita da Revit" è stato sostituito dal selector inline per-campo
    /// (<see cref="ProjectInfoFieldRowVm"/>). Il code-behind ora gestisce:
    /// - intercetta l'evento <see cref="ProjectInfoViewModel.AddSharedParameterRequested"/>
    ///   per aprire il dialog modale di creazione SP
    /// - supporta "Copia da altro CME…" per duplicare i metadati da un altro file
    /// </summary>
    public partial class ProjectInfoView : UserControl
    {
        public ProjectInfoView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is ProjectInfoViewModel oldVm)
                oldVm.AddSharedParameterRequested -= OnAddSharedParameterRequested;

            if (e.NewValue is ProjectInfoViewModel newVm)
                newVm.AddSharedParameterRequested += OnAddSharedParameterRequested;
        }

        /// <summary>
        /// L'utente ha scelto "+ Aggiungi parametro condiviso…" nel dropdown di una row.
        /// Apriamo il dialog modale e, su successo, reimpostiamo la SelectedSource con
        /// il nuovo parametro appena creato (prendendolo dalla refresh enumerator).
        /// </summary>
        private void OnAddSharedParameterRequested(object? sender, ProjectInfoFieldRowVm row)
        {
            if (DataContext is not ProjectInfoViewModel vm) return;

            var dlg = new AddSharedParameterDialog { Owner = Window.GetWindow(this) };
            dlg.InitFor(row.FieldKey);
            var ok = dlg.ShowDialog();
            if (ok != true || string.IsNullOrWhiteSpace(dlg.CreatedParameterName))
            {
                // Annullato o fallito: rollback della selezione a Manual per
                // evitare di lasciare il ComboBox su "+ Aggiungi…".
                row.SelectedSource = row.ParamSources.Count > 0 ? row.ParamSources[0] : null;
                return;
            }

            // Refresh lista sorgenti (include il parametro appena creato) e
            // riseleziona la voce giusta sulla row corrente.
            vm.RefreshParamSources();

            var createdName = dlg.CreatedParameterName!;
            ParamSourceOption? newOpt = null;
            foreach (var opt in row.ParamSources)
            {
                if (opt.Kind == ParamSourceOption.SourceKind.Param &&
                    string.Equals(opt.ParamName, createdName, System.StringComparison.Ordinal))
                {
                    newOpt = opt;
                    break;
                }
            }
            row.SelectedSource = newOpt ?? row.ParamSources[0];
        }

        /// <summary>
        /// Copia le Informazioni Progetto da un altro file .cme di progetto.
        /// Utile per duplicare l'intestazione tra computi simili (es. fasi diverse
        /// dello stesso progetto, o computi template). NON sovrascrive campi
        /// già compilati nel VM corrente (pattern "merge non invasivo").
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

            // Guard: impedisce aprire la UserLibrary (file DB globale)
            var userLibPath = QtoApplication.Instance?.UserLibrary?.Library?.DatabasePath;
            if (!string.IsNullOrEmpty(userLibPath) &&
                string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(userLibPath!),
                    System.StringComparison.OrdinalIgnoreCase))
            {
                TaskDialog.Show("CME – Copia Informazioni Progetto",
                    "Il file selezionato è la UserLibrary globale del plugin, non un file .cme di progetto.");
                return;
            }

            try
            {
                using var srcRepo = new QtoRepository(sourcePath);

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
                        "Il file selezionato non contiene Informazioni Progetto da copiare.");
                    return;
                }

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
    }
}
