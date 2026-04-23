using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using QtoRevitPlugin.Application;
using QtoRevitPlugin.Reports;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace QtoRevitPlugin.UI.ViewModels
{
    public partial class ExportWizardViewModel : ObservableObject
    {
        private readonly List<IReportExporter> _exporters = new List<IReportExporter>
        {
            new XpweExporter(),
            new ExcelExporter(),
            new PdfExporter(),
            new CsvExporter()
        };

        [ObservableProperty] private IReportExporter? _selectedExporter;
        [ObservableProperty] private string _titolo = "";
        [ObservableProperty] private string _committente = "";
        [ObservableProperty] private string _direttoreLavori = "";
        // Sprint 10 (CRIT-E2): campi XPWE aggiuntivi caricati da ProjectInfo
        [ObservableProperty] private string _impresa = "";
        [ObservableProperty] private string _rup = "";
        [ObservableProperty] private DateTime? _dataComputo;
        [ObservableProperty] private DateTime? _dataPrezzi;
        [ObservableProperty] private string _riferimentoPrezzario = "";
        [ObservableProperty] private string _cig = "";
        [ObservableProperty] private string _cup = "";
        [ObservableProperty] private decimal _ribassoPercentuale;
        [ObservableProperty] private string _luogo = "";
        [ObservableProperty] private string _comune = "";
        [ObservableProperty] private string _provincia = "";
        [ObservableProperty] private bool _includeAuditFields;
        [ObservableProperty] private bool _includeDeletedAndSuperseded;
        [ObservableProperty] private string _companyLogoPath = "";
        [ObservableProperty] private bool _isExporting;
        [ObservableProperty] private string _statusMessage = "";

        public IReadOnlyList<IReportExporter> AvailableExporters => _exporters;

        public ExportWizardViewModel()
        {
            SelectedExporter = _exporters[0];

            // Sprint 10 (CRIT-E1): pre-popola i campi intestazione leggendo
            // ProjectInfo persistito nel .cme (se presente). Fallback ai valori
            // derivati dalla sessione se l'utente non ha ancora compilato
            // "Informazioni Progetto" nella SetupView.
            var session = QtoApplication.Instance?.SessionManager?.ActiveSession;
            var repo = QtoApplication.Instance?.SessionManager?.Repository;
            if (session == null) return;

            var info = repo?.GetProjectInfo(session.Id);
            if (info != null)
            {
                Titolo = !string.IsNullOrWhiteSpace(info.DenominazioneOpera)
                    ? info.DenominazioneOpera
                    : session.SessionName ?? "Computo";
                Committente = info.Committente;
                DirettoreLavori = info.DirettoreLavori;
                Impresa = info.Impresa;
                Rup = info.RUP;
                DataComputo = info.DataComputo;
                DataPrezzi = info.DataPrezzi;
                RiferimentoPrezzario = info.RiferimentoPrezzario;
                Cig = info.CIG;
                Cup = info.CUP;
                RibassoPercentuale = info.RibassoPercentuale;
                Luogo = info.Luogo;
                Comune = info.Comune;
                Provincia = info.Provincia;
                CompanyLogoPath = info.LogoPath;
                StatusMessage = "Intestazione caricata da Informazioni Progetto.";
            }
            else
            {
                Titolo = session.SessionName ?? "Computo";
                StatusMessage = "Compila Informazioni Progetto in Setup per pre-popolare l'intestazione.";
            }
        }

        [RelayCommand]
        private void BrowseLogo()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Immagini (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"
            };
            if (dlg.ShowDialog() == true) CompanyLogoPath = dlg.FileName;
        }

        [RelayCommand]
        private async Task ExportAsync()
        {
            if (SelectedExporter == null) return;
            var repo = QtoApplication.Instance?.SessionManager?.Repository;
            var session = QtoApplication.Instance?.SessionManager?.ActiveSession;
            if (repo == null || session == null)
            {
                // MED-E1: feedback visivo forte (non solo StatusMessage silenzioso)
                StatusMessage = "Nessun computo aperto.";
                MessageBox.Show(
                    "Nessun computo aperto.\n\nApri o crea un file .cme dal DockablePane prima di esportare.",
                    "Export — computo mancante",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = SelectedExporter.FileFilter,
                FileName = $"{session.SessionName}_{DateTime.Now:yyyy-MM-dd}{SelectedExporter.FileExtension}"
            };
            if (dlg.ShowDialog() != true) return;

            IsExporting = true;
            StatusMessage = "Esportazione in corso...";
            try
            {
                var options = new ReportExportOptions
                {
                    Titolo = Titolo,
                    Committente = Committente,
                    DirettoreLavori = DirettoreLavori,
                    Impresa = Impresa,
                    RUP = Rup,
                    DataComputo = DataComputo,
                    DataPrezzi = DataPrezzi,
                    RiferimentoPrezzario = RiferimentoPrezzario,
                    CIG = Cig,
                    CUP = Cup,
                    RibassoPercentuale = RibassoPercentuale,
                    Luogo = Luogo,
                    Comune = Comune,
                    Provincia = Provincia,
                    IncludeAuditFields = IncludeAuditFields,
                    IncludeDeletedAndSuperseded = IncludeDeletedAndSuperseded,
                    CompanyLogoPath = string.IsNullOrWhiteSpace(CompanyLogoPath) ? null : CompanyLogoPath
                };

                await Task.Run(() =>
                {
                    var builder = new ReportDataSetBuilder(repo);
                    var dataset = builder.Build(session.Id, options);
                    SelectedExporter.Export(dataset, dlg.FileName, options);
                });

                StatusMessage = $"Esportato in: {dlg.FileName}";
                MessageBox.Show($"Export completato:\n{dlg.FileName}", "Export",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                QtoRevitPlugin.Services.CrashLogger.WriteException("ExportAsync", ex);
                StatusMessage = $"Errore: {ex.Message}";
                // Cleanup file parziale
                // MED-E3: logga eventuale fallimento cleanup (file bloccato da altro processo
                // es. Excel aperto). Il log non rilancia — l'eccezione principale è già gestita.
                if (File.Exists(dlg.FileName))
                {
                    try { File.Delete(dlg.FileName); }
                    catch (Exception cleanupEx)
                    {
                        QtoRevitPlugin.Services.CrashLogger.WriteException(
                            "ExportAsync.CleanupPartialFile", cleanupEx);
                    }
                }
                MessageBox.Show($"Errore durante l'export: {ex.Message}", "Errore",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { IsExporting = false; }
        }
    }
}
