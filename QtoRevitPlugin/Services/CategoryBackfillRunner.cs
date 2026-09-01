using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Services.Computi;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Orchestratore Revit del backfill una-tantum di categoria/famiglia (v13): per ogni sotto-riga
    /// legacy priva di categoria, rilegge l'elemento Revit tramite IDVV, ne deriva categoria/famiglia
    /// (stessa logica di <see cref="SelectionService"/>) e applica via
    /// <see cref="ComputoCategoryBackfillService"/>. Read-only sul modello Revit; scrive solo sul .cme.
    /// Idempotente: le righe già popolate non ricompaiono fra i candidati.
    /// </summary>
    public static class CategoryBackfillRunner
    {
        /// <summary>Esegue il backfill per la sessione. Ritorna il numero di sotto-righe aggiornate.</summary>
        public static int Run(Document doc, IQtoRepository repo, int sessionId, Action<string>? log = null)
        {
            if (doc == null || repo == null) return 0;

            var svc = new ComputoCategoryBackfillService(repo);
            var pending = svc.GetPending(sessionId);
            if (pending.Count == 0) return 0;

            var resolutions = new List<CategoryBackfillResolution>(pending.Count);
            foreach (var t in pending)
            {
#if REVIT2025_OR_LATER
                var el = doc.GetElement(new ElementId((long)t.ElementId));
#else
                var el = doc.GetElement(new ElementId(t.ElementId));
#endif
                if (el == null)
                {
                    // Elemento non più nel modello: nessun valore, saltato da Apply (non azzera).
                    resolutions.Add(new CategoryBackfillResolution { SubRowId = t.SubRowId });
                    continue;
                }

                resolutions.Add(new CategoryBackfillResolution
                {
                    SubRowId = t.SubRowId,
                    Category = el.Category?.Name,
                    FamilyName = DeriveFamilyName(el, doc),
                });
            }

            var applied = svc.Apply(resolutions);
            log?.Invoke($"CategoryBackfill: {applied}/{pending.Count} sotto-righe aggiornate.");
            return applied;
        }

        /// <summary>Nome famiglia: da FamilyInstance.Symbol, altrimenti dall'ElementType (system family).</summary>
        private static string DeriveFamilyName(Element el, Document doc)
        {
            if (el is FamilyInstance fi)
                return fi.Symbol?.FamilyName ?? "";

            var typeId = el.GetTypeId();
#if REVIT2025_OR_LATER
            if (typeId != null && typeId.Value != ElementId.InvalidElementId.Value)
#else
            if (typeId != null && typeId.IntegerValue != ElementId.InvalidElementId.IntegerValue)
#endif
            {
                return (doc.GetElement(typeId) as ElementType)?.FamilyName ?? "";
            }
            return "";
        }
    }
}
