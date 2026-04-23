using FluentAssertions;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Services;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace QtoRevitPlugin.Tests.Computo
{
    public class MultiSourceAggregatorTests
    {
        [Fact]
        public void Aggregate_EmptyInputs_ReturnsEmpty()
        {
            var result = MultiSourceAggregator.Aggregate(
                new List<QtoAssignment>(),
                new List<ManualQuantityEntry>());
            result.Should().BeEmpty();
        }

        [Fact]
        public void Aggregate_NullInputs_NoThrow()
        {
            var result = MultiSourceAggregator.Aggregate(null!, null!);
            result.Should().BeEmpty();
        }

        [Fact]
        public void Aggregate_OnlyAssignments_SumsQuantitiesPerEpCode()
        {
            var assignments = new[]
            {
                NewAssignment("EP1", quantity: 10, price: 50),
                NewAssignment("EP1", quantity: 5, price: 50),
                NewAssignment("EP2", quantity: 20, price: 100),
            };

            var result = MultiSourceAggregator.Aggregate(assignments, null!).ToList();

            result.Should().HaveCount(2);
            var ep1 = result.Single(e => e.EpCode == "EP1");
            ep1.QuantityFromModel.Should().Be(15);
            ep1.TotalFromModel.Should().Be(750);
            ep1.QuantityFromManual.Should().Be(0);
            ep1.HasPriceConflict.Should().BeFalse();
        }

        [Fact]
        public void Aggregate_ModelAndManualSameEp_SumsWithoutPriority()
        {
            var assignments = new[]
            {
                NewAssignment("EP1", quantity: 10, price: 50)
            };
            var manual = new[]
            {
                NewManual("EP1", quantity: 3, unitPrice: 50, sessionId: 1)
            };

            var result = MultiSourceAggregator.Aggregate(assignments, manual).ToList();

            result.Should().ContainSingle();
            var ep1 = result[0];
            ep1.QuantityFromModel.Should().Be(10);
            ep1.QuantityFromManual.Should().Be(3);
            ep1.QuantityTotal.Should().Be(13);
            ep1.TotalFromModel.Should().Be(500);
            ep1.TotalFromManual.Should().Be(150);
            ep1.TotalAmount.Should().Be(650);
            ep1.HasPriceConflict.Should().BeFalse("prezzi uguali su entrambe le sorgenti");
        }

        [Fact]
        public void Aggregate_PriceMismatch_DetectsConflict()
        {
            var assignments = new[]
            {
                NewAssignment("EP1", quantity: 10, price: 50)
            };
            var manual = new[]
            {
                NewManual("EP1", quantity: 3, unitPrice: 55, sessionId: 1) // prezzo diverso
            };

            var result = MultiSourceAggregator.Aggregate(assignments, manual).Single();

            result.HasPriceConflict.Should().BeTrue();
            result.UnitPriceModel.Should().Be(50);
            result.UnitPriceManual.Should().Be(55);
            result.PriceConflictMessage.Should().Contain("50").And.Contain("55");
        }

        [Fact]
        public void Aggregate_IgnoresDeletedAssignments()
        {
            var assignments = new[]
            {
                NewAssignment("EP1", quantity: 10, price: 50),
                NewAssignment("EP1", quantity: 5, price: 50, isDeleted: true), // ignored
            };
            var result = MultiSourceAggregator.Aggregate(assignments, null!).Single();
            result.QuantityFromModel.Should().Be(10);
        }

        [Fact]
        public void Aggregate_IgnoresExcludedAssignments()
        {
            var assignments = new[]
            {
                NewAssignment("EP1", quantity: 10, price: 50),
                NewAssignment("EP1", quantity: 7, price: 50, isExcluded: true), // ignored
            };
            var result = MultiSourceAggregator.Aggregate(assignments, null!).Single();
            result.QuantityFromModel.Should().Be(10);
        }

        [Fact]
        public void Aggregate_IgnoresNonActiveAssignments()
        {
            var supersedes = NewAssignment("EP1", quantity: 3, price: 50);
            supersedes.AuditStatus = AssignmentStatus.Superseded;

            var deleted = NewAssignment("EP1", quantity: 5, price: 50);
            deleted.AuditStatus = AssignmentStatus.Deleted;

            var assignments = new[]
            {
                NewAssignment("EP1", quantity: 10, price: 50),
                supersedes,
                deleted
            };
            var result = MultiSourceAggregator.Aggregate(assignments, null!).Single();
            result.QuantityFromModel.Should().Be(10, "solo Active deve essere aggregato");
        }

        [Fact]
        public void Aggregate_IgnoresDeletedManualItems()
        {
            var manual = new[]
            {
                NewManual("EP1", quantity: 3, unitPrice: 50, sessionId: 1),
                NewManual("EP1", quantity: 10, unitPrice: 50, sessionId: 1, isDeleted: true)
            };
            var result = MultiSourceAggregator.Aggregate(null!, manual).Single();
            result.QuantityFromManual.Should().Be(3);
        }

        [Fact]
        public void Aggregate_EpCodeCaseInsensitive()
        {
            // Anche se QtoAssignment.EpCode è case-sensitive nel DB, l'aggregazione è case-insensitive
            // per coerenza con il listino prezzi (codici tipicamente upper-case ma non sempre).
            var assignments = new[]
            {
                NewAssignment("ep1", quantity: 5, price: 10),
                NewAssignment("EP1", quantity: 3, price: 10)
            };
            var result = MultiSourceAggregator.Aggregate(assignments, null!);
            result.Should().ContainSingle();
            result[0].QuantityFromModel.Should().Be(8);
        }

        [Fact]
        public void Aggregate_ModelPriceNonUniform_FlagsConflict()
        {
            var assignments = new[]
            {
                NewAssignment("EP1", quantity: 5, price: 50),
                NewAssignment("EP1", quantity: 3, price: 52)  // prezzo diverso nel modello
            };
            var result = MultiSourceAggregator.Aggregate(assignments, null!).Single();
            result.ModelPriceNonUniform.Should().BeTrue();
            result.HasPriceConflict.Should().BeTrue();
        }

        [Fact]
        public void Aggregate_ResultsSortedByEpCode()
        {
            var assignments = new[]
            {
                NewAssignment("ZZZ", 1, 1),
                NewAssignment("AAA", 1, 1),
                NewAssignment("MMM", 1, 1),
            };
            var result = MultiSourceAggregator.Aggregate(assignments, null!).ToList();
            result.Select(r => r.EpCode).Should().ContainInOrder("AAA", "MMM", "ZZZ");
        }

        // ────────── Helpers ──────────

        private static QtoAssignment NewAssignment(
            string epCode, double quantity, double price,
            bool isDeleted = false, bool isExcluded = false)
        {
            return new QtoAssignment
            {
                UniqueId = System.Guid.NewGuid().ToString(),
                EpCode = epCode,
                EpDescription = "desc-" + epCode,
                Quantity = quantity,
                UnitPrice = price,
                AuditStatus = AssignmentStatus.Active,
                IsDeleted = isDeleted,
                IsExcluded = isExcluded
            };
        }

        private static ManualQuantityEntry NewManual(
            string epCode, double quantity, double unitPrice, int sessionId,
            bool isDeleted = false)
        {
            return new ManualQuantityEntry
            {
                SessionId = sessionId,
                EpCode = epCode,
                EpDescription = "manual-" + epCode,
                Quantity = quantity,
                UnitPrice = unitPrice,
                IsDeleted = isDeleted
            };
        }
    }
}
