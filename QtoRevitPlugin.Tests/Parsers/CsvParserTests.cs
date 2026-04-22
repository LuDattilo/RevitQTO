using System.IO;
using System.Text;
using FluentAssertions;
using QtoRevitPlugin.Parsers;
using Xunit;

namespace QtoRevitPlugin.Tests.Parsers
{
    public class CsvParserTests
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
        public void CanHandle_ReturnsTrueFor_CsvTsvTxt()
        {
            var parser = new CsvParser();

            parser.CanHandle("listino.csv").Should().BeTrue();
            parser.CanHandle("listino.tsv").Should().BeTrue();
            parser.CanHandle("listino.txt").Should().BeTrue();
            parser.CanHandle("listino.CSV").Should().BeTrue();

            parser.CanHandle("listino.xml").Should().BeFalse();
            parser.CanHandle("listino.xlsx").Should().BeFalse();
            parser.CanHandle("").Should().BeFalse();
        }

        // -----------------------------------------------------------------
        // Delimiter auto-detect
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_SemicolonDelimiter_ItalianExcel_Works()
        {
            const string csv = "Codice;Descrizione;U.M.;Prezzo Unitario\r\n" +
                               "A.01.001;Scavo di sbancamento;mc;12.50\r\n" +
                               "A.01.002;Rinterro;mc;5.00\r\n";

            using var stream = FromString(csv);

            var result = new CsvParser().Parse(stream, "listino");

            result.Items.Should().HaveCount(2);
            result.Metadata.Source.Should().Be("CSV");
            result.Items[0].Code.Should().Be("A.01.001");
            result.Items[0].UnitPrice.Should().Be(12.50);
        }

        [Fact]
        public void Parse_CommaDelimiter_AutoDetected()
        {
            const string csv = "Codice,Descrizione,U.M.,Prezzo Unitario\r\n" +
                               "A.01.001,Scavo,mc,12.50\r\n";

            using var stream = FromString(csv);

            var result = new CsvParser().Parse(stream, "listino");

            result.Metadata.Source.Should().Be("CSV");
            result.Items.Should().HaveCount(1);
            result.Items[0].Code.Should().Be("A.01.001");
            result.Items[0].UnitPrice.Should().Be(12.50);
        }

        [Fact]
        public void Parse_TabDelimiter_TsvDetected()
        {
            const string csv = "Codice\tDescrizione\tU.M.\tPrezzo Unitario\r\n" +
                               "A.01.001\tScavo\tmc\t12.50\r\n";

            using var stream = FromString(csv);

            var result = new CsvParser().Parse(stream, "listino");

            result.Metadata.Source.Should().Be("TSV");
            result.Items.Should().HaveCount(1);
            result.Items[0].Code.Should().Be("A.01.001");
        }

        // -----------------------------------------------------------------
        // Header heuristic
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_HeaderHeuristic_ItalianColumnNames()
        {
            const string csv = "Codice;Descrizione;U.M.;Prezzo Unitario\r\n" +
                               "A.01.001;Scavo di sbancamento;mc;12,50\r\n";

            using var stream = FromString(csv);

            var result = new CsvParser().Parse(stream, "listino");

            result.Items.Should().HaveCount(1);
            var item = result.Items[0];
            item.Code.Should().Be("A.01.001");
            item.Description.Should().Be("Scavo di sbancamento");
            item.Unit.Should().Be("mc");
            item.UnitPrice.Should().Be(12.50);
        }

        // -----------------------------------------------------------------
        // Quoted fields
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_QuotedField_WithEmbeddedDelimiter()
        {
            const string csv = "Codice;Descrizione;U.M.;Prezzo\r\n" +
                               "A.01.001;\"descrizione con ; inside\";mc;10.00\r\n";

            using var stream = FromString(csv);

            var result = new CsvParser().Parse(stream, "quoted");

            result.Items.Should().HaveCount(1);
            result.Items[0].Description.Should().Be("descrizione con ; inside");
            result.Items[0].UnitPrice.Should().Be(10.00);
        }

