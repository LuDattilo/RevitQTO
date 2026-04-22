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
    }
}
