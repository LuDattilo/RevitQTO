using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;

namespace QtoRevitPlugin.AI
{
    /// <summary>
    /// Gateway di alto livello per il pannello Health Check (Sprint UI-8).
    /// Combina due tipi di controllo sui <see cref="QtoAssignment"/> della
    /// sessione attiva:
    ///
    /// <list type="bullet">
    ///   <item><b>Anomalie quantità</b> (z-score statistico) — sempre disponibile,
    ///         non richiede AI. Usa <see cref="AnomalyDetector"/>.</item>
    ///   <item><b>Mismatch semantici</b> (cosine similarity cat+famiglia vs EP)
    ///         — richiede AI Ready, fallback vuoto altrimenti.</item>
    /// </list>
    ///
    /// Il gateway segue il principio graceful degradation di
    /// <see cref="AiSuggestionsGateway"/>: sempre ritorna <see cref="HealthReport"/>
    /// valido (mai null, mai throw).
    /// </summary>
    public static class HealthCheckGateway
    {
        /// <summary>
        /// Esegue entrambi i controlli sulla lista di assegnazioni attive.
        /// </summary>
        /// <param name="settings">CmeSettings per flag AiEnabled + URL Ollama.</param>
        /// <param name="repo">Repository attivo (richiesto dal factory AI).</param>
        /// <param name="assignments">
        /// Assegnazioni da analizzare. Tipicamente
        /// <c>repo.GetAssignments(sessionId).Where(a => a.AuditStatus == Active)</c>.
        /// </param>
        /// <param name="timeoutMs">Timeout hard per la parte AI (default 10s — la
        /// call batch embedding può essere lenta su molti assignment).</param>
        /// <param name="logger">Callback opzionale per log warn.</param>
        public static async Task<HealthReport> RunAsync(
            CmeSettings settings,
            IQtoRepository repo,
            IReadOnlyList<QtoAssignment> assignments,
            int timeoutMs = 10000,
            Action<string>? logger = null,
            CancellationToken externalCt = default)
        {
            if (assignments == null || assignments.Count == 0)
                return HealthReport.Empty();

            // 1. Anomalie quantità — sempre locale, istantaneo
            IReadOnlyList<QuantityAnomaly> anomalies;
            try
            {
                var detector = new AnomalyDetector();
                anomalies = detector.Detect(assignments);
            }
            catch (Exception ex)
            {
                logger?.Invoke($"HealthCheckGateway: AnomalyDetector throw — {ex.Message}");
                anomalies = Array.Empty<QuantityAnomaly>();
            }

            // 2. Mismatch semantici — solo se AI Ready
            IReadOnlyList<SemanticMismatch> mismatches = Array.Empty<SemanticMismatch>();
            bool aiUsed = false;

            if (settings != null && settings.AiEnabled && repo != null)
            {
                IQtoAiProvider? provider = null;
                try
                {
                    provider = QtoAiFactory.Create(settings, repo, logger);
                    if (provider != null && provider.IsAvailable)
                    {
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
                        cts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

                        mismatches = await provider
                            .FindSemanticMismatchesAsync(assignments, cts.Token)
                            .ConfigureAwait(false)
                            ?? Array.Empty<SemanticMismatch>();
                        aiUsed = true;
                    }
                }
                catch (OperationCanceledException)
                {
                    logger?.Invoke($"HealthCheckGateway: mismatch timeout dopo {timeoutMs}ms");
                }
                catch (Exception ex)
                {
                    logger?.Invoke($"HealthCheckGateway: FindSemanticMismatches throw — {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    if (provider is IDisposable d) d.Dispose();
                }
            }

            return new HealthReport
            {
                AssignmentsAnalyzed = assignments.Count,
                Anomalies = anomalies,
                Mismatches = mismatches,
                AiUsed = aiUsed,
                GeneratedAt = DateTime.UtcNow,
            };
        }
    }

    /// <summary>Risultato di un controllo Health Check.</summary>
    public class HealthReport
    {
        public int AssignmentsAnalyzed { get; set; }
        public IReadOnlyList<QuantityAnomaly> Anomalies { get; set; } = Array.Empty<QuantityAnomaly>();
        public IReadOnlyList<SemanticMismatch> Mismatches { get; set; } = Array.Empty<SemanticMismatch>();
        /// <summary>True se la parte AI è stata effettivamente eseguita. False se
        /// AI disabilitata / Ollama non raggiungibile / timeout / eccezione.</summary>
        public bool AiUsed { get; set; }
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

        public int TotalIssues => Anomalies.Count + Mismatches.Count;

        public static HealthReport Empty() => new HealthReport
        {
            AssignmentsAnalyzed = 0,
            Anomalies = Array.Empty<QuantityAnomaly>(),
            Mismatches = Array.Empty<SemanticMismatch>(),
            AiUsed = false,
            GeneratedAt = DateTime.UtcNow,
        };
    }
}
