using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using QtoRevitPlugin.Parsers;
using Xunit;

namespace QtoRevitPlugin.Tests.Parsers
{
    public class ExcelParserTests
    {
        /// <summary>
        /// Costruisce un workbook .xlsx in memoria con header + righe (tutte stringhe: ClosedXML
        /// trasformerà in number quando parsabile, così copriamo sia il path testuale che quello numerico).
        /// </summary>
        private static MemoryStream BuildWorkbook(
            string[] header,
            string[][] rows,
            string sheetName = "Prezzario",
            bool numericPrices = false,
            int priceColIndex = -1)
        {
            var wb = new XLWorkbook();
            try
            {
                var ws = wb.Worksheets.Add(sheetName);
                for (int c = 0; c < header.Length; c++)
                    ws.Cell(1, c + 1).Value = header[c];

                for (int r = 0; r < rows.Length; r++)
                {
                    for (int c = 0; c < rows[r].Length; c++)
                    {
                        if (numericPrices && c == priceColIndex
                            && double.TryParse(rows[r][c],
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out var d))
                        {
                            ws.Cell(r + 2, c + 1).Value = d;
                        }
                        else
                        {
                            ws.Cell(r + 2, c + 1).Value = rows[r][c];
                        }
                    }
                }

                var ms = new MemoryStream();
                wb.SaveAs(ms);
                ms.Position = 0;
                return ms;
            }
            finally
            {
                wb.Dispose();
            }
        }

        // -----------------------------------------------------------------
        // CanHandle
        // -----------------------------------------------------------------

        [Fact]
        public void CanHandle_ReturnsTrueFor_XlsxXlsmXls()
        {
            var parser = new ExcelParser();

            parser.CanHandle("listino.xlsx").Should().BeTrue();
            parser.CanHandle("listino.xlsm").Should().BeTrue();
            parser.CanHandle("listino.xls").Should().BeTrue();
            parser.CanHandle("listino.XLSX").Should().BeTrue();

            parser.CanHandle("listino.csv").Should().BeFalse();
            parser.CanHandle("listino.xml").Should().BeFalse();
            parser.CanHandle("").Should().BeFalse();
        }

        // -----------------------------------------------------------------
        // Parsing base
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_SimpleWorkbook_ExtractsItems()
        {
            using var stream = BuildWorkbook(
                header: new[] { "Codice", "Descrizione", "U.M.", "Prezzo Unitario" },
                rows: new[]
                {
                    new[] { "A.01.001", "Scavo di sbancamento", "mc", "12.50" },
                    new[] { "A.01.002", "Rinterro",             "mc", "5.00" },
                    new[] { "A.02.001", "Muratura",             "mq", "85.00" }
                },
                numericPrices: true,
                priceColIndex: 3);

            var result = new ExcelParser().Parse(stream, "listino");

            result.Items.Should().HaveCount(3);
            result.Metadata.Source.Should().Be("XLSX");
            result.Metadata.Name.Should().Be("listino");
            result.Metadata.RowCount.Should().Be(3);
            result.Items[0].Code.Should().Be("A.01.001");
            result.Items[0].UnitPrice.Should().Be(12.50);
        }

        // -----------------------------------------------------------------
        // Heuristic Italian headers
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_HeaderHeuristic_ItalianColumnNames()
        {
            using var stream = BuildWorkbook(
                header: new[] { "Codice", "Descrizione", "U.M.", "Prezzo Unitario" },
                rows: new[]
                {
                    new[] { "A.01.001", "Scavo di sbancamento", "mc", "12.50" }
                },
                numericPrices: true,
                priceColIndex: 3);

            var result = new ExcelParser().Parse(stream, "listino");

            result.Items.Should().HaveCount(1);
            var item = result.Items[0];
            item.Code.Should().Be("A.01.001");
            item.Description.Should().Be("Scavo di sbancamento");
            item.Unit.Should().Be("mc");
            item.UnitPrice.Should().Be(12.50);
        }

        // -----------------------------------------------------------------
        // Chapter derivation
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_DerivesChapterFromCode_WhenNoChapterColumn()
        {
            using var stream = BuildWorkbook(
                header: new[] { "Codice", "Descrizione", "U.M.", "Prezzo" },
                rows: new[]
                {
                    new[] { "A.01.001", "Scavo", "mc", "10.00" }
                },
                numericPrices: true,
                priceColIndex: 3);

            var result = new ExcelParser().Parse(stream, "derive");

            result.Items.Should().HaveCount(1);
            var item = result.Items[0];
            item.SuperChapter.Should().Be("A");
            item.Chapter.Should().Be("A.01");
            item.SubChapter.Should().BeEmpty();
        }

        // -----------------------------------------------------------------
        // Colonne obbligatorie
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_MissingMandatoryColumn_AbortsWithWarning()
        {
            // Header senza Descrizione
            using var stream = BuildWorkbook(
                header: new[] { "Codice", "U.M.", "Prezzo" },
                rows: new[]
                {
                    new[] { "A.01.001", "mc", "10.00" }
                });

            var result = new ExcelParser().Parse(stream, "missing-col");

            result.Items.Should().BeEmpty();
            result.Warnings.Should().Contain(w => w.Contains("Description"));
        }

