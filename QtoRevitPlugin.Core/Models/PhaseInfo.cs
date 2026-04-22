namespace QtoRevitPlugin.Models
{
    /// <summary>
    /// Proiezione pure-C# di una Phase Revit (per-step workflow §I9).
    /// Usata dal PhaseFilterView per mostrare all'utente le fasi disponibili del .rvt
    /// senza dipendenza Revit API nel Core.
    /// </summary>
    public class PhaseInfo
    {
        /// <summary>Valore intero dell'ElementId della Phase (stabile per sessione Revit).</summary>
        public int PhaseId { get; set; }

        /// <summary>Nome visibile (localizzato) della fase, es. "Nuova costruzione", "Demolizione".</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Sequence number assegnato da Revit (0 = prima fase cronologica del progetto).
        /// Utile per ordinare nella UI.
        /// </summary>
        public int Sequence { get; set; }

        /// <summary>
        /// Conteggio elementi "computabili" (non ElementType, non demoliti/unchanged)
        /// rilevati dalla fase. Null se non ancora calcolato (calcolo lazy on-demand).
        /// </summary>
        public int? ElementCount { get; set; }

        /// <summary>Descrizione opzionale (raramente popolata in progetti standard).</summary>
        public string Description { get; set; } = string.Empty;

        public override string ToString() => string.IsNullOrEmpty(Name) ? $"[Phase {PhaseId}]" : Name;
    }
}
