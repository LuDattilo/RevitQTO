using FluentAssertions;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Reports;
using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace QtoRevitPlugin.Tests.Sprint9
{
    public class CsvExporterTests
    {
        private static ReportDataSet MakeDataset()
        {
            var entry = new ReportEntry
            {
                OrderIndex = 1, EpCode = "EP001", EpDescription = "Desc",
                Unit = "m²", Quantity = 10.5, UnitPrice = 20m, Total = 210m,
                ElementId = "42", Category = "Floors",
                Version = 1, CreatedBy = "test", CreatedAt = new DateTime(2026, 4, 22),
                AuditStatus = "Active"
            };
            var ds = new ReportDataSet { Session = new WorkSession { Id = 1, ProjectName = "t" } };
            ds.UnchaperedEntries.Add(entry);
            ds.GrandTotal = 210m;
            return ds;
        }

        [Fact]
        public void Export_BasicMode_HasNineColumns()
        {
            var path = Path.Combine(Path.GetTempPath(), $"csv_{Guid.NewGuid()}.csv");
            try
            {
                new CsvExporter().Export(MakeDataset(), path, new ReportExportOptions());
                var lines = File.ReadAllLines(path, Encoding.UTF8);
                lines[0].Split(';').Should().HaveCount(9);
                lines[0].Should().StartWith("Capitolo;Codice;Descrizione");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Export_AnalyticMode_IncludesAuditColumns()
        {
            var path = Path.Combine(Path.GetTempPath(), $"csv_a_{Guid.NewGuid()}.csv");
            try
            {
                new CsvExporter().Export(MakeDataset(), path, new ReportExportOptions { IncludeAuditFields = true });
                var header = File.ReadAllLines(path, Encoding.UTF8)[0];
                header.Should().Contain("Version");
                header.Should().Contain("CreatedBy");
                header.Should().Contain("AuditStatus");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Export_QuotesCellsContainingSemicolons()
        {
            var ds = MakeDataset();
            ds.UnchaperedEntries[0].EpDescription = "Voce con ; nel testo";

            var path = Path.Combine(Path.GetTempPath(), $"csv_q_{Guid.NewGuid()}.csv");
            try
            {
                new CsvExporter().Export(ds, path, new ReportExportOptions());
                var dataLine = File.ReadAllLines(path, Encoding.UTF8)[1];
                dataLine.Should().Contain("\"Voce con ; nel testo\"");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
