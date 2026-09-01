using System.Collections.Generic;
using System.Linq;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Models.Computi;
using QtoRevitPlugin.Services.Computi;
using Xunit;

namespace QtoRevitPlugin.Tests.Computo
{
    /// <summary>
    /// Test del proiettore MeasurementRow → QtoAssignment (per la riconciliazione Health):
    /// granularità RGItem, EpCode/prezzo dal PriceItem, UniqueId dall'IDVV.
    /// </summary>
    public sealed class MeasurementToAssignmentProjectorTests
    {
        private static PriceItem Pi(int id, string code, double price) =>
            new PriceItem { Id = id, Code = code, Tariffa = code, UnitPrice = price, Unit = "m2", Description = "d" + code };

        private static MeasurementRow Vc(int id, int priceItemId, double qta) =>
            new MeasurementRow { Id = id, DocumentId = 1, PriceItemId = priceItemId, Quantita = qta };

        private static MeasurementSubRow Rg(int id, int rowId, int idvv, double qta,
            string? category = null, string? familyName = null) =>
            new MeasurementSubRow
            {
                Id = id, MeasurementRowId = rowId, IDVV = idvv, Quantita = qta, PartiUguali = qta,
                Category = category, FamilyName = familyName,
            };

        [Fact]
        public void Project_oneAssignmentPerSubRow_withCodeAndQuantity()
        {
            var rows = new List<MeasurementRow> { Vc(1, 10, 5.0) };
            var prices = new Dictionary<int, PriceItem> { [10] = Pi(10, "A", 20.0) };
            var subs = new Dictionary<int, IReadOnlyList<MeasurementSubRow>>
            {
                [1] = new List<MeasurementSubRow> { Rg(1, 1, 111, 2.0), Rg(2, 1, 222, 3.0) },
            };

            var a = MeasurementToAssignmentProjector.Project(7, rows, subs, prices);

            Assert.Equal(2, a.Count);
            Assert.All(a, x => Assert.Equal("A", x.EpCode));
            Assert.All(a, x => Assert.Equal(7, x.SessionId));
            Assert.Contains(a, x => x.ElementId == 111 && x.Quantity == 2.0);
            Assert.Contains(a, x => x.ElementId == 222 && x.Quantity == 3.0);
            Assert.Equal(40.0, a.First(x => x.ElementId == 111).Total, 2);  // 2 * 20
        }

        [Fact]
        public void Project_propagatesCategoryAndFamily_forSemanticMismatch()
        {
            var rows = new List<MeasurementRow> { Vc(1, 10, 1.0) };
            var prices = new Dictionary<int, PriceItem> { [10] = Pi(10, "A", 1.0) };
            var subs = new Dictionary<int, IReadOnlyList<MeasurementSubRow>>
            {
                [1] = new List<MeasurementSubRow> { Rg(1, 1, 111, 1.0, category: "Muri", familyName: "Muro di base") },
            };

            var a = MeasurementToAssignmentProjector.Project(1, rows, subs, prices);

            Assert.Equal("Muri", a[0].Category);
            Assert.Equal("Muro di base", a[0].FamilyName);
        }

        [Fact]
        public void Project_rowWithoutSubRows_fallsBackToAggregateQuantity()
        {
            var rows = new List<MeasurementRow> { Vc(1, 10, 9.0) };
            var prices = new Dictionary<int, PriceItem> { [10] = Pi(10, "A", 1.0) };
            var a = MeasurementToAssignmentProjector.Project(
                1, rows, new Dictionary<int, IReadOnlyList<MeasurementSubRow>>(), prices);
            Assert.Single(a);
            Assert.Equal(9.0, a[0].Quantity, 2);
        }

        [Fact]
        public void Project_manualNegativeIdvv_getsSyntheticUniqueId_noCollision()
        {
            var rows = new List<MeasurementRow> { Vc(1, 10, 1.0) };
            var prices = new Dictionary<int, PriceItem> { [10] = Pi(10, "A", 1.0) };
            var subs = new Dictionary<int, IReadOnlyList<MeasurementSubRow>>
            {
                [1] = new List<MeasurementSubRow> { Rg(1, 1, -1, 1.0), Rg(2, 1, -2, 1.0) },
            };
            var a = MeasurementToAssignmentProjector.Project(1, rows, subs, prices);
            Assert.Equal(2, a.Count);
            Assert.Equal(2, a.Select(x => x.UniqueId).Distinct().Count());  // UniqueId distinti
            Assert.All(a, x => Assert.Equal(0, x.ElementId));
        }
    }
}
