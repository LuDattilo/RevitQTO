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
    /// <summary>
    /// Smoke test per il nuovo XpweExporter conforme allo schema ACCA.
    /// Validazione completa (round-trip PriMus) è manuale — vedi docs/superpowers/specs.
    /// </summary>
    public class XpweExporterTests
    {
        private static ReportDataSet MakeDataset()
        {
            var super = new ComputoChapter { Id = 100, Code = "01", Name = "DEMOLIZIONI", Level = 1 };
            var cat = new ComputoChapter { Id = 101, Code = "01.A", Name = "Strutturali", Level = 2, ParentChapterId = 100 };
            var sub = new ComputoChapter { Id = 102, Code = "01.A.01", Name = "Solette", Level = 3, ParentChapterId = 101 };

            var entry = new ReportEntry
            {
                OrderIndex = 1, EpCode = "TOS25.A03.001", EpDescription = "Demolizione soletta c.a.",
                Unit = "m³", Quantity = 12.5, UnitPrice = 85.30m, Total = 1066.25m,
                ElementId = "1001", Category = "Floors"
            };
            var subNode = new ReportChapterNode { Chapter = sub, Subtotal = 1066.25m };
            subNode.Entries.Add(entry);
            var catNode = new ReportChapterNode { Chapter = cat, Subtotal = 1066.25m };
            catNode.Children.Add(subNode);
            var superNode = new ReportChapterNode { Chapter = super, Subtotal = 1066.25m };
            superNode.Children.Add(catNode);

            var ds = new ReportDataSet
            {
                Session = new WorkSession { Id = 1, ProjectName = "Test", SessionName = "Computo test" },
                Header = new ReportHeader
                {
                    Titolo = "Computo test", Committente = "Cliente", DirettoreLavori = "DL",
                    DataCreazione = new DateTime(2026, 4, 23, 10, 0, 0)
                },
                GrandTotal = 1066.25m
            };
            ds.Chapters.Add(superNode);
            return ds;
        }

        [Fact]
        public void Export_RootIsPweDocumento_NoNamespace()
        {
            var path = Path.Combine(Path.GetTempPath(), $"xpwe_new_{Guid.NewGuid()}.xml");
            try
            {
                new XpweExporter().Export(MakeDataset(), path, new ReportExportOptions());
                File.Exists(path).Should().BeTrue();

                var doc = XDocument.Load(path);
                doc.Root!.Name.LocalName.Should().Be("PweDocumento");
                // ACCA XPWE non usa namespaces XML
                doc.Root.Name.NamespaceName.Should().BeEmpty();
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Export_HasAccaHeader()
        {
            var path = Path.Combine(Path.GetTempPath(), $"xpwe_hdr_{Guid.NewGuid()}.xml");
            try
            {
                new XpweExporter().Export(MakeDataset(), path, new ReportExportOptions());
                var doc = XDocument.Load(path);
                var root = doc.Root!;
                root.Element("CopyRight")!.Value.Should().Contain("ACCA software");
                root.Element("TipoFormato")!.Value.Should().Be("XMLPwe");
                root.Element("Versione")!.Value.Should().Be("5.01");
                root.Element("Fgs").Should().NotBeNull();
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Export_ChaptersAreFlat_WithForeignKeys()
        {
            var path = Path.Combine(Path.GetTempPath(), $"xpwe_flat_{Guid.NewGuid()}.xml");
            try
            {
                new XpweExporter().Export(MakeDataset(), path, new ReportExportOptions());
                var doc = XDocument.Load(path);
                var capCats = doc.Descendants("PweDGCapitoliCategorie").Single();

                // 1 SuperCapitolo, 1 Capitolo, 1 SubCapitolo (struttura flat, non annidata)
                capCats.Descendants("DGSuperCapitoliItem").Should().HaveCount(1);
                capCats.Descendants("DGCapitoliItem").Should().HaveCount(1);
                capCats.Descendants("DGSubCapitoliItem").Should().HaveCount(1);

                // Ogni item ha attributo ID
                capCats.Descendants("DGSuperCapitoliItem").Single().Attribute("ID").Should().NotBeNull();
                // Capitolo ha IDPadre che punta al SuperCapitolo
                var capItem = capCats.Descendants("DGCapitoliItem").Single();
                capItem.Element("IDPadre").Should().NotBeNull();
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Export_EPItem_HasAccaFieldsInOrder()
        {
            var path = Path.Combine(Path.GetTempPath(), $"xpwe_ep_{Guid.NewGuid()}.xml");
            try
            {
                new XpweExporter().Export(MakeDataset(), path, new ReportExportOptions());
                var doc = XDocument.Load(path);
                var epItem = doc.Descendants("EPItem").Single();

                epItem.Element("Tariffa")!.Value.Should().Be("TOS25.A03.001");
                epItem.Element("UnMisura")!.Value.Should().Be("m³");
                epItem.Element("Prezzo1")!.Value.Should().Contain("85.30");
                epItem.Element("Prezzo2").Should().NotBeNull();  // Campo obbligatorio ACCA
                epItem.Element("IDSpCap").Should().NotBeNull();  // FK SuperCapitolo
                epItem.Element("Flags").Should().NotBeNull();
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Export_GeneratesVCItemForEachEntry()
        {
            var path = Path.Combine(Path.GetTempPath(), $"xpwe_vc_{Guid.NewGuid()}.xml");
            try
            {
                new XpweExporter().Export(MakeDataset(), path, new ReportExportOptions());
                var doc = XDocument.Load(path);
                var vcItems = doc.Descendants("VCItem").ToList();
                vcItems.Should().HaveCount(1);
                vcItems[0].Element("IDEP").Should().NotBeNull();
                vcItems[0].Element("Quantita")!.Value.Should().Contain("12.5");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
