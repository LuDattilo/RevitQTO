using FluentAssertions;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using System.IO;
using System.Linq;
using Xunit;

namespace QtoRevitPlugin.Tests.Listino
{
    public class PriceListActiveToggleTests : System.IDisposable
    {
        private readonly string _dbPath;
        private readonly QtoRepository _repo;

        public PriceListActiveToggleTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"cme_active_{System.Guid.NewGuid():N}.db");
            _repo = new QtoRepository(_dbPath);
        }

        public void Dispose()
        {
            _repo.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Fact]
        public void UpdatePriceListFlags_DeactivatesList_ExcludesFromActiveItemsQuery()
        {
            // 2 listini, 1 voce ognuno
            var pl1 = new PriceList { Name = "L1", IsActive = true };
            var pl2 = new PriceList { Name = "L2", IsActive = true };
            _repo.InsertPriceList(pl1);  // sets pl1.Id as side-effect
            _repo.InsertPriceList(pl2);  // sets pl2.Id as side-effect
            _repo.InsertPriceItemsBatch(pl1.Id, new[]
            {
                new PriceItem { Code = "A1", Description = "x", Unit = "m", UnitPrice = 1 }
            });
            _repo.InsertPriceItemsBatch(pl2.Id, new[]
            {
                new PriceItem { Code = "A2", Description = "y", Unit = "m", UnitPrice = 2 }
            });

            _repo.GetAllActivePriceItems().Should().HaveCount(2);

            // Disattiva L2
            _repo.UpdatePriceListFlags(pl2.Id, isActive: false, priority: 0);

            _repo.GetAllActivePriceItems()
                .Should().ContainSingle()
                .Which.Code.Should().Be("A1");
        }

        [Fact]
        public void UpdatePriceListFlags_DeactivatesList_ExcludesFromCodeExactQuery()
        {
            var pl = new PriceList { Name = "L1", IsActive = true };
            _repo.InsertPriceList(pl);
            _repo.InsertPriceItemsBatch(pl.Id, new[]
            {
                new PriceItem { Code = "X42", Description = "x", Unit = "m", UnitPrice = 1 }
            });

            _repo.FindByCodeExact("X42").Should().NotBeEmpty();

            _repo.UpdatePriceListFlags(pl.Id, isActive: false, priority: 0);

            _repo.FindByCodeExact("X42").Should().BeEmpty();
        }
    }
}
