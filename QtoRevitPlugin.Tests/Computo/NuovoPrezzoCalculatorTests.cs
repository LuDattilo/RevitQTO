using FluentAssertions;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Services;
using System;
using System.Linq;
using Xunit;

namespace QtoRevitPlugin.Tests.Computo
{
    /// <summary>
    /// Test formula analisi prezzi D.Lgs. 36/2023 All. II.14 (§I8).
    /// NP = CT × (1 + SG%) × (1 + Utile%) × (1 − Ribasso%)
    /// </summary>
    public class NuovoPrezzoCalculatorTests
    {
        [Fact]
        public void ComputeCostoTotale_SumsAllComponents()
        {
            NuovoPrezzoCalculator.ComputeCostoTotale(
                manodopera: 100, materiali: 50, noli: 30, trasporti: 20)
                .Should().Be(200);
        }

        [Fact]
        public void ComputeCostoTotale_NegativeComponent_Throws()
        {
            Action act = () => NuovoPrezzoCalculator.ComputeCostoTotale(-1, 0, 0, 0);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact]
        public void ComputeUnitPrice_ApplyFormula()
        {
            // CT = 100, SG = 15%, Utile = 10%, Ribasso = 0
            // NP = 100 × 1.15 × 1.10 × 1.00 = 126.50
            var np = NuovoPrezzoCalculator.ComputeUnitPrice(100, 15, 10, 0);
            np.Should().BeApproximately(126.50, 0.001);
        }

        [Fact]
        public void ComputeUnitPrice_WithRibasso20Percent_Reduces()
        {
            // 100 × 1.15 × 1.10 × 0.80 = 101.20
            var np = NuovoPrezzoCalculator.ComputeUnitPrice(100, 15, 10, 20);
            np.Should().BeApproximately(101.20, 0.001);
        }

        [Fact]
        public void ComputeUnitPrice_ZeroCostoTotale_ReturnsZero()
        {
            NuovoPrezzoCalculator.ComputeUnitPrice(0, 15, 10, 0).Should().Be(0);
        }

        [Fact]
        public void ComputeUnitPrice_NegativeCostoTotale_Throws()
        {
            Action act = () => NuovoPrezzoCalculator.ComputeUnitPrice(-1, 15, 10, 0);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(101)]
        public void ComputeUnitPrice_InvalidSpGenerali_Throws(double sg)
        {
            Action act = () => NuovoPrezzoCalculator.ComputeUnitPrice(100, sg, 10, 0);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact]
        public void ComputeUnitPrice_FromModel_MatchesLowLevel()
        {
            var np = new NuovoPrezzo
            {
                Manodopera = 40, Materiali = 30, Noli = 20, Trasporti = 10,
                SpGenerali = 15, UtileImpresa = 10, RibassoAsta = 0
            };
            var result = NuovoPrezzoCalculator.ComputeUnitPrice(np);
            result.Should().BeApproximately(126.50, 0.001);
        }

        // ─── Validation ───

        [Fact]
        public void Validate_ValidNp_ReturnsNoErrors()
        {
            var np = new NuovoPrezzo
            {
                Code = "NP.001",
                Description = "Trasporto a discarica autorizzata",
                Manodopera = 50, Materiali = 100, Noli = 30, Trasporti = 20,
                SpGenerali = 15, UtileImpresa = 10
            };
            NuovoPrezzoCalculator.Validate(np).Should().BeEmpty();
            NuovoPrezzoCalculator.IsValid(np).Should().BeTrue();
        }

        [Fact]
        public void Validate_MissingCode_ReturnsError()
        {
            var np = new NuovoPrezzo
            {
                Description = "Test", Manodopera = 10
            };
            var errors = NuovoPrezzoCalculator.Validate(np);
            errors.Should().Contain(e => e.Contains("codice"));
        }

        [Fact]
        public void Validate_MissingDescription_ReturnsError()
        {
            var np = new NuovoPrezzo { Code = "NP.001", Manodopera = 10 };
            var errors = NuovoPrezzoCalculator.Validate(np);
            errors.Should().Contain(e => e.Contains("descrizione"));
        }

        [Fact]
        public void Validate_ZeroCost_ReturnsError()
        {
            var np = new NuovoPrezzo
            {
                Code = "NP.001", Description = "Test"
                // Tutte le componenti 0
            };
            var errors = NuovoPrezzoCalculator.Validate(np);
            errors.Should().Contain(e => e.Contains("Il costo totale è zero"));
        }

        [Theory]
        [InlineData(12)]  // sotto MinSpGenerali
        [InlineData(18)]  // sopra MaxSpGenerali
        public void Validate_SpGeneraliOutOfNormativeRange_ReturnsError(double sg)
        {
            var np = new NuovoPrezzo
            {
                Code = "NP.001", Description = "Test",
                Manodopera = 10, SpGenerali = sg, UtileImpresa = 10
            };
            var errors = NuovoPrezzoCalculator.Validate(np);
            errors.Should().Contain(e => e.Contains("Spese generali"));
        }

        [Fact]
        public void Validate_SpGenerali13Percent_IsValid()
        {
            var np = new NuovoPrezzo
            {
                Code = "NP.001", Description = "Test",
                Manodopera = 10, SpGenerali = 13, UtileImpresa = 10
            };
            NuovoPrezzoCalculator.IsValid(np).Should().BeTrue();
        }

        [Fact]
        public void Validate_SpGenerali17Percent_IsValid()
        {
            var np = new NuovoPrezzo
            {
                Code = "NP.001", Description = "Test",
                Manodopera = 10, SpGenerali = 17, UtileImpresa = 10
            };
            NuovoPrezzoCalculator.IsValid(np).Should().BeTrue();
        }

        [Fact]
        public void Constants_MatchNormative()
        {
            NuovoPrezzoCalculator.MinSpGenerali.Should().Be(13.0);
            NuovoPrezzoCalculator.MaxSpGenerali.Should().Be(17.0);
            NuovoPrezzoCalculator.DefaultUtileImpresa.Should().Be(10.0);
        }
    }
}
