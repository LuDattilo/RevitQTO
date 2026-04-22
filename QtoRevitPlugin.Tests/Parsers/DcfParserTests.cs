using System.IO;
using System.Text;
using FluentAssertions;
using QtoRevitPlugin.Parsers;
using Xunit;

namespace QtoRevitPlugin.Tests.Parsers
{
    public class DcfParserTests
    {
        private static Stream FromString(string content, Encoding? encoding = null)
        {
            encoding ??= Encoding.UTF8;
            return new MemoryStream(encoding.GetBytes(content));
        }

        // -----------------------------------------------------------------
        // CanHandle
        // -----------------------------------------------------------------

        [Fact]
        public void CanHandle_ReturnsTrueFor_DcfXpweXml()
        {
            var parser = new DcfParser();

            parser.CanHandle("listino.dcf").Should().BeTrue();
            parser.CanHandle("listino.xpwe").Should().BeTrue();
            parser.CanHandle("listino.xml").Should().BeTrue();
            parser.CanHandle("listino.DCF").Should().BeTrue();
            parser.CanHandle("listino.XPWE").Should().BeTrue();
            parser.CanHandle("listino.XML").Should().BeTrue();

            parser.CanHandle("listino.csv").Should().BeFalse();
            parser.CanHandle("listino.xlsx").Should().BeFalse();
            parser.CanHandle("listino").Should().BeFalse();
            parser.CanHandle("").Should().BeFalse();
        }

        // -----------------------------------------------------------------
        // Parse — formato flat
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_FlatFormat_ExtractsItemsWithCodeDescUnitPrice()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<Root>
  <VoceEP CodiceVoce=""A.01.001""
          DescrVoce=""Scavo di sbancamento""
          UnitaMisura=""mc""
          PrezzoUnitario=""12.50""
          Capitolo=""A - SCAVI"" />
</Root>";

            using var stream = FromString(xml);

            var result = new DcfParser().Parse(stream, "listino-test");

            result.Items.Should().HaveCount(1);
            var item = result.Items[0];
            item.Code.Should().Be("A.01.001");
            item.Description.Should().Be("Scavo di sbancamento");
            item.Unit.Should().Be("mc");
            item.UnitPrice.Should().Be(12.50);
            item.Chapter.Should().Be("A - SCAVI");
            result.TotalRowsDetected.Should().Be(1);
        }

        // -----------------------------------------------------------------
        // Parse — formato gerarchico
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_HierarchicalFormat_ExtractsChaptersFromAncestors()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<Root>
  <SuperCapitolo Nome=""A"">
    <Capitolo Nome=""A.01"">
      <SottoCapitolo Nome=""A.01.1"">
        <VoceEP CodiceVoce=""A.01.001""
                DescrVoce=""Scavo""
                UnitaMisura=""mc""
                PrezzoUnitario=""10.00"" />
      </SottoCapitolo>
    </Capitolo>
  </SuperCapitolo>
</Root>";

            using var stream = FromString(xml);

            var result = new DcfParser().Parse(stream, "listino-gerarchico");

            result.Items.Should().HaveCount(1);
            var item = result.Items[0];
            item.SuperChapter.Should().Be("A");
            item.Chapter.Should().Be("A.01");
            item.SubChapter.Should().Be("A.01.1");
        }

        // -----------------------------------------------------------------
        // Parse — nomi elemento alternativi
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_AlternateElementNames_VoceArticolo()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<Root>
  <Voce Codice=""B.01.001"" Descrizione=""Muratura"" UM=""mq"" Prezzo=""55.00"" />
  <Articolo Code=""C.01.001"" Description=""Intonaco"" Unit=""mq"" Price=""18.00"" />
</Root>";

            using var stream = FromString(xml);

            var result = new DcfParser().Parse(stream, "alt");

            result.Items.Should().HaveCount(2);
            result.Items.Should().Contain(i => i.Code == "B.01.001" && i.UnitPrice == 55.00);
            result.Items.Should().Contain(i => i.Code == "C.01.001" && i.UnitPrice == 18.00);
        }

        // -----------------------------------------------------------------
        // Parse — decimale italiano
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_ItalianDecimal_ParsesCommaSeparator()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<Root>
  <VoceEP CodiceVoce=""A.01.001""
          DescrVoce=""Scavo""
          UnitaMisura=""mc""
          PrezzoUnitario=""12,50"" />
