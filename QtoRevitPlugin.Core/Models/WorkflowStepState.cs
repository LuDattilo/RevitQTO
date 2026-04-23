namespace QtoRevitPlugin.Models
{
    /// <summary>
    /// Stato operativo di uno step del workflow CME mostrato in HomeView.
    /// </summary>
    public enum WorkflowStepStatus
    {
        /// <summary>Step non ancora accessibile: mancano prerequisiti. UI: disabilitato, icona lucchetto.</summary>
        Locked,

        /// <summary>Step accessibile ma non ancora iniziato. UI: abilitato, icona vuota.</summary>
        Available,

        /// <summary>Step in corso (parzialmente completato). UI: evidenziato, icona ▶.</summary>
        Current,

        /// <summary>Step completato. UI: check verde, icona ✔.</summary>
        Done
    }

    /// <summary>
    /// Rappresentazione di uno step del workflow per HomeView: identifica
    /// la view target, l'etichetta, lo stato operativo e un hint di dettaglio
    /// (es. "32/120 taggati"). Lo stato è calcolato da
    /// <see cref="QtoRevitPlugin.Services.WorkflowStateEvaluator"/>.
    /// </summary>
    public class WorkflowStepState
    {
        public WorkflowStepState(
            string key,
            int order,
            string label,
            WorkflowStepStatus status,
            string hint = "")
        {
            Key = key;
            Order = order;
            Label = label;
            Status = status;
            Hint = hint;
        }

        /// <summary>
        /// Identificatore della view target (coerente con <c>QtoViewKey</c>):
        /// "Setup" | "Listino" | "Selection" | "Tagging" | "Verification" | "Export".
        /// </summary>
        public string Key { get; }

        /// <summary>Ordine 1..N (per display e precedenza di Current).</summary>
        public int Order { get; }

        /// <summary>Etichetta utente (es. "Setup progetto").</summary>
        public string Label { get; }

        /// <summary>Stato operativo dello step.</summary>
        public WorkflowStepStatus Status { get; }

        /// <summary>Dettaglio sintetico (es. "32/120 elementi taggati"), può essere vuoto.</summary>
        public string Hint { get; }
    }
}
