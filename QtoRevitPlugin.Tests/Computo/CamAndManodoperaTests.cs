using System.Collections.Generic;
using QtoRevitPlugin.Computo;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Models.Computi;
using Xunit;

namespace QtoRevitPlugin.Tests.Computo
{
    /// <summary>
    /// Test del Port #2: classificatore codici prezzario, quota CAM, incidenza manodopera.
    /// </summary>
    public sealed class CamAndManodoperaTests
    {
        private static PriceItem Pi(int id, string code, double price, double incMdo = 0, string? tariffa = null) =>
            new PriceItem
            {
                Id = id,
                Code = code,
                Tariffa = tariffa ?? code,
                UnitPrice = price,
                IncMDO = incMdo,
                Unit = "m2",
                Description = "desc " + code,
                ShortDesc = "d" + code,
            };

        private static MeasurementRow Vc(int id, int priceItemId, double qta) =>
            new MeasurementRow { Id = id, DocumentId = 1, PriceItemId = priceItemId, Quantita = qta };

        // ---------- PricelistCodeClassifier ----------

        [Theory]
        [InlineData("TOS26_02CAM.B07.005.002", "02CAM", true)]
        [InlineData("TOS26_01.A01.001", "01", false)]
        public void Classifier_extractsChapter_andCamFlag(string code, string expectedChapter, bool expectedCam)
        {
            PricelistCodeClassifier.Classify(code, out var chapter, out var cam);
            Assert.Equal(expectedChapter, chapter);
            Assert.Equal(expectedCam, cam);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("01.01")]        // nessun underscore
        [InlineData("PREZZO_NODOT")] // underscore ma nessun punto dopo
        public void Classifier_unrecognizedShape_returnsNull_notGuessed(string? code)
        {
            PricelistCodeClassifier.Classify(code, out var chapter, out var cam);
            Assert.Null(chapter);
            Assert.Null(cam);
        }

        // ---------- CamQuotaCalculator ----------

        [Fact]
        public void Cam_bucketsAmounts_andComputesQuota()
        {
            var prices = new Dictionary<int, PriceItem>
            {
                [10] = Pi(10, "TOS26_02CAM.B07.005", 100.0),  // CAM
                [20] = Pi(20, "TOS26_01.A01.001", 50.0),      // non-CAM
                [30] = Pi(30, "01.01", 25.0),                 // non classificabile
            };
            var rows = new List<MeasurementRow> { Vc(1, 10, 2.0), Vc(2, 20, 2.0), Vc(3, 30, 4.0) };

            var r = CamQuotaCalculator.Compute(rows, prices);

            Assert.Equal(200.0, r.CamImporto, 2);
            Assert.Equal(100.0, r.NonCamImporto, 2);
            Assert.Equal(100.0, r.UnclassifiedImporto, 2);
            Assert.Equal(400.0, r.TotalImporto, 2);
            Assert.Equal(0.5, r.QuotaCamSuTotale!.Value, 3);   // 200/400
            Assert.Contains("01.01", r.UnclassifiedCodes);
        }

        [Fact]
        public void Cam_orphanPriceItem_excludedFromTotals_reported()
        {
            var r = CamQuotaCalculator.Compute(
                new List<MeasurementRow> { Vc(1, 999, 5.0) },
                new Dictionary<int, PriceItem>());
            Assert.Equal(0.0, r.TotalImporto, 2);
            Assert.Null(r.QuotaCamSuTotale);   // non calcolabile, mai 0
            Assert.Single(r.OrphanMeasureCodes);
        }

        // ---------- ManodoperaAggregator ----------

        [Fact]
        public void Manodopera_computesIncidence_andCoverage()
        {
            var prices = new Dictionary<int, PriceItem>
            {
                [10] = Pi(10, "A", 100.0, incMdo: 30.0),  // 200 * 30% = 60
                [20] = Pi(20, "B", 50.0, incMdo: 0),      // senza MDO
            };
            var rows = new List<MeasurementRow> { Vc(1, 10, 2.0), Vc(2, 20, 4.0) };

            var r = ManodoperaAggregator.Aggregate(rows, prices, "riga");

            Assert.Equal(400.0, r.Totals.ImportoTotaleComputo, 2);       // 200 + 200
            Assert.Equal(60.0, r.Totals.IncidenzaManodoperaTotale, 2);   // solo A
            Assert.Equal(15.0, r.Totals.IncidenzaManodoperaPercentSulTotale!.Value, 2);  // 60/400
            Assert.Equal(2, r.Coverage.VociTotali);
            Assert.Equal(1, r.Coverage.VociConMdoNota);
            Assert.Equal(1, r.Coverage.VociSenzaMdo);
            Assert.Contains("B", r.Coverage.CodesSenzaMdo);
        }

        [Fact]
        public void Manodopera_groupByCodice_mergesSameTariffa()
        {
            var prices = new Dictionary<int, PriceItem> { [10] = Pi(10, "A", 100.0, incMdo: 20.0) };
            var rows = new List<MeasurementRow> { Vc(1, 10, 1.0), Vc(2, 10, 3.0) };

            var byCodice = ManodoperaAggregator.Aggregate(rows, prices, "codice");
            Assert.Single(byCodice.Righe);                        // fuse in una riga per codice A
            Assert.Equal(4.0, byCodice.Righe[0].Quantity, 2);
            Assert.Equal(400.0, byCodice.Righe[0].Importo!.Value, 2);

            var byRiga = ManodoperaAggregator.Aggregate(rows, prices, "riga");
            Assert.Equal(2, byRiga.Righe.Count);                  // due righe distinte
            // Il totale documento coincide fra le due modalità
            Assert.Equal(byCodice.Totals.IncidenzaManodoperaTotale,
                         byRiga.Totals.IncidenzaManodoperaTotale, 2);
        }

        [Fact]
        public void Manodopera_orphanPrice_yieldsUnresolvedFinding()
        {
            var r = ManodoperaAggregator.Aggregate(
                new List<MeasurementRow> { Vc(1, 999, 1.0) },
                new Dictionary<int, PriceItem>(), "riga");
            Assert.Contains(r.Findings, f => f.Code == "idep_unresolved");
            Assert.Equal(0.0, r.Totals.ImportoTotaleComputo, 2);
        }
    }
}