</Root>";

            using var stream = FromString(xml);

            var result = new DcfParser().Parse(stream, "italiano");

            result.Items.Should().HaveCount(1);
            result.Items[0].UnitPrice.Should().Be(12.50);
        }

        // -----------------------------------------------------------------
        // Parse — codice mancante
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_MissingCode_AddsWarningAndSkips()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<Root>
  <VoceEP DescrVoce=""Senza codice"" UnitaMisura=""mc"" PrezzoUnitario=""10.00"" />
  <VoceEP CodiceVoce=""A.01.001"" DescrVoce=""OK"" UnitaMisura=""mc"" PrezzoUnitario=""10.00"" />
</Root>";

            using var stream = FromString(xml);

            var result = new DcfParser().Parse(stream, "missing-code");

            result.Items.Should().HaveCount(1);
            result.Items[0].Code.Should().Be("A.01.001");
            result.TotalRowsDetected.Should().Be(2);
            result.Warnings.Should().Contain(w => w.Contains("CodiceVoce"));
        }

        // -----------------------------------------------------------------
        // Parse — prezzo non valido
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_InvalidPrice_WarningButKeepsItem()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<Root>
  <VoceEP CodiceVoce=""A.01.001"" DescrVoce=""Prezzo sbagliato"" UnitaMisura=""mc"" PrezzoUnitario=""abc"" />
</Root>";

            using var stream = FromString(xml);

            var result = new DcfParser().Parse(stream, "bad-price");

            result.Items.Should().HaveCount(1);
            result.Items[0].UnitPrice.Should().Be(0);
            result.Warnings.Should().Contain(w => w.Contains("PrezzoUnitario") && w.Contains("abc"));
        }

        // -----------------------------------------------------------------
        // Parse — XML malformato
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_MalformedXml_ReturnsEmptyResultWithWarning()
        {
            const string xml = @"<Root><VoceEP CodiceVoce=""A.01""></Root>"; // tag VoceEP non chiuso

            using var stream = FromString(xml);

            var result = new DcfParser().Parse(stream, "malformed");

            result.Items.Should().BeEmpty();
            result.Warnings.Count.Should().BeGreaterThanOrEqualTo(1);
        }

        // -----------------------------------------------------------------
        // Parse — derivazione SuperChapter / Chapter dal code
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_DerivesSuperChapterFromCode_WhenNoHierarchyNoAttribute()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<Root>
  <VoceEP CodiceVoce=""A.01.001""
          DescrVoce=""Scavo""
          UnitaMisura=""mc""
          PrezzoUnitario=""10.00"" />
</Root>";

            using var stream = FromString(xml);

            var result = new DcfParser().Parse(stream, "derived");

            result.Items.Should().HaveCount(1);
            var item = result.Items[0];
            item.SuperChapter.Should().Be("A");
            item.Chapter.Should().Be("A.01");
            item.SubChapter.Should().BeEmpty();
        }

        // -----------------------------------------------------------------
        // Parse — Source metadata (via Parse(stream) resta "DCF")
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_StreamOverload_SourceDefaultsToDcf()
        {
            // Nota: Parse(Stream, sourceName) imposta sempre Source="DCF"; solo Parse(string filePath)
            // deriva XPWE/DCF dall'estensione reale. Documentiamo il comportamento via stream.
            const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<Root>
  <VoceEP CodiceVoce=""A.01.001"" DescrVoce=""Scavo"" UnitaMisura=""mc"" PrezzoUnitario=""10.00"" />
</Root>";

            using var stream = FromString(xml);

            var result = new DcfParser().Parse(stream, "listino.xpwe");

            result.Metadata.Source.Should().Be("DCF");
            result.Metadata.Name.Should().Be("listino.xpwe");
        }

        // -----------------------------------------------------------------
        // Formato C — EASY Regione Toscana (child elements + livelloN CDATA)
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_EasyToscanaFormat_ExtractsFromChildElements()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<EASY:Prezzario xmlns:EASY=""https://prezzariollpp.regione.toscana.it/prezzario.xsd"">
  <EASY:Contenuto>
    <EASY:Articolo codice=""TOS25_01.A03.001.001"">
      <EASY:livello1><![CDATA[NUOVE COSTRUZIONI EDILI.
Prezzi per una nuova costruzione.]]></EASY:livello1>
      <EASY:livello2><![CDATA[DEMOLIZIONE.
Con qualsiasi mezzo.]]></EASY:livello2>
      <EASY:livello3><![CDATA[TOTALE O PARZIALE DI EDIFICI]]></EASY:livello3>
      <EASY:livello4><![CDATA[con struttura in pietrame. Mezzi meccanici.]]></EASY:livello4>
      <EASY:um><![CDATA[m³]]></EASY:um>
      <EASY:prezzo>13.63017</EASY:prezzo>
    </EASY:Articolo>
  </EASY:Contenuto>
