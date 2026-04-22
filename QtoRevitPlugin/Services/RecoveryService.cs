using Autodesk.Revit.DB;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using System;
using System.Globalization;
using System.Linq;

namespace QtoRevitPlugin.Services
{
    public enum RecoveryAction
    {
        /// <summary>Nessuna discrepanza significativa, nessun dialog.</summary>
        NoActionNeeded,

        /// <summary>Il modello contiene dati QTO più recenti del DB (scenario tipico: nuovo PC, DB perso).</summary>
        ImportFromModel,

        /// <summary>Il DB contiene dati più recenti del modello (rollback .rvt, etc).</summary>
        ExportToModel,

        /// <summary>Divergenza significativa — richiesto intervento utente.</summary>
        UserDecisionRequired
    }

    public class RecoveryAnalysis
    {
        public RecoveryAction RecommendedAction { get; set; }
        public DateTime? ModelLastSync { get; set; }
        public DateTime? DbLastSync { get; set; }
        public int ModelAssignmentCount { get; set; }
        public int DbAssignmentCount { get; set; }
        public string Summary { get; set; } = string.Empty;
    }

    /// <summary>
    /// Servizio di riconciliazione tra SQLite locale e Extensible Storage del modello.
    ///
    /// Principio (filosofia ES-centric): il .rvt è la verità autoritativa.
    /// SQLite è cache performante. In caso di divergenza, il default è "Importa dal modello".
    ///
    /// Heartbeat: il Shared Param `QtoLastSync` su ProjectInformation è aggiornato ad ogni
    /// WriteQtoHandler.Execute riuscito. Confrontando il suo timestamp con Sessions.LastSavedAt
    /// classifichiamo lo scenario senza dover scansionare tutti gli elementi.
    /// </summary>
    public class RecoveryService
    {
        public const string LastSyncParamName = "QtoLastSync";

        /// <summary>Soglia (secondi) sotto la quale consideriamo i due canali "allineati".</summary>
        private const double AlignmentToleranceSeconds = 5.0;

        /// <summary>Numero massimo di differenze per cui si procede senza dialog utente.</summary>
        private const int SilentSyncThreshold = 5;

        /// <summary>
        /// Analizza lo stato all'apertura del documento. Da chiamare dopo BindToDocument del SessionManager.
        /// </summary>
        public RecoveryAnalysis Analyze(Document doc, QtoRepository repo)
        {
            var analysis = new RecoveryAnalysis
            {
                ModelLastSync = ReadModelLastSync(doc),
                DbLastSync = GetLatestDbSync(doc, repo),
                ModelAssignmentCount = 0,     // Sprint 3+: full scan ES opzionale (se discrepanza > soglia)
                DbAssignmentCount = 0         // Sprint 3+: COUNT(*) su QtoAssignments
            };

            // Caso 1: DB vuoto, modello ha dati → Import (scenario: nuovo PC, cambio macchina)
            if (analysis.DbLastSync == null && analysis.ModelLastSync != null)
            {
                analysis.RecommendedAction = RecoveryAction.ImportFromModel;
                analysis.Summary = "Il database locale è vuoto ma il modello contiene dati QTO precedenti.";
                return analysis;
            }

            // Caso 2: entrambi vuoti → nuovo progetto, nessuna azione
            if (analysis.DbLastSync == null && analysis.ModelLastSync == null)
            {
                analysis.RecommendedAction = RecoveryAction.NoActionNeeded;
                analysis.Summary = "Progetto senza dati QTO precedenti.";
                return analysis;
            }

            // Caso 3: DB ha dati, modello no → modello aperto da utente senza plug-in, o ES mai scritto
            if (analysis.ModelLastSync == null && analysis.DbLastSync != null)
            {
                analysis.RecommendedAction = RecoveryAction.NoActionNeeded;
                analysis.Summary = "DB locale allineato. Il modello non contiene dati QTO — verranno scritti al primo tagging.";
                return analysis;
            }

            // Caso 4: entrambi hanno timestamp → confronto
            var diff = (analysis.ModelLastSync!.Value - analysis.DbLastSync!.Value).TotalSeconds;

            if (Math.Abs(diff) <= AlignmentToleranceSeconds)
            {
                analysis.RecommendedAction = RecoveryAction.NoActionNeeded;
                analysis.Summary = "DB e modello allineati.";
                return analysis;
            }

            if (diff > 0)
            {
                // Modello più recente (es. altro utente ha lavorato sul file condiviso)
                analysis.RecommendedAction = RecoveryAction.ImportFromModel;
                analysis.Summary = $"Il modello è più recente del DB locale di {FormatTimeSpan(diff)}.";
            }
            else
            {
                // DB più recente (es. .rvt rollback a backup precedente)
                analysis.RecommendedAction = RecoveryAction.ExportToModel;
                analysis.Summary = $"Il DB locale è più recente del modello di {FormatTimeSpan(-diff)}. " +
                                   "Il modello potrebbe essere stato ripristinato a un backup.";
            }

            return analysis;
        }

        /// <summary>
        /// Legge il timestamp ISO 8601 UTC dal Shared Param QtoLastSync su ProjectInformation.
        /// Ritorna null se il param non esiste o è vuoto (modello mai toccato dal plugin).
        /// </summary>
        public DateTime? ReadModelLastSync(Document doc)
        {
            if (doc == null) return null;
            var projInfo = doc.ProjectInformation;
            if (projInfo == null) return null;

            var param = projInfo.LookupParameter(LastSyncParamName);
            if (param == null || !param.HasValue) return null;

            var raw = param.AsString();
            if (string.IsNullOrWhiteSpace(raw)) return null;

            return DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : null;
        }

        private static DateTime? GetLatestDbSync(Document doc, QtoRepository repo)
        {
            var path = string.IsNullOrEmpty(doc.PathName) ? $"unsaved://{doc.Title}" : doc.PathName;
            var sessions = repo.GetSessionsForProject(path);
            var lastSaved = sessions
                .Select(s => s.LastSavedAt)
                .Where(t => t.HasValue)
                .DefaultIfEmpty()
                .Max();
            return lastSaved;
        }

        private static string FormatTimeSpan(double seconds)
        {
            var ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays} giorni";
            if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours} ore";
            if (ts.TotalMinutes >= 1) return $"{(int)ts.TotalMinutes} minuti";
            return $"{(int)ts.TotalSeconds} secondi";
        }

        /// <summary>
        /// Indica se la discrepanza è sufficientemente piccola da applicare l'azione raccomandata
        /// senza dialog utente (evita rumore UX su differenze di pochi secondi).
        /// </summary>
        public bool CanSyncSilently(RecoveryAnalysis analysis)
        {
            if (analysis.RecommendedAction == RecoveryAction.NoActionNeeded) return true;

            var delta = Math.Abs(analysis.ModelAssignmentCount - analysis.DbAssignmentCount);
            return delta <= SilentSyncThreshold;
        }
    }
}
