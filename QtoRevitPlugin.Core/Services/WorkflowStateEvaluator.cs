using System.Collections.Generic;
using QtoRevitPlugin.Models;

namespace QtoRevitPlugin.Services
{
    public class WorkflowStateEvaluator
    {
        public WorkflowAvailability Evaluate(bool hasActiveSession, bool hasActivePriceList)
        {
            if (!hasActiveSession)
            {
                return new WorkflowAvailability
                {
                    PrimaryMessage = "Per iniziare serve un computo attivo",
                    SecondaryMessage = "Crea o apri un file .cme per attivare il workflow CME"
                };
            }

            return new WorkflowAvailability
            {
                CanOpenSetup = true,
                CanOpenListino = true,
                CanOpenSelection = hasActivePriceList,
                PrimaryMessage = hasActivePriceList
                    ? "Workflow pronto per selezione e tagging"
                    : "Attiva un listino per procedere con la selezione",
                SecondaryMessage = hasActivePriceList
                    ? "Puoi procedere con selezione, tagging e verifica"
                    : "Importa o attiva un listino prima di selezionare gli elementi"
            };
        }

        /// <summary>
        /// Deriva lo stato dei 6 step del workflow CME (Setup → Listino → Selezione →
        /// Tagging → Verifica → Export) dalla sessione attiva e dal flag
        /// <paramref name="hasActivePriceList"/>. Usato da HomeView per rendere gli
        /// step cliccabili con feedback visivo (Done/Current/Available/Locked).
        ///
        /// Regole:
        ///   1. Setup: Done se sessione attiva (ProjectName valorizzato);
        ///      altrimenti Locked.
        ///   2. Listino: Done se <paramref name="hasActivePriceList"/>; Available
        ///      se sessione attiva ma listino non attivo; Locked se senza sessione.
        ///   3. Selezione: Done se TotalElements &gt; 0; Available se listino attivo;
        ///      Locked altrimenti.
        ///   4. Tagging: Done se TaggedElements == TotalElements &amp;&amp; TotalElements
        ///      &gt; 0; Current se TaggedElements &gt; 0 (parziale); Available se
        ///      selezione completata; Locked altrimenti.
        ///   5. Verifica: Done se TaggedPercent == 100 e TotalAmount &gt; 0 (oppure
        ///      Status == Completed); Available se tagging almeno avviato; Locked
        ///      altrimenti.
        ///   6. Export: Done se Status == Exported; Available se Verifica completata;
        ///      Locked altrimenti.
        ///
        /// Lo step "Current" viene promosso al primo step non-Done accessibile
        /// (ha stato Available o già Current) per dare un CTA visivo alla UI.
        /// </summary>
        public IReadOnlyList<WorkflowStepState> EvaluateSteps(WorkSession? session, bool hasActivePriceList)
        {
            var noSession = session == null;
            var total = session?.TotalElements ?? 0;
            var tagged = session?.TaggedElements ?? 0;
            var amount = session?.TotalAmount ?? 0.0;
            var status = session?.Status ?? SessionStatus.InProgress;

            // Calcoli derivati
            var hasSelection = total > 0;
            var taggingComplete = total > 0 && tagged >= total;
            var taggingPartial = tagged > 0 && tagged < total;
            var verificationDone = status == SessionStatus.Completed
                                   || (taggingComplete && amount > 0);
            var exportDone = status == SessionStatus.Exported;

            // --- 1. Setup progetto ---
            var setupStatus = noSession ? WorkflowStepStatus.Locked : WorkflowStepStatus.Done;
            var setupHint = noSession
                ? "Nessuna sessione attiva"
                : (session!.ProjectName ?? string.Empty);

            // --- 2. Listino ---
            WorkflowStepStatus listinoStatus;
            string listinoHint;
            if (noSession) { listinoStatus = WorkflowStepStatus.Locked; listinoHint = "Apri un computo"; }
            else if (hasActivePriceList) { listinoStatus = WorkflowStepStatus.Done; listinoHint = "Listino attivo"; }
            else { listinoStatus = WorkflowStepStatus.Available; listinoHint = "Attiva un listino"; }

            // --- 3. Selezione ---
            WorkflowStepStatus selStatus;
            string selHint;
            if (noSession || !hasActivePriceList) { selStatus = WorkflowStepStatus.Locked; selHint = "Richiede listino attivo"; }
            else if (hasSelection) { selStatus = WorkflowStepStatus.Done; selHint = $"{total} elementi selezionati"; }
            else { selStatus = WorkflowStepStatus.Available; selHint = "Nessun elemento selezionato"; }

            // --- 4. Tagging ---
            WorkflowStepStatus tagStatus;
            string tagHint;
            if (noSession || !hasSelection) { tagStatus = WorkflowStepStatus.Locked; tagHint = "Richiede selezione"; }
            else if (taggingComplete) { tagStatus = WorkflowStepStatus.Done; tagHint = $"{tagged}/{total} taggati"; }
            else if (taggingPartial) { tagStatus = WorkflowStepStatus.Current; tagHint = $"{tagged}/{total} taggati"; }
            else { tagStatus = WorkflowStepStatus.Available; tagHint = $"0/{total} taggati"; }

            // --- 5. Verifica ---
            WorkflowStepStatus verStatus;
            string verHint;
            if (noSession || !hasSelection) { verStatus = WorkflowStepStatus.Locked; verHint = "Richiede tagging avviato"; }
            else if (verificationDone) { verStatus = WorkflowStepStatus.Done; verHint = $"€ {amount:N2}"; }
            else if (taggingComplete) { verStatus = WorkflowStepStatus.Available; verHint = "Verifica pronta"; }
            else if (taggingPartial) { verStatus = WorkflowStepStatus.Available; verHint = "Anteprima parziale"; }
            else { verStatus = WorkflowStepStatus.Locked; verHint = "Richiede tagging"; }

            // --- 6. Export ---
            WorkflowStepStatus expStatus;
            string expHint;
            if (noSession) { expStatus = WorkflowStepStatus.Locked; expHint = "Apri un computo"; }
            else if (exportDone) { expStatus = WorkflowStepStatus.Done; expHint = "Esportato"; }
            else if (verificationDone) { expStatus = WorkflowStepStatus.Available; expHint = "Pronto all'export"; }
            else { expStatus = WorkflowStepStatus.Locked; expHint = "Richiede verifica"; }

            var steps = new List<WorkflowStepState>
            {
                new WorkflowStepState("Setup",        1, "Setup progetto", setupStatus, setupHint),
                new WorkflowStepState("Listino",      2, "Listino",        listinoStatus, listinoHint),
                new WorkflowStepState("Selection",    3, "Selezione",      selStatus, selHint),
                new WorkflowStepState("Tagging",      4, "Tagging",        tagStatus, tagHint),
                new WorkflowStepState("Verification", 5, "Verifica",       verStatus, verHint),
                new WorkflowStepState("Export",       6, "Esporta",        expStatus, expHint),
            };

            // Promuovi il primo step Available a Current per evidenziare il CTA.
            // Se già c'è un Current (tagging parziale) lascia tutto invariato.
            var hasCurrent = false;
            foreach (var s in steps) { if (s.Status == WorkflowStepStatus.Current) { hasCurrent = true; break; } }
            if (!hasCurrent)
            {
                for (var i = 0; i < steps.Count; i++)
                {
                    if (steps[i].Status == WorkflowStepStatus.Available)
                    {
                        steps[i] = new WorkflowStepState(
                            steps[i].Key, steps[i].Order, steps[i].Label,
                            WorkflowStepStatus.Current, steps[i].Hint);
                        break;
                    }
                }
            }

            return steps;
        }
    }
}
