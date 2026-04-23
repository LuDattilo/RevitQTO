using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using QtoRevitPlugin.Application;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Services;
using RevitTaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace QtoRevitPlugin.UI.Views
{
    /// <summary>
    /// Dialog modale per creare uno Shared Parameter legato a ProjectInformation,
    /// invocato dal dropdown "sorgente" della scheda Informazioni Progetto quando
    /// l'utente sceglie "+ Aggiungi parametro condiviso…".
    ///
    /// <para>Esito su successo: <see cref="CreatedParameterName"/> valorizzato e
    /// <c>DialogResult = true</c>. Il parametro è già stato scritto nel file SP
    /// scelto e bindato a <c>OST_ProjectInformation</c> (transazione Revit).</para>
    /// </summary>
    public partial class AddSharedParameterDialog : Window
    {
        /// <summary>
        /// Nome del parametro creato. Valido solo se DialogResult == true.
        /// </summary>
        public string? CreatedParameterName { get; private set; }

        public AddSharedParameterDialog()
        {
            InitializeComponent();
            Loaded += OnDialogLoaded;
        }

        /// <summary>
        /// Preseleziona nome suggerito (es. "CME_RUP") dato il FieldKey che
        /// l'utente sta mappando. Chiamare prima di ShowDialog.
        /// </summary>
        public void InitFor(string fieldKey)
        {
            NameBox.Text = ProjectInfoFieldKeys.SuggestedSharedParamNameFor(fieldKey);
            NameBox.SelectAll();
            DescBox.Text = $"Campo CME — {ProjectInfoFieldKeys.DisplayNameFor(fieldKey)}";
            SubtitleBlock.Text = $"Crea il parametro condiviso per il campo «{ProjectInfoFieldKeys.DisplayNameFor(fieldKey)}». " +
                                 "Sarà legato a ProjectInformation e selezionabile dal dropdown.";
        }

        private void OnDialogLoaded(object? sender, RoutedEventArgs e)
        {
            // Mostra all'utente il path corrente del Project SP (read-only info)
            try
            {
                var doc = QtoApplication.Instance?.CurrentUiApp?.ActiveUIDocument?.Document;
                var app = doc?.Application;
                var projectSp = app?.SharedParametersFilename;
                if (!string.IsNullOrWhiteSpace(projectSp) && File.Exists(projectSp))
                {
                    ProjectSpPathBlock.Text = projectSp!;
                }
                else
                {
                    ProjectSpPathBlock.Text = "(nessun file SP impostato nel progetto — fallback automatico al file CME)";
                    RadioProjectSp.IsEnabled = false;
                    RadioCmeSp.IsChecked = true;
                }
            }
            catch
            {
                ProjectSpPathBlock.Text = "(errore lettura path SP progetto)";
            }

            UpdateCreateEnabled();
            NameBox.Focus();
        }

        private void OnNameChanged(object sender, TextChangedEventArgs e)
        {
            UpdateCreateEnabled();
        }

        private void UpdateCreateEnabled()
        {
            var name = (NameBox?.Text ?? string.Empty).Trim();
            var valid = !string.IsNullOrWhiteSpace(name) && IsValidParamName(name);
            if (CreateButton != null) CreateButton.IsEnabled = valid;
            if (NameHint != null)
                NameHint.Text = valid
                    ? "Pronto per la creazione."
                    : "Nome non valido. Usa lettere, numeri, underscore (no spazi/simboli).";
        }

        /// <summary>
        /// Validazione soft del nome SP: Revit accetta praticamente qualsiasi
        /// stringa non-vuota, ma limitiamo a [A-Za-z0-9_] per evitare problemi
        /// in export/schedule. Lunghezza massima 48.
        /// </summary>
        private static bool IsValidParamName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 48) return false;
            foreach (var c in name)
                if (!char.IsLetterOrDigit(c) && c != '_') return false;
            return true;
        }

        private void OnCreateClick(object sender, RoutedEventArgs e)
        {
            var doc = QtoApplication.Instance?.CurrentUiApp?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                StatusBlock.Text = "Nessun documento Revit attivo.";
                return;
            }

            var name = (NameBox.Text ?? string.Empty).Trim();
            if (!IsValidParamName(name))
            {
                StatusBlock.Text = "Nome parametro non valido.";
                return;
            }

            // Scelta file SP: se user sceglie "CME dedicato", passo path esplicito;
            // altrimenti passo null → ResolveSpFilePath userà il Project SP.
            string? explicitPath = null;
            if (RadioCmeSp.IsChecked == true)
                explicitPath = SharedParameterFileHelper.GetCmeSpFilePath();

            try
            {
                var createdName = SharedParameterWriterService.CreateAndBindProjectInfoParam(
                    doc,
                    spFilePath: explicitPath,
                    paramName: name,
                    description: string.IsNullOrWhiteSpace(DescBox.Text) ? null : DescBox.Text);

                CreatedParameterName = createdName;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("AddSharedParameterDialog.OnCreate", ex);
                RevitTaskDialog.Show("CME — Errore creazione SP",
                    $"Impossibile creare il parametro «{name}»:\n\n{ex.Message}");
                StatusBlock.Text = "Errore — vedi TaskDialog";
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
