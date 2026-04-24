using System.IO;
using System.Linq;
using FluentAssertions;
using QtoRevitPlugin.Xpwe;
using Xunit;

namespace QtoRevitPlugin.Tests.Computi
{
    public class XpweDeserializerTests
    {
        // Path relativo: dotnet test esegue da Tests/bin/Debug/netX → salgo 4 livelli per arrivare alla repo root.
        private static readonly string RepoRoot =
            Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(typeof(XpweDeserializerTests).Assembly.Location)!,
                "..", "..", "..", ".."));

        private static string TestFile(string name) => Path.Combine(RepoRoot, name);

        [Fact]
        public void Parse_TestXpwe_IsPrezziario()
        {
            var path = TestFile("test.XPWE");
            File.Exists(path).Should().BeTrue($"file {path} non trovato");
            var parser = new XpweDeserializer();
            var result = parser.ParseFile(path);

            result.Document.TipoDocumento.Should().Be(0, "test.XPWE è un Prezziario");
            result.Document.Versione.Should().Be("5.01");
            result.SuperCapitoli.Should().HaveCount(1);
            result.PriceItems.Should().HaveCount(1);
            result.MeasurementRows.Should().BeEmpty("prezziario non ha voci di computo");
        }

        [Fact]
        public void Parse_CMESample_IsComputo()
        {
            var path = TestFile("CME_Sample.xpwe");
            if (!File.Exists(path))
            {
                // Test opzionale: salta se il file sample non è checked-in
                return;
            }
            var parser = new XpweDeserializer();
            var result = parser.ParseFile(path);

            result.Document.TipoDocumento.Should().Be(1, "CME_Sample è un Computo");
            result.SuperCapitoli.Should().HaveCount(6);
            result.SuperCategorie.Should().HaveCountGreaterThan(3);
            result.PriceItems.Should().HaveCount(119);
            result.MeasurementRows.Should().HaveCount(168);

            // Un VCItem con RGItem
            var withSub = result.MeasurementRows.FirstOrDefault(m => m.SubRows.Count > 0);
            withSub.Should().NotBeNull("almeno un VCItem ha RGItem");
            withSub!.IDEP.Should().BeGreaterThan(0);
        }

        [Fact]
        public void Parse_DateExcelZero_NormalizesToNull()
        {
            var xml = @"<PweDocumento>
                <TipoDocumento>0</TipoDocumento>
                <Versione>5.04</Versione>
                <PweDatiGenerali>
                    <PweDGCapitoliCategorie>
                        <PweDGSuperCapitoli>
                            <DGSuperCapitoliItem ID='1'>
                                <Codice>01</Codice>
                                <DesSintetica>Test</DesSintetica>
                                <DataInit>30/12/1899</DataInit>
                            </DGSuperCapitoliItem>
                        </PweDGSuperCapitoli>
                    </PweDGCapitoliCategorie>
                </PweDatiGenerali>
            </PweDocumento>";
            var result = new XpweDeserializer().ParseString(xml);
            result.SuperCapitoli.Should().HaveCount(1);
            result.SuperCapitoli[0].Node.DataInit.Should().BeNull("30/12/1899 = Excel zero = null");
        }

        [Fact]
        public void Parse_IdZero_ResolvesToNullRef()
        {
            var xml = @"<PweDocumento>
                <TipoDocumento>0</TipoDocumento>
                <PweMisurazioni>
                    <PweElencoPrezzi>
                        <EPItem ID='1'>
                            <Tariffa>T1</Tariffa>
                            <DesRidotta>voce</DesRidotta>
                            <UnMisura>mc</UnMisura>
                            <Prezzo1>100</Prezzo1>
                            <IDSpCap>2</IDSpCap>
                            <IDCap>0</IDCap>
                            <IDSbCap>0</IDSbCap>
                        </EPItem>
                    </PweElencoPrezzi>
                </PweMisurazioni>
            </PweDocumento>";
            var result = new XpweDeserializer().ParseString(xml);
            result.PriceItems.Should().HaveCount(1);
            result.PriceItems[0].IDSpCap.Should().Be(2);
            result.PriceItems[0].IDCap.Should().BeNull("ID=0 → null");
            result.PriceItems[0].IDSbCap.Should().BeNull();
        }

        [Fact]
        public void Parse_MissingRoot_Throws()
        {
            var xml = "<NotPwe><x/></NotPwe>";
            var parser = new XpweDeserializer();
            var act = () => parser.ParseString(xml);
            act.Should().Throw<InvalidDataException>();
        }
    }
}
