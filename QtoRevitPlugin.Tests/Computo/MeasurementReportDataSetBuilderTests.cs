using System.Collections.Generic;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Models.Computi;
using QtoRevitPlugin.Reports;
using Xunit;

namespace QtoRevitPlugin.Tests.Computo
{
    /// <summary>
    /// Test del builder di riconciliazione (Fase 0): ReportDataSet costruito dal modello Computi
    /// (MeasurementRow + PriceItem + CategoryNode) invece che da QtoAssignments. Verifica
    /// raggruppamento per categoria più profonda, subtotali/GrandTotal, e voci senza categoria.
    /// </summary>
    public sealed class MeasurementReportDataSetBuilderTests
    {
        private static PriceItem Pi(int id, string code, double price, string desc = "voce", string unit = "m2") =>
            new PriceItem { Id = id, Code = code, UnitPrice = price, Description = desc, Unit = unit };

        private static MeasurementRow Vc(int id, int priceItemId, double qta,
            int? sp = null, int? cat = null, int? sb = null, int sort = 0) =>
            new MeasurementRow
            {
                Id = id,
                DocumentId = 1,
                PriceItemId = priceItemId,
                Quantita = qta,
                SpCatId = sp,
                CatId = cat,
                SbCatId = sb,
                SortOrder = sort,
            };

        private static CategoryNode Cat(int id, string level, string codice, string des, int? parent = null, int sort = 0) =>
            new CategoryNode
            {
                Id = id,
                DocumentId = 1,
                Level = level,
                Codice = codice,
                DesSintetica = des,
                ParentId = parent,
                SortOrder = sort,
                IsActive = true,
            };

        [Fact]
        public void GroupsRows_underDeepestCategory_andComputesSubtotals()
        {
            var session = new WorkSession { Id = 1, SessionName = "test" };
            var cats = new List<CategoryNode>
            {
                Cat(1, "SpCat", "01", "Opere edili"),
                Cat(2, "Cat", "01.01", "Murature", parent: 1),
            };
            var prices = new Dictionary<int, PriceItem>
            {
                [10] = Pi(10, "A", 100.0),
                [20] = Pi(20, "B", 50.0),
            };
            var rows = new List<MeasurementRow>
            {
                Vc(1, 10, 2.0, sp: 1, cat: 2),   // 200 sotto Cat 2 (Murature)
                Vc(2, 20, 4.0, sp: 1),           // 200 sotto SpCat 1 (Opere edili)
            };

            var ds = MeasurementReportDataSetBuilder.BuildDataSet(session, new ReportHeader(), rows, prices, cats);

            Assert.Single(ds.Chapters);                       // una radice (SpCat 1)
            var root = ds.Chapters[0];
            Assert.Equal("Opere edili", root.Chapter.Name);
            Assert.Single(root.Children);                     // Cat 2
            Assert.Equal(200m, root.Children[0].Subtotal);    // Murature: 2*100
            Assert.Single(root.Entries);                      // voce B direttamente sotto SpCat
            Assert.Equal(200m, root.Entries[0].Total);        // 4*50
            Assert.Equal(400m, root.Subtotal);                // 200 (child) + 200 (entry)
            Assert.Equal(400m, ds.GrandTotal);
        }

        [Fact]
        public void RowsWithoutCategory_goToUnchaperedEntries()
        {
            var session = new WorkSession { Id = 1 };
            var prices = new Dictionary<int, PriceItem> { [10] = Pi(10, "A", 100.0) };
            var rows = new List<MeasurementRow> { Vc(1, 10, 3.0) };  // nessuna categoria

            var ds = MeasurementReportDataSetBuilder.BuildDataSet(
                session, new ReportHeader(), rows, prices, new List<CategoryNode>());

            Assert.Empty(ds.Chapters);
            Assert.Single(ds.UnchaperedEntries);
            Assert.Equal(300m, ds.UnchaperedEntries[0].Total);
            Assert.Equal(300m, ds.GrandTotal);
        }

        [Fact]
        public void OrphanPriceItem_yieldsZeroPricedEntry_notCrash()
        {
            var session = new WorkSession { Id = 1 };
            var rows = new List<MeasurementRow> { Vc(1, 999, 3.0) };  // FK prezzo mancante

            var ds = MeasurementReportDataSetBuilder.BuildDataSet(
                session, new ReportHeader(), rows, new Dictionary<int, PriceItem>(), new List<CategoryNode>());

            Assert.Single(ds.UnchaperedEntries);
            Assert.Equal("", ds.UnchaperedEntries[0].EpCode);
            Assert.Equal(0m, ds.UnchaperedEntries[0].Total);
        }

        [Fact]
        public void DanglingCategoryRef_fallsBackToUnchapered()
        {
            // La voce cita una SbCatId inesistente: non deve sparire, finisce fra le non-categorizzate.
            var session = new WorkSession { Id = 1 };
            var cats = new List<CategoryNode> { Cat(1, "SpCat", "01", "Opere") };
            var prices = new Dictionary<int, PriceItem> { [10] = Pi(10, "A", 10.0) };
            var rows = new List<MeasurementRow> { Vc(1, 10, 1.0, sb: 999) };

            var ds = MeasurementReportDataSetBuilder.BuildDataSet(session, new ReportHeader(), rows, prices, cats);

            Assert.Single(ds.UnchaperedEntries);
            Assert.Equal(10m, ds.GrandTotal);
        }
    }
}
