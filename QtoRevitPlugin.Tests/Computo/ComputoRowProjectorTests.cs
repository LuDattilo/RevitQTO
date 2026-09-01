using System.Collections.Generic;
using QtoRevitPlugin.Computo;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Models.Computi;
using Xunit;

namespace QtoRevitPlugin.Tests.Computo
{
    /// <summary>
    /// Test del proiettore MeasurementRow (+PriceItem) → righe del motore totali.
    /// Verifica la risoluzione prezzo/codice, il flag UnitPriceResolved sui prezzi orfani,
    /// e il passaggio degli override IVA per voce.
    /// </summary>
    public sealed class ComputoRowProjectorTests
    {
        private static PriceItem Pi(int id, string code, double price, string? tariffa = null) =>
            new PriceItem { Id = id, Code = code, Tariffa = tariffa, UnitPrice = price, Unit = "m2" };

        private static MeasurementRow Vc(int id, int priceItemId, double qta) =>
            new MeasurementRow { Id = id, DocumentId = 1, PriceItemId = priceItemId, Quantita = qta };

        [Fact]
        public void Project_ResolvesPriceAndCode_fromLinkedPriceItem()
        {
            var prices = new Dictionary<int, PriceItem> { [10] = Pi(10, "01.01", 25.0) };
            var rows = new[] { Vc(1, 10, 4.0) };

            var projected = ComputoRowProjector.Project(rows, prices);

            Assert.Single(projected);
            Assert.Equal("01.01", projected[0].Code);
            Assert.Equal(25.0, projected[0].UnitPrice);
            Assert.Equal(4.0, projected[0].Quantity);
            Assert.True(projected[0].UnitPriceResolved);
        }

        [Fact]
        public void Project_PrefersTariffa_overCode()
        {
            var prices = new Dictionary<int, PriceItem> { [10] = Pi(10, "INTERNAL", 25.0, tariffa: "A.01.005") };
            var projected = ComputoRowProjector.Project(new[] { Vc(1, 10, 1.0) }, prices);
            Assert.Equal("A.01.005", projected[0].Code);
        }

        [Fact]
        public void Project_OrphanPriceItem_marksUnitPriceUnresolved_priceZero()
        {
            var prices = new Dictionary<int, PriceItem>(); // FK non risolvibile
            var projected = ComputoRowProjector.Project(new[] { Vc(1, 999, 4.0) }, prices);

            Assert.False(projected[0].UnitPriceResolved);
            Assert.Equal(0.0, projected[0].UnitPrice);
            Assert.Equal(4.0, projected[0].Quantity);
        }

        [Fact]
        public void Project_EndToEnd_feedsTotalsEngineCorrectly()
        {
            var prices = new Dictionary<int, PriceItem>
            {
                [10] = Pi(10, "A", 100.0),
                [20] = Pi(20, "B", 50.0),
            };
            var rows = new[] { Vc(1, 10, 2.0), Vc(2, 20, 3.0) }; // 200 + 150 = 350

            var input = new ComputoTotalsInput { MarkupPercent = 10.0, DefaultVatPercent = 22.0 };
            input.Rows.AddRange(ComputoRowProjector.Project(rows, prices));
            var totals = ComputoTotals.Compute(input);

            Assert.Equal(350.0, totals.DirectCostTotal, 2);
            Assert.Equal(385.0, totals.PriceTotal, 2);            // +10%
            Assert.True(totals.VatComputable);
            Assert.Equal(84.70, totals.VatAmount, 2);             // 385 * 22%
            Assert.Equal(469.70, totals.GrandTotalWithVat, 2);
        }

        [Fact]
        public void Project_AppliesVatOverride_perRow()
        {
            var prices = new Dictionary<int, PriceItem> { [10] = Pi(10, "A", 100.0) };
            var overrides = new Dictionary<int, double> { [1] = 10.0 };
            var projected = ComputoRowProjector.Project(new[] { Vc(1, 10, 1.0) }, prices, overrides);
            Assert.Equal(10.0, projected[0].VatPercentOverride);
        }
    }
}
