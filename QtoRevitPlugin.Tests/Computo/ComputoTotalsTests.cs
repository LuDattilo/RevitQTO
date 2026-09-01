using QtoRevitPlugin.Computo;
using Xunit;

namespace QtoRevitPlugin.Tests.Computo
{
    /// <summary>
    /// Test del motore totali a 4 livelli (portato dal modulo Computo di Pulse).
    /// Blocca il comportamento monetario: maggiorazione, IVA per riga (mai media),
    /// arrotondamento commerciale una sola volta, e le diagnostiche "valore assente ≠ zero".
    /// </summary>
    public sealed class ComputoTotalsTests
    {
        private static ComputoTotalsRow Row(long id, string code, double qty, double price,
            double? vat = null, bool priceResolved = true) =>
            new ComputoTotalsRow
            {
                ElementId = id,
                Code = code,
                Quantity = qty,
                UnitPrice = price,
                VatPercentOverride = vat,
                UnitPriceResolved = priceResolved,
            };

        private static ComputoTotalsInput Input(double? markup, double? defaultVat, params ComputoTotalsRow[] rows)
        {
            var input = new ComputoTotalsInput { MarkupPercent = markup, DefaultVatPercent = defaultVat };
            foreach (var r in rows) input.Rows.Add(r);
            return input;
        }

        [Fact]
        public void Markup15Percent_appliesToPriceTotal()
        {
            var r = ComputoTotals.Compute(Input(15.0, null, Row(1, "A", 1, 1000.0)));
            Assert.Equal(1000.0, r.DirectCostTotal, 2);
            Assert.Equal(150.0, r.MarkupAmount, 2);
            Assert.Equal(1150.0, r.PriceTotal, 2);
            Assert.Equal(1150.0, r.TaxableTotal, 2);
            Assert.True(r.ShowMarkupLine);
        }

        [Fact]
        public void MarkupNull_priceEqualsDirect_lineHidden()
        {
            var r = ComputoTotals.Compute(Input(null, null, Row(1, "A", 1, 1000.0)));
            Assert.Equal(r.DirectCostTotal, r.PriceTotal, 2);
            Assert.False(r.ShowMarkupLine);
            Assert.Equal(0.0, r.MarkupAmount, 2);
            Assert.Null(r.MarkupPercentApplied);
        }

        [Fact]
        public void MarkupExplicitZero_sameAsNull_lineHidden_butValueReadable()
        {
            var r = ComputoTotals.Compute(Input(0.0, null, Row(1, "A", 1, 1000.0)));
            Assert.Equal(r.DirectCostTotal, r.PriceTotal, 2);
            Assert.False(r.ShowMarkupLine);
            Assert.Equal(0.0, r.MarkupAmount, 2);
            Assert.Equal(0.0, r.MarkupPercentApplied);
        }

        [Theory]
        [InlineData(22.0, 22.0)]
        [InlineData(10.0, 10.0)]
        [InlineData(4.0, 4.0)]
        public void SingleRateComputo_producesCorrectVat(double rate, double expectedRate)
        {
            var r = ComputoTotals.Compute(Input(null, null, Row(1, "A", 1, 100.0, rate)));
            Assert.True(r.VatComputable);
            Assert.Equal(100.0 * expectedRate / 100.0, r.VatAmount, 2);
        }

        [Fact]
        public void MixedVatRates_sumsPerRow_neverAverages()
        {
            var input = Input(null, null,
                Row(1, "A", 1, 100.0, 10.0),
                Row(2, "B", 1, 100.0, 22.0));
            var r = ComputoTotals.Compute(input);
            Assert.True(r.VatComputable);
            Assert.Equal(10.0 + 22.0, r.VatAmount, 2);          // 100*0.10 + 100*0.22, MAI 200*0.16
        }

