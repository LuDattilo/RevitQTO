using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QtoRevitPlugin.Application;
using QtoRevitPlugin.Computo.Preflight;
using QtoRevitPlugin.Services.Computi;

namespace QtoRevitPlugin.UI.ViewModels
{
    /// <summary>
    /// ViewModel della vista Verifica (pre-consegna). Sostituisce lo stub PreviewView: esegue
    /// <see cref="ComputoAnalysisService.GetPreflight"/> sulla sessione attiva e mostra i findings
    /// aggregati per classe (coerenza interna, percentuali, coerenza UM), con le classi non applicabili
    /// dichiarate "skipped" e il relativo motivo. Read-only: non decide "consegna sì/no".
    /// </summary>
    public partial class VerificaViewModel : ViewModelBase
    {
        public ObservableCollection<PreflightClassVm> Classes { get; } = new();
        public ObservableCollection<PreflightFindingVm> Findings { get; } = new();

        [ObservableProperty] private bool _isRunning;
        [ObservableProperty] private bool _hasReport;
        [ObservableProperty] private int _errorCount;
        [ObservableProperty] private int _warningCount;

        // Percentuali facoltative: se impostate, la classe "percentages" viene verificata.
        [ObservableProperty] private double? _markupPercent;
        [ObservableProperty] private double? _defaultVatPercent;

        [ObservableProperty] private string _statusMessage =
            "Premi «Esegui verifica» per controllare il computo prima della consegna.";

        public bool HasNoIssues => HasReport && ErrorCount == 0 && WarningCount == 0;

        partial void OnHasReportChanged(bool value) => OnPropertyChanged(nameof(HasNoIssues));
        partial void OnErrorCountChanged(int value) => OnPropertyChanged(nameof(HasNoIssues));
        partial void OnWarningCountChanged(int value) => OnPropertyChanged(nameof(HasNoIssues));

        [RelayCommand]
        private void Run()
        {
            var repo = QtoApplication.Instance?.SessionManager?.Repository;
            var session = QtoApplication.Instance?.SessionManager?.ActiveSession;
            if (repo == null || session == null)
            {
                StatusMessage = "Nessun computo aperto. Apri o crea un file .cme dalla Home.";
                return;
            }

            IsRunning = true;
            Classes.Clear();
            Findings.Clear();
            HasReport = false;

            try
            {
                var report = new ComputoAnalysisService(repo).GetPreflight(session.Id, MarkupPercent, DefaultVatPercent);

                foreach (var c in report.Classes)
                {
                    Classes.Add(new PreflightClassVm
                    {
                        Name = ClassLabel(c.ClassName),
                        IsSkipped = c.Status == "skipped",
                        Detail = c.Status == "skipped"
                            ? (c.SkipReason ?? "non applicabile")
                            : (c.Findings.Count == 0 ? "OK · nessun rilievo" : $"{c.Findings.Count} rilievo/i"),
                    });

                    foreach (var f in c.Findings)
                        Findings.Add(new PreflightFindingVm
                        {
                            ClassName = ClassLabel(c.ClassName),
                            IsError = f.Severity == PreflightSeverity.Error,
                            SeverityLabel = f.Severity == PreflightSeverity.Error ? "ERRORE" : "Avviso",
                            Code = f.Code,
                            Voce = f.Voce,
                            Message = f.Message,
                        });
                }

                ErrorCount = report.ErrorCount;
                WarningCount = report.WarningCount;
                HasReport = true;

                StatusMessage = ErrorCount == 0 && WarningCount == 0
                    ? "Verifica completata · nessun rilievo bloccante o d'attenzione."
                    : $"Verifica completata · {ErrorCount} errore/i · {WarningCount} avviso/i.";
            }
            finally
            {
                IsRunning = false;
            }
        }

        private static string ClassLabel(string className) => className switch
        {
            "internal_consistency" => "Coerenza interna",
            "percentages" => "Percentuali (maggiorazione / IVA)",
            "unit_consistency" => "Coerenza unità di misura",
            "completeness" => "Completezza",
            "double_count" => "Doppio conteggio",
            "unit_reconciliation" => "Riconciliazione con abaco",
            _ => className,
        };
    }

    public sealed class PreflightClassVm
    {
        public string Name { get; set; } = "";
        public bool IsSkipped { get; set; }
        public string Detail { get; set; } = "";
    }

    public sealed class PreflightFindingVm
    {
        public string ClassName { get; set; } = "";
        public bool IsError { get; set; }
        public string SeverityLabel { get; set; } = "";
        public string Code { get; set; } = "";
        public string Voce { get; set; } = "";
        public string Message { get; set; } = "";
    }
}
