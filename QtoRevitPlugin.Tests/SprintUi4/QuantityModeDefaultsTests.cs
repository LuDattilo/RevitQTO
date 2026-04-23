using FluentAssertions;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Services;
using Xunit;

namespace QtoRevitPlugin.Tests.SprintUi4
{
    /// <summary>
    /// Test della mappa BuiltInCategory → QuantityMode (Sprint UI-5).
    /// Mantiene blindata la convenzione italiana di default: muri/pavimenti
    /// in m², strutture in m³, travi/impianti lineari in m, infissi a corpo.
    /// </summary>
    public class QuantityModeDefaultsTests
    {
        [Theory]
        [InlineData("OST_Walls", QuantityMode.Area)]
        [InlineData("OST_Floors", QuantityMode.Area)]
        [InlineData("OST_Ceilings", QuantityMode.Area)]
        [InlineData("OST_Roofs", QuantityMode.Area)]
        [InlineData("OST_Rooms", QuantityMode.Area)]
        [InlineData("OST_StructuralFraming", QuantityMode.Volume)]
        [InlineData("OST_StructuralColumns", QuantityMode.Volume)]
        [InlineData("OST_StructuralFoundation", QuantityMode.Volume)]
        [InlineData("OST_Railings", QuantityMode.Length)]
        [InlineData("OST_DuctCurves", QuantityMode.Length)]
        [InlineData("OST_PipeCurves", QuantityMode.Length)]
        [InlineData("OST_Rebar", QuantityMode.Length)]
        [InlineData("OST_Doors", QuantityMode.Count)]
        [InlineData("OST_Windows", QuantityMode.Count)]
        [InlineData("OST_MechanicalEquipment", QuantityMode.Count)]
        [InlineData("OST_GenericModel", QuantityMode.Count)]
        public void GetDefault_KnownCategory_ReturnsExpectedMode(string ost, QuantityMode expected)
        {
            QuantityModeDefaults.GetDefault(ost).Should().Be(expected);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("OST_NonEsisteQuestaCategoria")]
        public void GetDefault_UnknownOrEmpty_FallsBackToCount(string? ost)
        {
            QuantityModeDefaults.GetDefault(ost).Should().Be(QuantityMode.Count);
        }

        [Theory]
        [InlineData(QuantityMode.Area, "Area")]
        [InlineData(QuantityMode.Volume, "Volume")]
        [InlineData(QuantityMode.Length, "Length")]
        [InlineData(QuantityMode.Count, "Count")]
        public void ExtractorKey_MatchesQuantityExtractorConvention(QuantityMode mode, string expected)
        {
            // Chiave deve essere compatibile con QuantityExtractor.SupportedParams
            // (Area / Volume / Length / Count) per evitare decoupling tra UI e extractor.
            QuantityModeDefaults.ExtractorKey(mode).Should().Be(expected);
        }

        [Theory]
        [InlineData(QuantityMode.Area, "m²")]
        [InlineData(QuantityMode.Volume, "m³")]
        [InlineData(QuantityMode.Length, "m")]
        [InlineData(QuantityMode.Count, "cad.")]
        public void UnitAbbrev_ReturnsItalianConvention(QuantityMode mode, string expected)
        {
            QuantityModeDefaults.UnitAbbrev(mode).Should().Be(expected);
        }

        [Theory]
        [InlineData(QuantityMode.Area)]
        [InlineData(QuantityMode.Volume)]
        [InlineData(QuantityMode.Length)]
        [InlineData(QuantityMode.Count)]
        public void DisplayLabel_IsNotEmpty(QuantityMode mode)
        {
            // Contract: ogni mode ha label visibile per UI radio/combo.
            QuantityModeDefaults.DisplayLabel(mode).Should().NotBeNullOrWhiteSpace();
        }
    }
}