        [Fact]
        public void Parse_DoubleQuoteEscape_InsideQuotedField()
        {
            const string csv = "Codice;Descrizione;U.M.;Prezzo\r\n" +
                               "A.01.001;\"descr \"\"con virgolette\"\" dentro\";mc;10.00\r\n";

            using var stream = FromString(csv);

            var result = new CsvParser().Parse(stream, "escape");

            result.Items.Should().HaveCount(1);
            result.Items[0].Description.Should().Be("descr \"con virgolette\" dentro");
        }

        // -----------------------------------------------------------------
        // Decimale italiano
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_ItalianDecimal_PriceComma()
        {
            const string csv = "Codice;Descrizione;U.M.;Prezzo Unitario\r\n" +
                               "A.01.001;Scavo;mc;12,50\r\n";

            using var stream = FromString(csv);

            var result = new CsvParser().Parse(stream, "italiano");

            result.Items.Should().HaveCount(1);
            result.Items[0].UnitPrice.Should().Be(12.50);
        }

        // -----------------------------------------------------------------
        // Colonne obbligatorie
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_MissingMandatoryColumn_AbortsWithWarning()
        {
            // Header senza Descrizione
            const string csv = "Codice;U.M.;Prezzo\r\n" +
                               "A.01.001;mc;10.00\r\n";

            using var stream = FromString(csv);

            var result = new CsvParser().Parse(stream, "missing-col");

            result.Items.Should().BeEmpty();
            result.Warnings.Should().Contain(w => w.Contains("Description"));
        }

        // -----------------------------------------------------------------
        // Padding righe
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_RowWithFewerColumns_PadsAndWarns()
        {
            // Header 4 col, riga dati con 3 col (manca Prezzo)
            const string csv = "Codice;Descrizione;U.M.;Prezzo\r\n" +
                               "A.01.001;Scavo;mc\r\n";

            using var stream = FromString(csv);

            var result = new CsvParser().Parse(stream, "short-row");

            result.Items.Should().HaveCount(1);
            result.Items[0].Code.Should().Be("A.01.001");
            result.Items[0].UnitPrice.Should().Be(0); // prezzo paddato → 0
            result.Warnings.Should().Contain(w => w.Contains("colonne"));
        }

        // -----------------------------------------------------------------
        // Righe vuote
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_EmptyRow_Skipped()
        {
            const string csv = "Codice;Descrizione;U.M.;Prezzo\r\n" +
                               "A.01.001;Scavo;mc;10.00\r\n" +
                               "\r\n" +
                               "A.01.002;Rinterro;mc;5.00\r\n";

            using var stream = FromString(csv);

            var result = new CsvParser().Parse(stream, "empty-rows");

            result.Items.Should().HaveCount(2);
            result.Items[0].Code.Should().Be("A.01.001");
            result.Items[1].Code.Should().Be("A.01.002");
        }

        [Fact]
        public void Parse_EmptyCode_Skipped()
        {
            const string csv = "Codice;Descrizione;U.M.;Prezzo\r\n" +
                               ";Descrizione senza codice;mc;10.00\r\n" +
                               "A.01.001;Scavo;mc;5.00\r\n";

            using var stream = FromString(csv);

            var result = new CsvParser().Parse(stream, "empty-code");

            result.Items.Should().HaveCount(1);
            result.Items[0].Code.Should().Be("A.01.001");
            result.Warnings.Should().Contain(w => w.Contains("Code vuoto"));
        }

        // -----------------------------------------------------------------
        // Chapter derivation
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_DerivesChapter_FromCode_WhenNoChapterColumn()
        {
            // Nessuna colonna Capitolo/SuperCapitolo → derivazione dal code
            const string csv = "Codice;Descrizione;U.M.;Prezzo\r\n" +
                               "A.01.001;Scavo;mc;10.00\r\n";

            using var stream = FromString(csv);

            var result = new CsvParser().Parse(stream, "derive");

            result.Items.Should().HaveCount(1);
            var item = result.Items[0];
            item.SuperChapter.Should().Be("A");
            item.Chapter.Should().Be("A.01");
            item.SubChapter.Should().BeEmpty();
        }
    }
}
