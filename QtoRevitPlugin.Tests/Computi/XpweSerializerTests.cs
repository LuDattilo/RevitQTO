using System.IO;
using FluentAssertions;
using QtoRevitPlugin.Xpwe;
using Xunit;

namespace QtoRevitPlugin.Tests.Computi
{
    /// <summary>
    /// Plan C-7: test simmetrici a XpweDeserializerTests.
    /// Verifica roundtrip parse → serialize → parse preservando entity counts.
    /// </summary>
    public class XpweSerializerTests
    {
        private static readonly string RepoRoot =
            Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(typeof(XpweSerializerTests).Assembly.Location)!,
                "..", "..", "..", ".."));

        private static string TestFile(string name) => Path.Combine(RepoRoot, name);

        private const string PrimusPI = "<?mso-application progid=\"PriMus.Document.XPWE\"?>";

        [Fact]
        public void Roundtrip_TestXpwe_PreservesCounts()
        {
            var path = TestFile("test.XPWE");
            File.Exists(path).Should().BeTrue($"file {path} non trovato");

            var parser = new XpweDeserializer();
            var serializer = new XpweSerializer();

            var result1 = parser.ParseFile(path);
            var xml = serializer.SaveToString(result1);
            var cleanXml = xml.Replace(PrimusPI, "");
            var result2 = parser.ParseString(cleanXml);

            result2.Document.TipoDocumento.Should().Be(result1.Document.TipoDocumento);
            result2.SuperCapitoli.Count.Should().Be(result1.SuperCapitoli.Count);
            result2.PriceItems.Count.Should().Be(result1.PriceItems.Count);
            result2.MeasurementRows.Count.Should().Be(result1.MeasurementRows.Count);
        }

        [Fact]
        public void SaveToString_Deterministic_SameInputSameOutput()
        {
            var result = new XpweImportResult();
            result.Document.TipoDocumento = 0;
            result.Document.Versione = "5.04";
            result.Document.Fgs = 2147614720L;

            var serializer = new XpweSerializer();
            var a = serializer.SaveToString(result);
            var b = serializer.SaveToString(result);
            a.Should().Be(b, "serialize deve essere deterministico");
        }

        [Fact]
        public void SaveToString_StartsWithPriMusProcessingInstruction()
        {
            var result = new XpweImportResult();
            var xml = new XpweSerializer().SaveToString(result);
            xml.Should().StartWith(PrimusPI);
        }

        [Fact]
        public void SaveToString_ContainsAccaCopyright()
        {
            var result = new XpweImportResult();
            var xml = new XpweSerializer().SaveToString(result);
            xml.Should().Contain("Copyright ACCA software S.p.A.");
        }

        [Fact]
        public void Roundtrip_CMESample_PreservesCountsIncludingRGItem()
        {
            var path = TestFile("CME_Sample.xpwe");
            if (!File.Exists(path)) return;  // opzionale

            var parser = new XpweDeserializer();
            var serializer = new XpweSerializer();

            var result1 = parser.ParseFile(path);
            var xml = serializer.SaveToString(result1);
            var cleanXml = xml.Replace(PrimusPI, "");
            var result2 = parser.ParseString(cleanXml);

            result2.Document.TipoDocumento.Should().Be(1, "CME_Sample è un Computo");
            result2.SuperCapitoli.Count.Should().Be(result1.SuperCapitoli.Count);
            result2.SuperCategorie.Count.Should().Be(result1.SuperCategorie.Count);
            result2.PriceItems.Count.Should().Be(result1.PriceItems.Count);
            result2.MeasurementRows.Count.Should().Be(result1.MeasurementRows.Count);

            int rgTotal1 = 0, rgTotal2 = 0;
            foreach (var m in result1.MeasurementRows) rgTotal1 += m.SubRows.Count;
            foreach (var m in result2.MeasurementRows) rgTotal2 += m.SubRows.Count;
            rgTotal2.Should().Be(rgTotal1, "tutti gli RGItem devono sopravvivere al roundtrip");
        }
    }
}
