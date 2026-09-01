using System;
using System.Collections.Generic;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models.Computi;

namespace QtoRevitPlugin.Services.Computi
{
    /// <summary>Un elemento da retro-popolare: la sotto-riga e l'ElementId Revit da rileggere.</summary>
    public sealed class CategoryBackfillTarget
    {
        public int SubRowId { get; set; }
        public int ElementId { get; set; }   // IDVV > 0
    }

    /// <summary>Il risultato risolto da Revit per una sotto-riga (categoria/famiglia dell'elemento).</summary>
    public sealed class CategoryBackfillResolution
    {
        public int SubRowId { get; set; }
        public string? Category { get; set; }
        public string? FamilyName { get; set; }
    }

    /// <summary>
    /// Backfill una-tantum di categoria/famiglia Revit sulle sotto-righe legacy (pre-v13, Category NULL),
    /// così il mismatch semantico AI di Health funziona anche sui computi creati prima della v13. La parte
    /// Revit-free vive qui (individua i candidati, applica le risoluzioni); la lettura effettiva
    /// dall'elemento Revit è fornita dal chiamante (layer plugin), che passa le risoluzioni ad
    /// <see cref="Apply"/>. Idempotente: una volta popolate, le righe non ricompaiono fra i candidati.
    /// </summary>
    public class ComputoCategoryBackfillService
    {
        private readonly IQtoRepository _repo;

        public ComputoCategoryBackfillService(IQtoRepository repo) =>
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));

        /// <summary>I candidati al backfill per la sessione (sotto-righe con IDVV&gt;0 e categoria mancante).</summary>
        public IReadOnlyList<CategoryBackfillTarget> GetPending(int sessionId)
        {
            var doc = _repo.GetComputoDocumentBySession(sessionId);
            if (doc == null) return Array.Empty<CategoryBackfillTarget>();

            var subs = _repo.GetSubRowsMissingCategory(doc.Id);
            var list = new List<CategoryBackfillTarget>(subs.Count);
            foreach (var s in subs)
                list.Add(new CategoryBackfillTarget { SubRowId = s.Id, ElementId = s.IDVV });
            return list;
        }

        /// <summary>
        /// Applica le risoluzioni lette da Revit. Salta quelle senza né categoria né famiglia (es. elemento
        /// non più presente nel modello): non sovrascrive con valori vuoti. Ritorna il numero di righe aggiornate.
        /// </summary>
        public int Apply(IEnumerable<CategoryBackfillResolution> resolutions)
        {
            if (resolutions == null) return 0;
            var applied = 0;
            foreach (var r in resolutions)
            {
                if (r == null) continue;
                if (string.IsNullOrWhiteSpace(r.Category) && string.IsNullOrWhiteSpace(r.FamilyName))
                    continue;   // niente da scrivere: non azzerare
                _repo.UpdateSubRowCategory(r.SubRowId, r.Category, r.FamilyName);
                applied++;
            }
            return applied;
        }
    }
}
