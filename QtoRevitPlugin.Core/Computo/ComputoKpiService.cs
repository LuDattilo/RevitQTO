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
    /// Ricalcola i KPI di <see cref="WorkSession"/> (elementi, voci, importo) dal modello Computi
    /// canonico (<see cref="MeasurementRow"/> + <see cref="MeasurementSubRow"/> + <see cref="PriceItem"/>),
    /// lo stesso binario su cui scrive <c>SelectionViewModel.ApplyEp</c>.
    ///
    /// Fase 0 della riconciliazione: prima i KPI di header/Home venivano aggiornati solo da
    /// <c>AssignmentService</c> (modello QtoAssignments), quindi restavano a zero quando le
    /// assegnazioni passavano dalla nuova SelectionView. Questo servizio chiude quel disallineamento.
    /// L'importo diretto è calcolato riusando il motore <see cref="ComputoTotals"/> (unica fonte
    /// aritmetica), non ricalcolato a mano.
    /// </summary>
    public class ComputoKpiService
    {
        private readonly IQtoRepository _repo;

        public ComputoKpiService(IQtoRepository repo) =>
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));

        /// <summary>
        /// Ricalcola e persiste i KPI della sessione dal suo <see cref="ComputoDocument"/>.
        /// No-op silenzioso se la sessione non esiste; se non c'è ancora un documento Computi, i KPI
        /// vengono azzerati (stato coerente "computo vuoto").
        /// </summary>
        public ComputoKpi RecomputeAndPersist(int sessionId)
        {
            var session = _repo.GetSession(sessionId);
            if (session == null) return ComputoKpi.Empty;

            var kpi = Compute(sessionId);

            session.TotalElements = kpi.DistinctElements;
            // TaggedElements resta alias di TotalElements finché non reintroduciamo il concetto
            // "selezionato ma non ancora taggato" (coerente con AssignmentService).
            session.TaggedElements = kpi.DistinctElements;
            session.TotalAmount = kpi.DirectAmount;
            _repo.UpdateSession(session);
            return kpi;
        }

        /// <summary>Calcola i KPI senza persisterli (per anteprime/diagnostica).</summary>
        public ComputoKpi Compute(int sessionId)
        {
            var doc = _repo.GetComputoDocumentBySession(sessionId);
            if (doc == null) return ComputoKpi.Empty;

            var rows = _repo.GetMeasurementRows(doc.Id);
            if (rows.Count == 0) return ComputoKpi.Empty;

            var priceIds = rows.Select(r => r.PriceItemId).Where(id => id > 0).Distinct().ToList();
            var priceItemsById = priceIds.Count == 0
                ? new Dictionary<int, PriceItem>()
                : _repo.GetPriceItems(priceIds).ToDictionary(p => p.Id, p => p);

            var subRowsByRowId = rows.ToDictionary(r => r.Id, r => _repo.GetMeasurementSubRows(r.Id));

            return ComputeKpi(rows, priceItemsById, subRowsByRowId);
        }

        /// <summary>
        /// Nucleo puro del calcolo (nessun accesso a repository), esposto per unit-test in-memory.
        /// </summary>
        /// <param name="subRowsByRowId">
        /// Sotto-righe (RGItem) per Id di voce. Servono per contare gli elementi Revit distinti
        /// (IDVV &gt; 0); un IDVV ≤ 0 è una misura manuale, non un elemento del modello.
        /// </param>
        public static ComputoKpi ComputeKpi(
            IReadOnlyList<MeasurementRow> rows,
            IReadOnlyDictionary<int, PriceItem> priceItemsById,
            IReadOnlyDictionary<int, IReadOnlyList<MeasurementSubRow>> subRowsByRowId)
        {
            if (rows == null || rows.Count == 0) return ComputoKpi.Empty;

            // Importo diretto tramite il motore totali (unica fonte aritmetica).
            var input = new ComputoTotalsInput();
            input.Rows.AddRange(ComputoRowProjector.Project(rows, priceItemsById));
            var totals = ComputoTotals.Compute(input);

            // Elementi Revit distinti (IDVV > 0) su tutte le sotto-righe.
            var distinctElements = new HashSet<int>();
            if (subRowsByRowId != null)
            {
                foreach (var row in rows)
                {
                    if (!subRowsByRowId.TryGetValue(row.Id, out var subs) || subs == null) continue;
                    foreach (var s in subs)
                        if (s.IDVV > 0) distinctElements.Add(s.IDVV);
                }
            }

            return new ComputoKpi
            {
                VociCount = rows.Count,
                DistinctElements = distinctElements.Count,
                DirectAmount = totals.DirectCostTotal,
                UnitPriceComputable = totals.UnitPriceComputable,
            };
        }
    }

    /// <summary>KPI di sintesi del computo (VCItem, elementi distinti, importo diretto).</summary>
    public sealed class ComputoKpi
    {
        public int VociCount { get; set; }
        public int DistinctElements { get; set; }
        public double DirectAmount { get; set; }

        /// <summary>False se almeno una voce ha prezzo non risolto (KPI importo incompleto).</summary>
        public bool UnitPriceComputable { get; set; } = true;

        public static ComputoKpi Empty => new ComputoKpi();
    }
}
