using FluentAssertions;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace QtoRevitPlugin.Tests.Computo
{
    public class ManualItemsCsvParserTests
    {
        [Fact]
        public void Parse_EmptyContent_NoEntriesNoErrors()
        {
            var result = ManualItemsCsvParser.Parse("", sessionId: 1);
            result.Entries.Should().BeEmpty();
            // Contenuto vuoto → no error (silent). Errore arriva solo su contenuto malformato con header atteso.
        }

        [Fact]
        public void Parse_ValidFile_ParsesAllRows()
        {
            var csv =
                "EpCode;Description;Quantity;Unit;UnitPrice;Notes\n" +
                "OS.001;Oneri sicurezza;1,00;cad;1200,00;Verbale 12/03\n" +
                "TR.042;Trasporto discarica;5,00;m3;85,00;DDT n.142\n" +
                "MD.010;Manodopera extra;8,00;h;32,50;Ordine di servizio n.7\n";

            var result = ManualItemsCsvParser.Parse(csv, sessionId: 42);

            result.HasErrors.Should().BeFalse();
            result.Entries.Should().HaveCount(3);

            result.Entries[0].EpCode.Should().Be("OS.001");
            result.Entries[0].EpDescription.Should().Be("Oneri sicurezza");
            result.Entries[0].Quantity.Should().Be(1.0);
            result.Entries[0].Unit.Should().Be("cad");
            result.Entries[0].UnitPrice.Should().Be(1200.0);
            result.Entries[0].Notes.Should().Be("Verbale 12/03");
            result.Entries[0].SessionId.Should().Be(42);

            result.Entries[2].Quantity.Should().Be(8.0);
            result.Entries[2].UnitPrice.Should().Be(32.5);
        }

        [Fact]
        public void Parse_HeaderCaseInsensitive()
        {
            var csv =
                "epcode;description;QUANTITY;unit;UNITPRICE\n" +
                "A1;Test;5,5;kg;10\n";

            var result = ManualItemsCsvParser.Parse(csv, sessionId: 1);
            result.Entries.Should().ContainSingle();
            result.Entries[0].Quantity.Should().Be(5.5);
        }

        [Fact]
        public void Parse_HeaderOrderIndependent()
        {
            // Colonne in ordine diverso
            var csv =
                "UnitPrice;Notes;Quantity;EpCode\n" +
                "50,00;Nota test;3,5;X.100\n";

            var result = ManualItemsCsvParser.Parse(csv, sessionId: 1);
            result.Entries.Should().ContainSingle();
            result.Entries[0].EpCode.Should().Be("X.100");
            result.Entries[0].Quantity.Should().Be(3.5);
            result.Entries[0].UnitPrice.Should().Be(50.0);
            result.Entries[0].Notes.Should().Be("Nota test");
            result.Entries[0].EpDescription.Should().BeEmpty(); // colonna mancante OK
        }

        [Fact]
        public void Parse_MissingEpCodeHeader_ReturnsError()
        {
            var csv = "Description;Quantity\nTest;1,0\n";
            var result = ManualItemsCsvParser.Parse(csv, sessionId: 1);
            result.HasErrors.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Contains("EpCode"));
        }

        [Fact]
        public void Parse_MissingQuantityHeader_ReturnsError()
        {
            var csv = "EpCode;Description\nX;Test\n";
            var result = ManualItemsCsvParser.Parse(csv, sessionId: 1);
            result.HasErrors.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Contains("Quantity"));
        }

        [Fact]
        public void Parse_EmptyEpCode_SkipsAndReports()
        {
            var csv =
                "EpCode;Quantity\n" +
                ";5,0\n" +  // EpCode vuoto
                "VALIDO;1,0\n";

            var result = ManualItemsCsvParser.Parse(csv, sessionId: 1);

            result.Entries.Should().ContainSingle();
            result.Entries[0].EpCode.Should().Be("VALIDO");
            result.Errors.Should().ContainSingle().Which.Should().Contain("EpCode vuoto");
        }

        [Fact]
        public void Parse_InvalidNumber_ReportsAndContinues()
        {
            var csv =
                "EpCode;Quantity;UnitPrice\n" +
                "X1;abc;100\n" + // Quantity non numerica
                "X2;2,5;50\n";    // valida

            var result = ManualItemsCsvParser.Parse(csv, sessionId: 1);

            result.Entries.Should().ContainSingle();
            result.Entries[0].EpCode.Should().Be("X2");
            result.Errors.Should().Contain(e => e.Contains("abc"));
        }

        [Fact]
        public void Parse_IgnoresCommentLinesAndBlankLines()
        {
            var csv =
                "# commento top\n" +
                "EpCode;Quantity\n" +
                "\n" +
                "# altro commento\n" +
                "X1;1,0\n" +
                "\n" +
                "X2;2,0\n";

            var result = ManualItemsCsvParser.Parse(csv, sessionId: 1);
            result.Entries.Should().HaveCount(2);
        }

        [Fact]
        public void Parse_QuotedFieldsWithSeparator()
        {
            // Campo con ";" dentro — deve essere racchiuso in virgolette
            var csv =
                "EpCode;Description;Quantity\n" +
                "A1;\"Via Roma; 10\";1\n";

            var result = ManualItemsCsvParser.Parse(csv, sessionId: 1);
            result.Entries.Should().ContainSingle();
            result.Entries[0].EpDescription.Should().Be("Via Roma; 10");
        }

        [Fact]
        public void Parse_NumberWithThousandsSeparator()
        {
            // 1.200,50 (italiano) o 1200.50 (invariant)
            var csv =
                "EpCode;Quantity;UnitPrice\n" +
                "A1;1,0;1.200,50\n" +
                "A2;1,0;1200.50\n";

            var result = ManualItemsCsvParser.Parse(csv, sessionId: 1);
            result.Entries.Should().HaveCount(2);
            result.Entries[0].UnitPrice.Should().BeApproximately(1200.5, 0.001);
            result.Entries[1].UnitPrice.Should().BeApproximately(1200.5, 0.001);
        }

        [Fact]
        public void Export_ProducesRoundtrippableCsv()
        {
            var entries = new List<ManualQuantityEntry>
            {
                new ManualQuantityEntry
                {
                    EpCode = "OS.001", EpDescription = "Oneri sicurezza",
                    Quantity = 1.0, Unit = "cad", UnitPrice = 1200.0, Notes = "Verbale 12/03"
                },
                new ManualQuantityEntry
                {
                    EpCode = "X.500", EpDescription = "Voce con ; separator e \" quote",
                    Quantity = 2.5, Unit = "m", UnitPrice = 50.0
                }
            };

            var csv = ManualItemsCsvParser.Export(entries);

            csv.Should().Contain("EpCode;Description;Quantity;Unit;UnitPrice;Notes");
            csv.Should().Contain("OS.001");

            // Roundtrip: parse risultato e verifica
            var parsed = ManualItemsCsvParser.Parse(csv, sessionId: 99);
            parsed.HasErrors.Should().BeFalse();
            parsed.Entries.Should().HaveCount(2);
            parsed.Entries[0].EpCode.Should().Be("OS.001");
            parsed.Entries[1].EpDescription.Should().Contain("separator");
        }

        [Fact]
        public void ParseFile_NonExistentPath_Throws()
        {
            Action act = () => ManualItemsCsvParser.ParseFile("/not/a/real/path/x.csv", sessionId: 1);
            act.Should().Throw<FileNotFoundException>();
        }
    }
}
