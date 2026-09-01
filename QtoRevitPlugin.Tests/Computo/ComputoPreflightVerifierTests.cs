using System.Collections.Generic;
using System.Linq;
using QtoRevitPlugin.Computo.Preflight;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Models.Computi;
using Xunit;

namespace QtoRevitPlugin.Tests.Computo
{
    /// <summary>
    /// Test del Port #3: verifica pre-consegna (coerenza interna, percentuali, coerenza UM interna),
    /// con le classi non applicabili al modello CME dichiarate come "skipped".
    /// </summary>
    public sealed class ComputoPreflightVerifierTests
    {
        private static PriceItem Pi(int id, string code, double price, string unit = "m2") =>
            new PriceItem { Id = id, Code = code, Tariffa = code, UnitPrice = price, Unit = unit };

        private static MeasurementRow Vc(int id, int priceItemId, double qta) =>
            new MeasurementRow { Id = id, DocumentId = 1, PriceItemId = priceItemId, Quantita = qta };

        private static PreflightClassResult Cls(PreflightReport r, string name) =>
            r.Classes.Single(c => c.ClassName == name);

        [Fact]
        public void InternalConsistency_flagsMissingCodeAndNonPositiveQuantity()
        {
            var prices = new Dictionary<int, PriceItem> { [10] = Pi(10, "A", 100.0) };
            var rows = new List<MeasurementRow>
            {
                Vc(1, 10, 5.0),    // ok
                Vc(2, 999, 3.0),   // FK prezzo mancante -> voce_without_code
                Vc(3, 10, 0.0),    // quantità 0 -> non_positive_quantity
            };

            var report = ComputoPreflightVerifier.Verify(rows, prices);
            var ic = Cls(report, "internal_consistency");

            Assert.Contains(ic.Findings, f => f.Code == "voce_without_code");
            Assert.Contains(ic.Findings, f => f.Code == "non_positive_quantity");
        }

        [Fact]
        public void Percentages_skippedWhenNoneSet()
        {
            var report = ComputoPreflightVerifier.Verify(
                new List<MeasurementRow>(), new Dictionary<int, PriceItem>());
            var p = Cls(report, "percentages");
            Assert.Equal("skipped", p.Status);
            Assert.False(string.IsNullOrEmpty(p.SkipReason));
        }

        [Fact]
        public void Percentages_flagsOutOfBandMarkup_andNonStandardVat()
        {
            var prices = new Dictionary<int, PriceItem> { [10] = Pi(10, "A", 100.0) };
            var rows = new List<MeasurementRow> { Vc(1, 10, 1.0) };

            var report = ComputoPreflightVerifier.Verify(rows, prices, markupPercent: 40.0, defaultVatPercent: 15.0);
            var p = Cls(report, "percentages");

            Assert.Equal("checked", p.Status);
            Assert.Contains(p.Findings, f => f.Code == "markup_out_of_typical_band");
            Assert.Contains(p.Findings, f => f.Code == "vat_out_of_standard_set");
        }

        [Fact]
        public void Percentages_standardValues_noFindings()
        {
            var prices = new Dictionary<int, PriceItem> { [10] = Pi(10, "A", 100.0) };
            var rows = new List<MeasurementRow> { Vc(1, 10, 1.0) };
            var report = ComputoPreflightVerifier.Verify(rows, prices, markupPercent: 24.3, defaultVatPercent: 22.0);
            var p = Cls(report, "percentages");
            Assert.Empty(p.Findings);
        }

        [Fact]
        public void UnitConsistency_flagsSameCodeWithDifferentUnits()
        {
            var prices = new Dictionary<int, PriceItem>
            {
                [10] = Pi(10, "A", 100.0, unit: "m2"),
                [20] = Pi(20, "A", 100.0, unit: "m3"),   // stesso codice, UM diversa
            };
            var rows = new List<MeasurementRow> { Vc(1, 10, 1.0), Vc(2, 20, 1.0) };

            var report = ComputoPreflightVerifier.Verify(rows, prices);
            var uc = Cls(report, "unit_consistency");

            Assert.Contains(uc.Findings, f => f.Code == "unit_inconsistent" && f.Severity == PreflightSeverity.Error);
            Assert.Equal(1, report.ErrorCount);
        }

        [Fact]
        public void NotApplicableClasses_areDeclaredSkipped_notOmitted()
        {
            var report = ComputoPreflightVerifier.Verify(
                new List<MeasurementRow>(), new Dictionary<int, PriceItem>());
            foreach (var name in new[] { "completeness", "double_count", "unit_reconciliation" })
            {
                var c = Cls(report, name);
                Assert.Equal("skipped", c.Status);
                Assert.False(string.IsNullOrEmpty(c.SkipReason));
            }
        }
    }
}
