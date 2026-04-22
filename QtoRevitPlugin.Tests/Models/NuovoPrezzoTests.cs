using FluentAssertions;
using QtoRevitPlugin.Models;
using Xunit;

namespace QtoRevitPlugin.Tests.Models
{
    public class NuovoPrezzoTests
    {
        [Fact]
        public void UnitPrice_CalculatesCorrectly_WithoutRibasso()
        {
            // NP = CT × (1 + SG%) × (1 + Utile%)
            // CT = 100, SG = 15%, Utile = 10% → NP = 100 × 1.15 × 1.10 = 126.50
            var np = new NuovoPrezzo
            {
                Manodopera = 60,
                Materiali = 30,
                Noli = 5,
                Trasporti = 5,
                SpGenerali = 15,
                UtileImpresa = 10,
                RibassoAsta = 0
            };

            np.CostoTotale.Should().Be(100.0);
            np.UnitPrice.Should().BeApproximately(126.50, 0.01);
        }

        [Fact]
        public void UnitPrice_AppliesRibassoAsta()
        {
            // CT=100, SG=15%, Utile=10%, Ribasso=5% → NP = 126.50 × 0.95 = 120.175
            var np = new NuovoPrezzo
            {
                Manodopera = 100,
                SpGenerali = 15,
                UtileImpresa = 10,
                RibassoAsta = 5
            };

            np.UnitPrice.Should().BeApproximately(100 * 1.15 * 1.10 * 0.95, 0.001);
        }

        [Fact]
        public void Status_DefaultsToBozza()
        {
            var np = new NuovoPrezzo();
            np.Status.Should().Be(NpStatus.Bozza);
        }
    }
}