        [Fact]
        public void RowsMissingVatRate_listsExactlyTheUnresolvedRow()
        {
            var input = Input(null, 22.0,
                Row(1, "A", 1, 100.0),                 // usa il default 22
                Row(2, "B", 1, 100.0, 10.0),           // override 10
                Row(3, "C", 1, 100.0, null));          // niente override, ma il default C'È: risolvibile
            var r = ComputoTotals.Compute(input);
            Assert.True(r.VatComputable);
            Assert.Empty(r.RowsMissingVatRate);

            var input2 = Input(null, null,
                Row(1, "A", 1, 100.0),
                Row(2, "B", 1, 100.0, 10.0),
                Row(3, "C", 1, 100.0, null));
            var r2 = ComputoTotals.Compute(input2);
            Assert.False(r2.VatComputable);
            Assert.Equal(2, r2.RowsMissingVatRate.Count);
            Assert.Contains("A#1", r2.RowsMissingVatRate);
            Assert.Contains("C#3", r2.RowsMissingVatRate);
        }

        [Fact]
        public void RowsMissingUnitPrice_listsRowsWhosePriceWasNotResolved()
        {
            var r = ComputoTotals.Compute(Input(null, 22.0,
                Row(1, "A", 1, 100.0),
                Row(2, "B", 1, 0.0, priceResolved: false)));

            Assert.False(r.UnitPriceComputable);
            Assert.Equal(1, r.RowsMissingUnitPriceTotalCount);
            Assert.Contains("B#2", r.RowsMissingUnitPrice);
            Assert.False(r.RowsMissingUnitPriceTruncated);
            Assert.Equal(100.0, r.DirectCostTotal, 2);
        }

        [Fact]
        public void MarkupZero_withVatOverride_vatStillComputes()
        {
            var r = ComputoTotals.Compute(Input(0.0, null, Row(1, "A", 1, 100.0, 22.0)));
            Assert.False(r.ShowMarkupLine);
            Assert.True(r.VatComputable);
            Assert.Equal(22.0, r.VatAmount, 2);
        }

        [Fact]
        public void Rounding_sumsFirst_thenRoundsOnce()
        {
            var input = new ComputoTotalsInput();
            for (var i = 0; i < 3; i++)
                input.Rows.Add(Row(i + 1, "V", 1.0 / 3.0, 10.0));   // 3.333... ciascuna; arrotondata singolarmente 3.33*3=9.99
            var r = ComputoTotals.Compute(input);
            Assert.Equal(10.00, r.DirectCostTotal, 2);              // NON 9.99
        }

        [Fact]
        public void ShowMarkupLineFalse_doesNotHideMarkupFields()
        {
            var r = ComputoTotals.Compute(Input(null, null, Row(1, "A", 2, 50.0)));
            Assert.False(r.ShowMarkupLine);
            Assert.Equal(0.0, r.MarkupAmount, 2);   // leggibile, non omesso
        }

        [Fact]
        public void MissingRowsCap_truncatesButKeepsTrueCount()
        {
            var input = new ComputoTotalsInput();
            for (var i = 0; i < 5; i++)
                input.Rows.Add(Row(i + 1, "V" + i, 1, 10.0));   // nessuna aliquota, nessun default
            var r = ComputoTotals.Compute(input, missingRowsCap: 2);
            Assert.False(r.VatComputable);
            Assert.Equal(2, r.RowsMissingVatRate.Count);
            Assert.Equal(5, r.RowsMissingVatRateTotalCount);
            Assert.True(r.RowsMissingVatRateTruncated);

            var full = ComputoTotals.Compute(input, missingRowsCap: int.MaxValue);
            Assert.Equal(5, full.RowsMissingVatRate.Count);
            Assert.False(full.RowsMissingVatRateTruncated);
        }

        [Fact]
        public void NegativeZero_isNormalized_noSpuriousMinusSign()
        {
            // Riga "a detrarre" che azzera il diretto con residuo sub-cent sul lato negativo.
            var input = Input(null, 22.0,
                Row(1, "A", 1, 100.0),
                Row(2, "A", 1, -100.0));
            var r = ComputoTotals.Compute(input);
            Assert.Equal(0.0, r.DirectCostTotal, 2);
            Assert.False(double.IsNegative(r.DirectCostTotal));   // 0.0 positivo, non -0.0
        }
    }
}
