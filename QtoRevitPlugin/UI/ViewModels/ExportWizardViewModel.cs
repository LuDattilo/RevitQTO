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
        [ObservableProperty] private bool _includeAuditFields;
        [ObservableProperty] private bool _includeDeletedAndSuperseded;
        [ObservableProperty] private string _companyLogoPath = "";
        [ObservableProperty] private bool _isExporting;
        [ObservableProperty] private string _statusMessage = "";

        public IReadOnlyList<IReportExporter> AvailableExporters => _exporters;

        public ExportWizardViewModel()
        {
            SelectedExporter = _exporters[0];
            var session = QtoApplication.Instance?.SessionManager?.ActiveSession;
            if (session != null)
            {
                Titolo = session.SessionName ?? "Computo";
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
                StatusMessage = "Nessun computo aperto.";
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
                if (File.Exists(dlg.FileName)) try { File.Delete(dlg.FileName); } catch { }
                MessageBox.Show($"Errore durante l'export: {ex.Message}", "Errore",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { IsExporting = false; }
        }
    }
}
