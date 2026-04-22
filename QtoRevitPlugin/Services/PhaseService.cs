using Autodesk.Revit.DB;
using QtoRevitPlugin.Models;
using System.Collections.Generic;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Servizio di lettura fasi di progetto Revit (§I9 - PhaseFilterView).
    ///
    /// Lettura di <see cref="Document.Phases"/> è safe fuori dalla Transaction
    /// (property access, non mutating). Il count elementi usa FilteredElementCollector
    /// con <see cref="ElementPhaseStatusFilter"/>: filtro veloce (regola C7).
    /// </summary>
    public class PhaseService
    {
        private readonly Document _doc;

        public PhaseService(Document doc)
        {
            _doc = doc ?? throw new System.ArgumentNullException(nameof(doc));
        }

        /// <summary>
        /// Enumera tutte le fasi del documento in ordine cronologico (PhaseArray index = Sequence).
        /// NON calcola ElementCount (lazy — chiamare <see cref="CountComputableElementsInPhase"/>).
        /// </summary>
        public IReadOnlyList<PhaseInfo> GetAvailablePhases()
        {
            var result = new List<PhaseInfo>();
            var phases = _doc.Phases;
            for (int i = 0; i < phases.Size; i++)
            {
                var phase = phases.get_Item(i);
                result.Add(new PhaseInfo
                {
#if REVIT2025_OR_LATER
                    PhaseId = (int)phase.Id.Value,
#else
                    PhaseId = phase.Id.IntegerValue,
#endif
                    Name = phase.Name,
                    Sequence = i,
                    Description = phase.LookupParameter("Phase Description")?.AsString() ?? string.Empty
                });
            }
            return result;
        }

        /// <summary>
        /// Numero di elementi "computabili" in una fase specifica: status New + Existing
        /// (Demolished e Temporary esclusi dalla computazione metrica standard).
        /// Filtra ElementType out (veloce).
        /// </summary>
        public int CountComputableElementsInPhase(int phaseId)
        {
#if REVIT2025_OR_LATER
            var phaseElementId = new ElementId((long)phaseId);
#else
            var phaseElementId = new ElementId(phaseId);
#endif
            var filter = new ElementPhaseStatusFilter(
                phaseElementId,
                new List<ElementOnPhaseStatus>
                {
                    ElementOnPhaseStatus.New,
                    ElementOnPhaseStatus.Existing
                });

            return new FilteredElementCollector(_doc)
                .WhereElementIsNotElementType()   // filtro rapido (regola C7)
                .WherePasses(filter)              // filtro lento per ultimo
                .GetElementCount();
        }
    }
}