        // -----------------------------------------------------------------
        // Righe vuote
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_EmptyRow_Skipped()
        {
            using var stream = BuildWorkbook(
                header: new[] { "Codice", "Descrizione", "U.M.", "Prezzo" },
                rows: new[]
                {
                    new[] { "A.01.001", "Scavo",    "mc", "10.00" },
                    new[] { "",          "",         "",   ""       }, // totalmente vuota
                    new[] { "A.01.002", "Rinterro", "mc", "5.00" }
                },
                numericPrices: true,
                priceColIndex: 3);

            var result = new ExcelParser().Parse(stream, "empty-rows");

            result.Items.Should().HaveCount(2);
            result.Items[0].Code.Should().Be("A.01.001");
            result.Items[1].Code.Should().Be("A.01.002");
        }

        // -----------------------------------------------------------------
        // Code vuoto
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_EmptyCode_SkippedWithWarning()
        {
            using var stream = BuildWorkbook(
                header: new[] { "Codice", "Descrizione", "U.M.", "Prezzo" },
                rows: new[]
                {
                    new[] { "",          "Senza codice", "mc", "10.00" },
                    new[] { "A.01.001", "Scavo",         "mc", "5.00" }
                },
                numericPrices: true,
                priceColIndex: 3);

            var result = new ExcelParser().Parse(stream, "empty-code");

            result.Items.Should().HaveCount(1);
            result.Items[0].Code.Should().Be("A.01.001");
            result.Warnings.Should().Contain(w => w.Contains("Code vuoto"));
        }

        // -----------------------------------------------------------------
        // Prezzo non valido
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_InvalidPrice_KeepsItemWithWarning()
        {
            // Prezzo = testo non numerico → UnitPrice=0 + warning, item mantenuto.
            using var stream = BuildWorkbook(
                header: new[] { "Codice", "Descrizione", "U.M.", "Prezzo" },
                rows: new[]
                {
                    new[] { "A.01.001", "Scavo", "mc", "non-un-numero" }
                });

            var result = new ExcelParser().Parse(stream, "bad-price");

            result.Items.Should().HaveCount(1);
            result.Items[0].Code.Should().Be("A.01.001");
            result.Items[0].UnitPrice.Should().Be(0);
            result.Warnings.Should().Contain(w => w.Contains("prezzo") && w.Contains("UnitPrice=0"));
        }

        // -----------------------------------------------------------------
        // Decimale italiano
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_ItalianDecimalComma_ParsedCorrectly()
        {
            // Cella testuale "12,50" → TryParseDecimal deve gestire la virgola come separatore decimale.
            using var stream = BuildWorkbook(
                header: new[] { "Codice", "Descrizione", "U.M.", "Prezzo Unitario" },
                rows: new[]
                {
                    new[] { "A.01.001", "Scavo", "mc", "12,50" }
                });

            var result = new ExcelParser().Parse(stream, "italiano");

            result.Items.Should().HaveCount(1);
            result.Items[0].UnitPrice.Should().Be(12.50);
        }

        // -----------------------------------------------------------------
        // .xls legacy warning
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_XlsExtension_EmitsLegacyWarning()
        {
            var parser = new ExcelParser();

            // Creiamo un file temporaneo .xls con un blob random (ClosedXML fallirà ad aprire);
            // il parser deve rilevare estensione .xls e warning dedicato, OPPURE fallback su
            // "file corrotto". Il test qui verifica che NON crashi e che il result abbia warning.
            var tmp = Path.Combine(Path.GetTempPath(), "qto-test-legacy.xls");
            try
            {
                File.WriteAllBytes(tmp, new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }); // CFBF signature

                var result = parser.Parse(tmp);

                result.Metadata.Source.Should().Be("XLS");
                result.Items.Should().BeEmpty();
                // Un warning deve esserci: o "legacy" (via sourceName hint) o "non valido/corrotto" (open fallito).
                result.Warnings.Should().NotBeEmpty();
            }
            finally
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }

        // -----------------------------------------------------------------
        // Multiple sheets → warning + primo valido scelto
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_MultipleValidSheets_ChoosesFirstAndWarns()
        {
            // Creiamo un workbook con 2 fogli entrambi popolati; aspettiamoci warning "Trovati".
            var wb = new XLWorkbook();
            try
            {
                var ws1 = wb.Worksheets.Add("Sheet1");
                ws1.Cell(1, 1).Value = "Codice"; ws1.Cell(1, 2).Value = "Descrizione";
                ws1.Cell(1, 3).Value = "U.M.";   ws1.Cell(1, 4).Value = "Prezzo";
                ws1.Cell(2, 1).Value = "A.01.001"; ws1.Cell(2, 2).Value = "Scavo";
                ws1.Cell(2, 3).Value = "mc";       ws1.Cell(2, 4).Value = 10.0;

                var ws2 = wb.Worksheets.Add("Sheet2");
                ws2.Cell(1, 1).Value = "Codice"; ws2.Cell(1, 2).Value = "Descrizione";
                ws2.Cell(1, 3).Value = "U.M.";   ws2.Cell(1, 4).Value = "Prezzo";
                ws2.Cell(2, 1).Value = "B.01.001"; ws2.Cell(2, 2).Value = "Altro";
                ws2.Cell(2, 3).Value = "mq";       ws2.Cell(2, 4).Value = 20.0;

                using var ms = new MemoryStream();
                wb.SaveAs(ms);
                ms.Position = 0;

                var result = new ExcelParser().Parse(ms, "multi");

                result.Items.Should().HaveCount(1);
                result.Items[0].Code.Should().Be("A.01.001"); // primo foglio
                result.Warnings.Should().Contain(w => w.Contains("Trovati") && w.Contains("fogli"));
            }
            finally
            {
                wb.Dispose();
            }
        }
    }
}
