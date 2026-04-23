using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QtoRevitPlugin.AI;
using QtoRevitPlugin.Application;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace QtoRevitPlugin.UI.ViewModels
{
    /// <summary>
    /// ViewModel per la HealthView (Sprint UI-8). Orchestra il controllo di salute
    /// della sessione attiva via <see cref="HealthCheckGateway.RunAsync"/>:
    ///   - anomalie quantità (z-score, sempre disponibile)
    ///   - mismatch semantici categoria/EP (solo se AI Ready)
    ///
    /// Il VM è interamente async: l'utente clicca "Esegui controllo", il gateway
    /// viene invocato in background, le collection si popolano quando arrivano i
    /// risultati. IsRunning controlla visibilità spinner / disabilitazione bottone.
    /// </summary>
    public partial class HealthViewModel : ViewModelBase
    {
        public ObservableCollection<AnomalyRowVm> Anomalies { get; } = new();
        public ObservableCollection<MismatchRowVm> Mismatches { get; } = new();

        [ObservableProperty] private bool _isRunning;
        [ObservableProperty] private bool _hasReport;
        [ObservableProperty] private int _assignmentsAnalyzed;
        [ObservableProperty] private bool _aiUsed;
        [ObservableProperty] private DateTime? _generatedAt;
        [ObservableProperty] private string _statusMessage = "Premi «Esegui controllo» per analizzare il computo.";

        public int AnomaliesCount => Anomalies.Count;
        public int MismatchesCount => Mismatches.Count;
        public int TotalIssues => AnomaliesCount + MismatchesCount;
        public bool HasNoIssues => HasReport && TotalIssues == 0;

        public HealthViewModel()
        {
            // Aggiorna contatori derivati quando le collection cambiano
            Anomalies.CollectionChanged += (_, _) => RaiseDerivedCounters();
            Mismatches.CollectionChanged += (_, _) => RaiseDerivedCounters();
        }

        private void RaiseDerivedCounters()
        {
            OnPropertyChanged(nameof(AnomaliesCount));
            OnPropertyChanged(nameof(MismatchesCount));
            OnPropertyChanged(nameof(TotalIssues));
            OnPropertyChanged(nameof(HasNoIssues));
        }

        partial void OnHasReportChanged(bool value) => OnPropertyChanged(nameof(HasNoIssues));

        /// <summary>
        /// Esegue il controllo completo. Legge assegnazioni attive dalla sessione,
        /// chiama <see cref="HealthCheckGateway"/>, popola le collection.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanRun))]
        private async Task RunAsync()
        {
            var repo = QtoApplication.Instance?.SessionManager?.Repository;
            var session = QtoApplication.Instance?.SessionManager?.ActiveSession;
            if (repo == null || session == null)
            {
                StatusMessage = "Nessun computo aperto. Apri o crea un file .cme dalla Home.";
                return;
            }

            IsRunning = true;
            StatusMessage = "Analisi in corso…";
            Anomalies.Clear();
            Mismatches.Clear();
            HasReport = false;

            try
            {
                IReadOnlyList<QtoAssignment> assignments = repo.GetAssignments(session.Id)
                    .Where(a => a.AuditStatus == AssignmentStatus.Active)
                    .ToList();

                if (assignments.Count == 0)
                {
                    StatusMessage = "Nessuna assegnazione EP nel computo corrente. Usa il Tagging per aggiungerne.";
                    HasReport = true;
                    AssignmentsAnalyzed = 0;
                    return;
                }

                var settings = SettingsService.Load();
                var report = await HealthCheckGateway.RunAsync(
                    settings, repo, assignments,
                    timeoutMs: 15000,
                    logger: msg => CrashLogger.Warn(msg));

                // Popola collection ordinate
                foreach (var a in report.Anomalies.OrderByDescending(x => x.ZScore))
                    Anomalies.Add(AnomalyRowVm.FromModel(a));
                foreach (var m in report.Mismatches.OrderBy(x => x.Similarity))
                    Mismatches.Add(MismatchRowVm.FromModel(m));

                AssignmentsAnalyzed = report.AssignmentsAnalyzed;
                AiUsed = report.AiUsed;
                GeneratedAt = report.GeneratedAt.ToLocalTime();
                HasReport = true;

                StatusMessage = BuildResultMessage(report);
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("HealthViewModel.Run", ex);
                StatusMessage = $"Errore durante l'analisi: {ex.Message}";
                HasReport = false;
            }
            finally
            {
                IsRunning = false;
            }
        }

        private bool CanRun() => !IsRunning;

        partial void OnIsRunningChanged(bool value) => RunCommand.NotifyCanExecuteChanged();

        private string BuildResultMessage(HealthReport report)
        {
            if (report.TotalIssues == 0)
                return $"Controllo completato · {report.AssignmentsAnalyzed} assegnazioni analizzate · nessun problema rilevato.";

            var parts = new List<string>();
            if (report.Anomalies.Count > 0) parts.Add($"{report.Anomalies.Count} anomalia/e quantità");
            if (report.Mismatches.Count > 0) parts.Add($"{report.Mismatches.Count} mismatch semantici");
            var summary = string.Join(" · ", parts);

            var aiHint = report.AiUsed
                ? ""
                : " · (AI non disponibile: solo anomalie quantità rilevate)";
            return $"Controllo completato · {report.AssignmentsAnalyzed} assegnazioni · {summary}{aiHint}";
        }
    }

    /// <summary>Row VM per la lista anomalie quantità.</summary>
    public class AnomalyRowVm
    {
        public string UniqueId { get; set; } = string.Empty;
        public string EpCode { get; set; } = string.Empty;
        public double Quantity { get; set; }
        public double Mean { get; set; }
        public double StdDev { get; set; }
        public double ZScore { get; set; }
        public AnomalySeverity Severity { get; set; }
        public string Message { get; set; } = string.Empty;

        public string ZScoreLabel => ZScore.ToString("F2");
        public string QuantityLabel => Quantity.ToString("#,##0.##");
        public string MeanLabel => Mean.ToString("#,##0.##");
        public string SeverityLabel => Severity switch
        {
            AnomalySeverity.Alta => "⚠ ALTA",
            AnomalySeverity.Media => "△ Media",
            _ => "—"
        };

        public static AnomalyRowVm FromModel(QuantityAnomaly a) => new AnomalyRowVm
        {
            UniqueId = a.UniqueId,
            EpCode = a.EpCode,
            Quantity = a.Quantity,
            Mean = a.Mean,
            StdDev = a.StdDev,
            ZScore = a.ZScore,
            Severity = a.Severity,
            Message = a.Message,
        };
    }

    /// <summary>Row VM per la lista mismatch semantici.</summary>
    public class MismatchRowVm
    {
        public string UniqueId { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string FamilyName { get; set; } = string.Empty;
        public string EpCode { get; set; } = string.Empty;
        public string EpDescription { get; set; } = string.Empty;
        public float Similarity { get; set; }
        public int SuggestionsCount { get; set; }
        public string FirstSuggestionCode { get; set; } = string.Empty;
        public string FirstSuggestionScore { get; set; } = string.Empty;

        public string SimilarityLabel => $"{(int)Math.Round(Similarity * 100)}%";
        public string CategoryFamilyLabel => string.IsNullOrEmpty(FamilyName) ? Category : $"{Category} · {FamilyName}";
        public string SuggestionLabel => SuggestionsCount > 0
            ? $"→ {FirstSuggestionCode} ({FirstSuggestionScore})"
            : "(nessuna alternativa)";

        public static MismatchRowVm FromModel(SemanticMismatch m)
        {
            var vm = new MismatchRowVm
            {
                UniqueId = m.UniqueId,
                Category = m.Category,
                FamilyName = m.FamilyName,
                EpCode = m.EpCode,
                EpDescription = m.EpDescription,
                Similarity = m.Similarity,
                SuggestionsCount = m.Suggestions?.Count ?? 0,
            };
            if (m.Suggestions != null && m.Suggestions.Count > 0)
            {
                var top = m.Suggestions[0];
                vm.FirstSuggestionCode = top.PriceItem?.Code ?? string.Empty;
                vm.FirstSuggestionScore = $"{(int)Math.Round(top.Score * 100)}%";
            }
            return vm;
        }
    }
}
