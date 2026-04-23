using ClosedXML.Excel;
using FluentAssertions;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Reports;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace QtoRevitPlugin.Tests.Sprint9
{
    public class ExcelExporterTests
    {
        private static ReportDataSet MakeDataset()
        {
            var ds = new ReportDataSet
            {
                Session = new WorkSession { Id = 1, ProjectName = "Test", SessionName = "S1" },
                Header = new ReportHeader { Titolo = "T", Committente = "C", DirettoreLavori = "DL", DataCreazione = DateTime.Now },
                GrandTotal = 100m
            };
            ds.UnchaperedEntries.Add(new ReportEntry
            {
                OrderIndex = 1, EpCode = "EP001", EpDescription = "D", Unit = "m",
                Quantity = 10, UnitPrice = 10m, Total = 100m, ElementId = "1", Category = "Walls"
            });
            return ds;
        }

        [Fact]
        public void Export_FileHasTwoSheets_ComputoAndMetadati()
        {
            var path = Path.Combine(Path.GetTempPath(), $"xlsx_{Guid.NewGuid()}.xlsx");
            try
            {
                new ExcelExporter().Export(MakeDataset(), path, new ReportExportOptions());
                using var wb = new XLWorkbook(path);
                wb.Worksheets.Count().Should().Be(2);
                wb.Worksheets.Any(ws => ws.Name == "Computo").Should().BeTrue();
                wb.Worksheets.Any(ws => ws.Name == "Metadati").Should().BeTrue();
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Export_ComputoSheet_HasEightColumnsInOrder()
        {
            var path = Path.Combine(Path.GetTempPath(), $"xlsx_cols_{Guid.NewGuid()}.xlsx");
            try
            {
                new ExcelExporter().Export(MakeDataset(), path, new ReportExportOptions());
                using var wb = new XLWorkbook(path);
                var ws = wb.Worksheet("Computo");
                ws.Cell(1, 1).Value.ToString().Should().Be("N°");
                ws.Cell(1, 2).Value.ToString().Should().Be("Capitolo");
                ws.Cell(1, 3).Value.ToString().Should().Be("Codice");
                ws.Cell(1, 4).Value.ToString().Should().Be("Descrizione");
                ws.Cell(1, 5).Value.ToString().Should().Be("UM");
                ws.Cell(1, 6).Value.ToString().Should().Be("Quantità");
                ws.Cell(1, 7).Value.ToString().Should().Be("Prezzo");
                ws.Cell(1, 8).Value.ToString().Should().Be("Importo");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
