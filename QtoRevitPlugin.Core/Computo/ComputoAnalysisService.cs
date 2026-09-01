using System;
using System.Collections.Generic;
using System.Linq;
using QtoRevitPlugin.Computo;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Models.Computi;

namespace QtoRevitPlugin.Services.Computi
{
    /// <summary>
    /// Facciata di sola lettura sopra i calcolatori di dominio del computo (Port #1/#2 da Pulse):
    /// totali a 4 livelli, quota CAM, incidenza manodopera. Materializza le voci e i prezzi dal
    /// <see cref="ComputoDocument"/> della sessione una sola volta e li passa ai motori puri, così la
    /// UI WPF li invoca per <c>sessionId</c> con un'unica chiamata e resta sottile.
    /// </summary>
    public class ComputoAnalysisService
    {
        private readonly IQtoRepository _repo;

        public ComputoAnalysisService(IQtoRepository repo) =>
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));

        /// <summary>Totali a 4 livelli. <paramref name="markupPercent"/>/<paramref name="defaultVatPercent"/> null = non applicati.</summary>
        public ComputoTotalsResult GetTotals(int sessionId, double? markupPercent = null, double? defaultVatPercent = null)
        {
            var (rows, prices) = Load(sessionId);
            var input = new ComputoTotalsInput { MarkupPercent = markupPercent, DefaultVatPercent = defaultVatPercent };
            input.Rows.AddRange(ComputoRowProjector.Project(rows, prices));
            return ComputoTotals.Compute(input);
        }

        /// <summary>Quota CAM (Criteri Ambientali Minimi) del computo.</summary>
        public CamQuotaResult GetCam(int sessionId)
        {
            var (rows, prices) = Load(sessionId);
            return CamQuotaCalculator.Compute(rows, prices);
        }

        /// <summary>Incidenza manodopera (art. 41 D.Lgs 36/2023). <paramref name="groupBy"/> = "riga" o "codice".</summary>
        public ManodoperaResult GetManodopera(int sessionId, string groupBy = "riga")
        {
            var (rows, prices) = Load(sessionId);
            return ManodoperaAggregator.Aggregate(rows, prices, groupBy);
        }

        private (IReadOnlyList<MeasurementRow> rows, IReadOnlyDictionary<int, PriceItem> prices) Load(int sessionId)
        {
            var doc = _repo.GetComputoDocumentBySession(sessionId);
            if (doc == null)
                return (Array.Empty<MeasurementRow>(), new Dictionary<int, PriceItem>());

            var rows = _repo.GetMeasurementRows(doc.Id);
            var priceIds = rows.Select(r => r.PriceItemId).Where(id => id > 0).Distinct().ToList();
            var prices = priceIds.Count == 0
                ? new Dictionary<int, PriceItem>()
                : _repo.GetPriceItems(priceIds).ToDictionary(p => p.Id, p => p);
            return (rows, prices);
        }
    }
}
