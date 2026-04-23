using FluentAssertions;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Reports;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace QtoRevitPlugin.Tests.Sprint9
{
    public class PdfExporterTests
    {
        [Fact]
        public void Export_ProducesNonEmptyPdfFile()
        {
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

            var ds = new ReportDataSet
            {
                Session = new WorkSession { Id = 1, ProjectName = "Test", SessionName = "S1" },
                Header = new ReportHeader { Titolo = "Computo Test", Committente = "C", DirettoreLavori = "DL", DataCreazione = DateTime.Now },
                GrandTotal = 100m
            };
            ds.UnchaperedEntries.Add(new ReportEntry
            {
                OrderIndex = 1, EpCode = "EP001", EpDescription = "Voce test",
                Unit = "m²", Quantity = 10, UnitPrice = 10m, Total = 100m, ElementId = "1"
            });

            var path = Path.Combine(Path.GetTempPath(), $"pdf_{Guid.NewGuid()}.pdf");
            try
            {
                new PdfExporter().Export(ds, path, new ReportExportOptions());
                File.Exists(path).Should().BeTrue();
                new FileInfo(path).Length.Should().BeGreaterThan(1000);
                var firstBytes = File.ReadAllBytes(path).Take(4).ToArray();
                // PDF magic bytes: %PDF
                firstBytes[0].Should().Be((byte)'%');
                firstBytes[1].Should().Be((byte)'P');
                firstBytes[2].Should().Be((byte)'D');
                firstBytes[3].Should().Be((byte)'F');
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
