using System.Collections.Generic;
using System.Linq;
using QtoRevitPlugin.Computo.Extraction;
using Xunit;

namespace QtoRevitPlugin.Tests.Computo
{
    /// <summary>
    /// Test del Port #4: filtro fase, esplosione strati, voci derivate. Nuclei puri (Revit-free).
    /// </summary>
    public sealed class ExtractionEnginesTests
    {
        // ---------- ComputoPhaseFilter ----------

        [Theory]
        [InlineData("demolito", "demolished")]
        [InlineData("Nuove", "new")]
        [InlineData("provvisionale", "temporary")]
        [InlineData("esistente", "existing")]
        public void PhaseFilter_normalizesItalianAliases(string raw, string expected)
        {
            Assert.Equal(expected, ComputoPhaseFilter.NormalizeStatus(raw));
        }

        [Fact]
        public void PhaseFilter_unrecognizedStatus_reported_notDropped()
        {
            Assert.Null(ComputoPhaseFilter.NormalizeStatus("pippo"));
            var bad = ComputoPhaseFilter.UnrecognisedStatuses(new[] { "nuovo", "pippo" }).ToList();
            Assert.Single(bad);
            Assert.Contains("pippo", bad);
        }

        [Fact]
        public void PhaseFilter_matches_respectsFilter_andEmptyMeansAll()
        {
            Assert.True(ComputoPhaseFilter.Matches("demolito", null));                 // nessun filtro = tutto
            Assert.True(ComputoPhaseFilter.Matches("demolito", new[] { "demolished" }));
            Assert.False(ComputoPhaseFilter.Matches("nuovo", new[] { "demolished" }));
            Assert.Equal("Demolizioni", ComputoPhaseFilter.GroupLabel("demolito"));
        }

        // ---------- LayerComputoExploder ----------

        [Fact]
        public void Exploder_noCodedLayers_usesDirect_andReportsUncoded()
        {
            var layers = new List<LayerInput>
            {
                new LayerInput { MaterialName = "Intonaco", WidthMm = 15 },   // nessun codice
            };
            var r = LayerComputoExploder.Explode(10.0, layers);
            Assert.True(r.UseDirect);
            Assert.Empty(r.Contributions);
            Assert.Contains("Intonaco", r.UncodedMaterials);
        }

        [Fact]
        public void Exploder_volumeLayer_areaTimesWidth_notMaterialArea()
        {
            var layers = new List<LayerInput>
            {
                new LayerInput { Code = "CLS", Um = "mc", WidthMm = 200, MaterialName = "Calcestruzzo" },
            };
            var r = LayerComputoExploder.Explode(10.0, layers);   // 10 m2 * 0.2 m = 2 mc
            Assert.False(r.UseDirect);
            Assert.Single(r.Contributions);
            Assert.Equal(2.0, r.Contributions[0].Quantity, 3);
            Assert.True(r.Contributions[0].Computed);
        }

        [Fact]
        public void Exploder_weightLayer_withoutDensity_isFlagged_notBlocked()
        {
            var layers = new List<LayerInput>
            {
                new LayerInput { Code = "ACC", Um = "kg", WidthMm = 10, MaterialName = "Acciaio", Density = null },
            };
            var r = LayerComputoExploder.Explode(5.0, layers);
            Assert.Single(r.Contributions);
            Assert.False(r.Contributions[0].Computed);         // emessa ma da completare
            Assert.Equal(0.0, r.Contributions[0].Quantity, 3);
            Assert.Contains("densità", r.Contributions[0].Note);
        }

        [Fact]
        public void Exploder_membraneZeroWidth_excludedFromVolume_keptForArea()
        {
            var layers = new List<LayerInput>
            {
                new LayerInput { Code = "MEM_MC", Um = "mc", WidthMm = 0, MaterialName = "Membrana" }, // esclusa
                new LayerInput { Code = "MEM_MQ", Um = "mq", WidthMm = 0, MaterialName = "Membrana" }, // tenuta
            };
            var r = LayerComputoExploder.Explode(8.0, layers);
            Assert.Single(r.Contributions);
            Assert.Equal("MEM_MQ", r.Contributions[0].Code);
            Assert.Equal(8.0, r.Contributions[0].Quantity, 3);
        }

        // ---------- DerivedComputoDeriver ----------

        [Fact]
        public void Deriver_addsVoce_onVolumeBase_withCoefficient()
        {
            var rules = new List<DerivedRule>
            {
                new DerivedRule { Code = "ARM", Um = "kg", Base = DerivedBase.Volume, Coefficient = 80.0, MaterialName = "Armatura" },
            };
            var r = DerivedComputoDeriver.Derive(baseVolumeM3: 2.0, baseAreaM2: null, rules, new HashSet<string>());
            Assert.Single(r);
            Assert.True(r[0].Computed);
            Assert.Equal(160.0, r[0].Quantity, 3);   // 2 mc * 80 kg/mc
        }

        [Fact]
        public void Deriver_antiDoubleGate_suppresses_whenCategoryModelled()
        {
            var rules = new List<DerivedRule>
            {
                new DerivedRule { Code = "ARM", Um = "kg", Base = DerivedBase.Volume, Coefficient = 80.0,
                    AntiDoubleCategory = "OST_Rebar", AntiDoubleLabel = "armatura" },
            };
            var scope = new HashSet<string> { "OST_Rebar" };
            var r = DerivedComputoDeriver.Derive(2.0, null, rules, scope);
            Assert.Single(r);
            Assert.True(r[0].Suppressed);
            Assert.False(r[0].Computed);
            Assert.Equal(0.0, r[0].Quantity, 3);
            Assert.Contains("due volte", r[0].Note);
        }

        [Fact]
        public void Deriver_missingCoefficient_flagged_notInvented()
        {
            var rules = new List<DerivedRule>
            {
                new DerivedRule { Code = "ARM", Um = "kg", Base = DerivedBase.Volume, Coefficient = null },
            };
            var r = DerivedComputoDeriver.Derive(2.0, null, rules, new HashSet<string>());
            Assert.Single(r);
            Assert.False(r[0].Computed);
            Assert.False(r[0].Suppressed);   // NON soppressa: da completare a mano
            Assert.Contains("incidenza mancante", r[0].Note);
        }

        [Fact]
        public void Deriver_missingBase_flagged_notSilentZero()
        {
            var rules = new List<DerivedRule>
            {
                new DerivedRule { Code = "ARM", Um = "kg", Base = DerivedBase.Volume, Coefficient = 80.0 },
            };
            var r = DerivedComputoDeriver.Derive(baseVolumeM3: null, baseAreaM2: null, rules, new HashSet<string>());
            Assert.Single(r);
            Assert.False(r[0].Computed);
            Assert.Contains("assente", r[0].Note);
        }
    }
}
