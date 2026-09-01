using System.Collections.Generic;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Models.Computi;
using QtoRevitPlugin.Services.Computi;
using Xunit;

namespace QtoRevitPlugin.Tests.Computo
{
    /// <summary>
    /// Test del nucleo puro di ricalcolo KPI dal modello Computi: importo diretto via motore totali,
    /// conteggio elementi Revit distinti (solo IDVV &gt; 0), conteggio voci.
    /// </summary>
    public sealed class ComputoKpiServiceTests
    {
        private static PriceItem Pi(int id, double price) =>
            new PriceItem { Id = id, Code = "C" + id, UnitPrice = price, Unit = "m2" };

        private static MeasurementRow Vc(int id, int priceItemId, double qta) =>
            new MeasurementRow { Id = id, DocumentId = 1, PriceItemId = priceItemId, Quantita = qta };

        private static MeasurementSubRow Rg(int id, int rowId, int idvv, double qta) =>
            new MeasurementSubRow { Id = id, MeasurementRowId = rowId, IDVV = idvv, Quantita = qta, PartiUguali = qta };

        [Fact]
        public void ComputeKpi_sumsDirectAmount_andCountsVoci()
        {
            var rows = new List<MeasurementRow> { Vc(1, 10, 2.0), Vc(2, 20, 4.0) };
            var prices = new Dictionary<int, PriceItem> { [10] = Pi(10, 100.0), [20] = Pi(20, 50.0) };
            var subs = new Dictionary<int, IReadOnlyList<MeasurementSubRow>>
            {
                [1] = new List<MeasurementSubRow> { Rg(1, 1, 111, 2.0) },
                [2] = new List<MeasurementSubRow> { Rg(2, 2, 222, 4.0) },
            };

            var kpi = ComputoKpiService.ComputeKpi(rows, prices, subs);

            Assert.Equal(2, kpi.VociCount);
            Assert.Equal(2, kpi.DistinctElements);
            Assert.Equal(400.0, kpi.DirectAmount, 2);   // 2*100 + 4*50
            Assert.True(kpi.UnitPriceComputable);
        }

        [Fact]
        public void ComputeKpi_countsDistinctElements_ignoringManualNegativeIdvv()
        {
            var rows = new List<MeasurementRow> { Vc(1, 10, 1.0), Vc(2, 10, 1.0) };
            var prices = new Dictionary<int, PriceItem> { [10] = Pi(10, 10.0) };
            var subs = new Dictionary<int, IReadOnlyList<MeasurementSubRow>>
            {
                // stesso elemento 500 su due voci diverse -> conta 1 volta; -1 e -2 sono manuali
                [1] = new List<MeasurementSubRow> { Rg(1, 1, 500, 1.0), Rg(2, 1, -1, 1.0) },
                [2] = new List<MeasurementSubRow> { Rg(3, 2, 500, 1.0), Rg(4, 2, -2, 1.0) },
            };

            var kpi = ComputoKpiService.ComputeKpi(rows, prices, subs);

            Assert.Equal(1, kpi.DistinctElements);   // solo l'elemento 500
            Assert.Equal(2, kpi.VociCount);
        }

        [Fact]
        public void ComputeKpi_orphanPrice_flagsUnitPriceNotComputable()
        {
            var rows = new List<MeasurementRow> { Vc(1, 999, 3.0) };
            var kpi = ComputoKpiService.ComputeKpi(
                rows,
                new Dictionary<int, PriceItem>(),
                new Dictionary<int, IReadOnlyList<MeasurementSubRow>>());

            Assert.False(kpi.UnitPriceComputable);
            Assert.Equal(0.0, kpi.DirectAmount, 2);
            Assert.Equal(0, kpi.DistinctElements);
        }

        [Fact]
        public void ComputeKpi_emptyRows_returnsEmpty()
        {
            var kpi = ComputoKpiService.ComputeKpi(
                new List<MeasurementRow>(),
                new Dictionary<int, PriceItem>(),
                new Dictionary<int, IReadOnlyList<MeasurementSubRow>>());
            Assert.Equal(0, kpi.VociCount);
            Assert.Equal(0.0, kpi.DirectAmount, 2);
        }
    }
}