</EASY:Prezzario>";

            using var stream = FromString(xml);
            var result = new DcfParser().Parse(stream, "Firenze-2025");

            result.Items.Should().HaveCount(1);
            var item = result.Items[0];

            item.Code.Should().Be("TOS25_01.A03.001.001");
            item.Unit.Should().Be("m³");
            item.UnitPrice.Should().BeApproximately(13.63017, 0.00001);
            item.SuperChapter.Should().Be("NUOVE COSTRUZIONI EDILI.");  // prima riga di livello1
            item.Chapter.Should().Be("DEMOLIZIONE.");                    // prima riga di livello2
            item.SubChapter.Should().Be("TOTALE O PARZIALE DI EDIFICI"); // livello3
            item.Description.Should().Contain("TOTALE O PARZIALE DI EDIFICI"); // livello3 + livello4
            item.Description.Should().Contain("con struttura in pietrame");
            item.ShortDesc.Should().Contain("con struttura in pietrame");  // prima riga di livello4
        }

        [Fact]
        public void Parse_EasyToscanaFormat_DescriptionConcatsLivello3And4()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<EASY:Prezzario xmlns:EASY=""https://test.xsd"">
  <EASY:Articolo codice=""AAA.001"">
    <EASY:livello3><![CDATA[Livello 3 text]]></EASY:livello3>
    <EASY:livello4><![CDATA[Livello 4 text]]></EASY:livello4>
    <EASY:um>mq</EASY:um>
    <EASY:prezzo>1.23</EASY:prezzo>
  </EASY:Articolo>
</EASY:Prezzario>";

            using var stream = FromString(xml);
            var result = new DcfParser().Parse(stream, "test");

            result.Items.Should().ContainSingle()
                .Which.Description.Should().Contain("Livello 3 text").And.Contain("Livello 4 text");
        }

        // -----------------------------------------------------------------
        // Detection binario ACCA PriMus (AAMVHFSS magic)
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_AccaBinaryFormat_DetectedEarlyWithSpecificWarning()
        {
            // Magic bytes ACCA PriMus + qualche byte random
            var binary = new byte[]
            {
                (byte)'A', (byte)'A', (byte)'M', (byte)'V',
                (byte)'H', (byte)'F', (byte)'S', (byte)'S',
                0xFE, 0xFF, 0xFF, 0x00, 0x12, 0x34, 0x56, 0x78
            };
            using var stream = new MemoryStream(binary);

            var result = new DcfParser().Parse(stream, "binario-primus");

            result.Items.Should().BeEmpty();
            result.Warnings.Should().ContainSingle()
                .Which.Should().Contain("BINARIO").And.Contain("PriMus").And.Contain("XML");
        }

        [Fact]
        public void Parse_EasyToscanaFormat_OnlyLivello4_UsedAsShortDesc()
        {
            // Quando non c'è livello3 ma solo livello4, description = livello4, shortDesc = prima riga livello4
            const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<EASY:Prezzario xmlns:EASY=""https://test.xsd"">
  <EASY:Articolo codice=""BBB.002"">
    <EASY:livello4><![CDATA[Variante tecnica unica]]></EASY:livello4>
    <EASY:um>kg</EASY:um>
    <EASY:prezzo>0.45</EASY:prezzo>
  </EASY:Articolo>
</EASY:Prezzario>";

            using var stream = FromString(xml);
            var result = new DcfParser().Parse(stream, "test");

            var item = result.Items.Should().ContainSingle().Subject;
            item.Description.Should().Be("Variante tecnica unica");
            item.ShortDesc.Should().Be("Variante tecnica unica");
        }
    }
}
