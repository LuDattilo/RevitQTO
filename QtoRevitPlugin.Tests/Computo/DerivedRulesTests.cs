using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using QtoRevitPlugin.Computo.Extraction;
using QtoRevitPlugin.Services;
using Xunit;

namespace QtoRevitPlugin.Tests.Computo
{
    /// <summary>
    /// Test del wiring delle voci derivate: config JSON (round-trip + posizione), RulesFor,
    /// mapper template→regola, e integrazione con DerivedComputoDeriver.
    /// </summary>
    public sealed class DerivedRulesTests
    {
        private static string UniqueDir()
        {
            var d = Path.Combine(Path.GetTempPath(), $"dr_{Guid.NewGuid():N}");
            Directory.CreateDirectory(d);
            return d;
        }

        [Fact]
        public void Service_defaultIsEmpty_noDerivedUntilConfigured()
        {
            var dir = UniqueDir();
            try
            {
                var svc = new DerivedRulesService(globalDir: dir);
                svc.LoadEffective().Rules.Should().BeEmpty();   // H7: nessuna derivata di default
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Service_saveThenLoad_roundTrips()
        {
            var dir = UniqueDir();
            try
            {
                var svc = new DerivedRulesService(globalDir: dir);
                svc.SaveGlobal(DerivedRulesService.ExampleConfig());

                var loaded = new DerivedRulesService(globalDir: dir).LoadEffective();
                loaded.Rules.Should().HaveCount(2);
                loaded.Rules[0].Code.Should().Be("ARM.01");
                loaded.Rules[0].ResolveBase().Should().Be(DerivedBase.Volume);
                loaded.Rules[1].ResolveBase().Should().Be(DerivedBase.Area);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void RulesFor_matchesCategory_andTypeFilter()
        {
            var cfg = new DerivedRulesConfig
            {
                Rules = new List<DerivedRuleTemplate>
                {
                    new DerivedRuleTemplate { BindCategory = "Pilastri strutturali", Code = "A" },
                    new DerivedRuleTemplate { BindCategory = "Muri", BindType = "Muro 30", Code = "B" },
                    new DerivedRuleTemplate { BindCategory = "*", Code = "C" },
                }
            };

            cfg.RulesFor("Pilastri strutturali", "P30").Select(r => r.Code)
               .Should().BeEquivalentTo(new[] { "A", "C" });      // categoria + wildcard
            cfg.RulesFor("Muri", "Muro 30").Select(r => r.Code)
               .Should().BeEquivalentTo(new[] { "B", "C" });      // type match + wildcard
            cfg.RulesFor("Muri", "Muro 20").Select(r => r.Code)
               .Should().BeEquivalentTo(new[] { "C" });           // type NON combacia -> solo wildcard
        }

        [Fact]
        public void Mapper_thenDeriver_producesDerivedContribution()
        {
            var t = new DerivedRuleTemplate
            {
                Code = "ARM.01", Um = "kg", BaseMeasure = "volume",
                AntiDoubleCategory = "OST_Rebar", AntiDoubleLabel = "armatura",
            };
            var rule = DerivedRuleMapper.ToRule(t, resolvedCoefficient: 80.0);

            // Nessuna armatura modellata -> deriva 2 mc * 80 = 160 kg
            var ok = DerivedComputoDeriver.Derive(2.0, null, new[] { rule }, new HashSet<string>());
            ok.Should().ContainSingle();
            ok[0].Computed.Should().BeTrue();
            ok[0].Quantity.Should().BeApproximately(160.0, 0.001);

            // Armatura modellata in scope -> soppressa (gate anti-doppio)
            var gated = DerivedComputoDeriver.Derive(2.0, null, new[] { rule }, new HashSet<string> { "OST_Rebar" });
            gated[0].Suppressed.Should().BeTrue();
        }
    }
}
