using FluentAssertions;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Reports;
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace QtoRevitPlugin.Tests.Sprint9
{
    public class XpweExporterTests
    {
        private static readonly XNamespace XpweNs = "http://www.acca.it/primus/xpwe/v1";

        private static ReportDataSet MakeDataset()
        {
            var super = new ComputoChapter { Id = 1, Code = "01", Name = "SUPER", Level = 1 };
            var cat = new ComputoChapter { Id = 2, Code = "01.A", Name = "CAT", Level = 2, ParentChapterId = 1 };
            var sub = new ComputoChapter { Id = 3, Code = "01.A.01", Name = "SUB", Level = 3, ParentChapterId = 2 };

            var entry = new ReportEntry
            {
                OrderIndex = 1, EpCode = "EP001", EpDescription = "Test voce",
                Unit = "m²", Quantity = 10.5, UnitPrice = 20.00m, Total = 210.00m
            };
            var subNode = new ReportChapterNode { Chapter = sub, Subtotal = 210m };
            subNode.Entries.Add(entry);
            var catNode = new ReportChapterNode { Chapter = cat, Subtotal = 210m };
            catNode.Children.Add(subNode);
            var superNode = new ReportChapterNode { Chapter = super, Subtotal = 210m };
            superNode.Children.Add(catNode);

            var ds = new ReportDataSet
            {
                Session = new WorkSession { Id = 1, ProjectName = "Test" },
                Header = new ReportHeader
                {
                    Titolo = "Computo test", Committente = "Cliente", DirettoreLavori = "DL",
                    DataCreazione = new DateTime(2026, 4, 22, 10, 0, 0)
                },
                GrandTotal = 210m
            };
            ds.Chapters.Add(superNode);
            return ds;
        }

        [Fact]
        public void Export_ProducesValidXmlWithRootElement()
        {
            var path = Path.Combine(Path.GetTempPath(), $"xpwe_{Guid.NewGuid()}.xml");
            try
            {
                new XpweExporter().Export(MakeDataset(), path, new ReportExportOptions());
                File.Exists(path).Should().BeTrue();

                var doc = XDocument.Load(path);
                doc.Root!.Name.LocalName.Should().Be("PriMus");
                doc.Root.Name.NamespaceName.Should().Be(XpweNs.NamespaceName);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Export_HierarchyIsSuperCategoriaCategoriaSubCategoriaVoce()
        {
            var path = Path.Combine(Path.GetTempPath(), $"xpwe_h_{Guid.NewGuid()}.xml");
            try
            {
                new XpweExporter().Export(MakeDataset(), path, new ReportExportOptions());
                var doc = XDocument.Load(path);
                var super = doc.Descendants(XpweNs + "SuperCategoria").Single();
                var cat = super.Elements(XpweNs + "Categoria").Single();
                var sub = cat.Elements(XpweNs + "SubCategoria").Single();
                var voce = sub.Elements(XpweNs + "Voce").Single();
                voce.Element(XpweNs + "CodiceEP")!.Value.Should().Be("EP001");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Export_UsesUtf8EncodingDeclaration()
        {
            var path = Path.Combine(Path.GetTempPath(), $"xpwe_utf8_{Guid.NewGuid()}.xml");
            try
            {
                new XpweExporter().Export(MakeDataset(), path, new ReportExportOptions());
                var firstLine = File.ReadAllLines(path)[0];
                firstLine.Should().Contain("<?xml");
                firstLine.ToLowerInvariant().Should().Contain("utf-8");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
