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
    /// Test per XpweExporter conforme allo schema ACCA PriMus validato contro
    /// CME_Sample.xpwe (computo Lupi Student Hall). Verifica struttura attesa da
    /// import PriMus: root, header, tabelle Categorie, EPItem, VCItem con RGItem.
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
                    Titolo = "Computo test", Committente = "Cliente Test", DirettoreLavori = "DL Test",
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
            var path = Path.Combine(Path.GetTempPath(), $"xpwe_{Guid.NewGuid()}.xpwe");
            try
            {
                new XpweExporter().Export(MakeDataset(), path, new ReportExportOptions());
                File.Exists(path).Should().BeTrue();

                var doc = XDocument.Load(path);
                doc.Root!.Name.LocalName.Should().Be("PweDocumento");
                doc.Root.Name.NamespaceName.Should().BeEmpty();
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Export_HasMsoApplicationProcessingInstruction()
        {
            var path = Path.Combine(Path.GetTempPath(), $"xpwe_pi_{Guid.NewGuid()}.xpwe");
            try
            {
                new XpweExporter().Export(MakeDataset(), path, new ReportExportOptions());
                var doc = XDocument.Load(path);
                var pi = doc.Nodes().OfType<XProcessingInstruction>().FirstOrDefault();
                pi.Should().NotBeNull();
                pi!.Target.Should().Be("mso-application");
                pi.Data.Should().Contain("PriMus.Document.XPWE");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Export_HasAccaHeaderFields()
        {
            var path = Path.Combine(Path.GetTempPath(), $"xpwe_hdr_{Guid.NewGuid()}.xpwe");
            try
            {
                new XpweExporter().Export(MakeDataset(), path, new ReportExportOptions());
                var doc = XDocument.Load(path);
                var root = doc.Root!;
                root.Element("CopyRight")!.Value.Should().Contain("ACCA software");
                root.Element("TipoDocumento")!.Value.Should().Be("1");   // 1 = Computo
                root.Element("TipoFormato")!.Value.Should().Be("XMLPwe");
                root.Element("Versione")!.Value.Should().Be("5.04");
                root.Element("FileNameDocumento").Should().NotBeNull();
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Export_DatiGenerali_HasProjectInfo()
        {
            var path = Path.Combine(Path.GetTempPath(), $"xpwe_proj_{Guid.NewGuid()}.xpwe");
            try
            {
                new XpweExporter().Export(MakeDataset(), path, new ReportExportOptions());
                var doc = XDocument.Load(path);
                var dg = doc.Descendants("PweDGDatiGenerali").Single();
                dg.Element("Oggetto")!.Value.Should().Be("Computo test");
                dg.Element("Committente")!.Value.Should().Be("Cliente Test");
                dg.Element("Impresa").Should().NotBeNull();
                dg.Element("PercPrezzi").Should().NotBeNull();
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Export_ChaptersAreOnCategorieAxis_NotCapitoli()
        {
            var path = Path.Combine(Path.GetTempPath(), $"xpwe_cat_{Guid.NewGuid()}.xpwe");
            try
            {
                new XpweExporter().Export(MakeDataset(), path, new ReportExportOptions());
                var doc = XDocument.Load(path);

                // I nostri ComputoChapter vanno sulle Categorie PriMus, NON sui Capitoli.
                doc.Descendants("DGSuperCategorieItem").Should().HaveCount(1);
                doc.Descendants("DGCategorieItem").Should().HaveCount(1);
                doc.Descendants("DGSubCategorieItem").Should().HaveCount(1);

                // I Capitoli hanno solo un SuperCapitolo placeholder (documento sorgente)
                doc.Descendants("DGSuperCapitoliItem").Should().HaveCount(1);
                doc.Descendants("DGCapitoliItem").Should().BeEmpty();
                doc.Descendants("DGSubCapitoliItem").Should().BeEmpty();
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Export_EPItem_HasAccaFields()
        {
            var path = Path.Combine(Path.GetTempPath(), $"xpwe_ep_{Guid.NewGuid()}.xpwe");
            try
            {
                new XpweExporter().Export(MakeDataset(), path, new ReportExportOptions());
                var doc = XDocument.Load(path);
                var epItem = doc.Descendants("EPItem").Single();

                epItem.Attribute("ID").Should().NotBeNull();
                epItem.Element("TipoEP")!.Value.Should().Be("0");
                epItem.Element("Tariffa")!.Value.Should().Be("TOS25.A03.001");
                epItem.Element("Articolo")!.Value.Should().Be("TOS25.A03.001");
                epItem.Element("UnMisura")!.Value.Should().Be("m³");
                epItem.Element("Prezzo1")!.Value.Should().Contain("85.30");
                epItem.Element("Prezzo2")!.Value.Should().Be("0");
                epItem.Element("IDSpCap").Should().NotBeNull();
                epItem.Element("IncSIC").Should().NotBeNull();
                epItem.Element("TagBIM").Should().NotBeNull();
                epItem.Element("PweEPAnalisi").Should().NotBeNull();
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Export_VCItem_ReferencesEPItemViaIDEP_AndHasRGMisura()
        {
            var path = Path.Combine(Path.GetTempPath(), $"xpwe_vc_{Guid.NewGuid()}.xpwe");
            try
            {
                new XpweExporter().Export(MakeDataset(), path, new ReportExportOptions());
                var doc = XDocument.Load(path);

                // Schema VCItem reale: FK a EPItem + misurazioni annidate in PweVCMisure
                var vcItem = doc.Descendants("VCItem").Single();
                vcItem.Attribute("ID").Should().NotBeNull();
                vcItem.Element("IDEP").Should().NotBeNull();  // FK a EPItem
                vcItem.Element("Quantita")!.Value.Should().Contain("12.5");
                vcItem.Element("DataMis").Should().NotBeNull();

                // Category FK (asse Categorie, non Capitoli)
                vcItem.Element("IDSpCat").Should().NotBeNull();
                vcItem.Element("IDCat").Should().NotBeNull();
                vcItem.Element("IDSbCat").Should().NotBeNull();

                // PweVCMisure contiene RGItem (riga misurazione)
                var rg = vcItem.Descendants("RGItem").Single();
                rg.Element("IDVV")!.Value.Should().Be("-2");
                rg.Element("PartiUguali")!.Value.Should().Contain("12.5");
                rg.Element("Quantita")!.Value.Should().Contain("12.5");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Export_VCItem_IDCategoriesMatchIndexedValues()
        {
            var path = Path.Combine(Path.GetTempPath(), $"xpwe_fk_{Guid.NewGuid()}.xpwe");
            try
            {
                new XpweExporter().Export(MakeDataset(), path, new ReportExportOptions());
                var doc = XDocument.Load(path);

                // La voce è in SubCat dentro Cat dentro Super → dovrebbe avere tutti i 3 FK
                var vcItem = doc.Descendants("VCItem").Single();
                int.Parse(vcItem.Element("IDSpCat")!.Value).Should().Be(1);
                int.Parse(vcItem.Element("IDCat")!.Value).Should().Be(1);
                int.Parse(vcItem.Element("IDSbCat")!.Value).Should().Be(1);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
