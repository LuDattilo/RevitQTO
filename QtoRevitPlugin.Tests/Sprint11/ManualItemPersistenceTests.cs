using FluentAssertions;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace QtoRevitPlugin.Tests.Sprint11
{
    public class ManualItemPersistenceTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly QtoRepository _repo;
        private readonly int _sessionId;

        public ManualItemPersistenceTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"mi_{Guid.NewGuid():N}.db");
            _repo = new QtoRepository(_dbPath);
            _sessionId = _repo.InsertSession(new WorkSession { ProjectPath = "p.rvt", ProjectName = "p" });
        }

        public void Dispose() { _repo.Dispose(); if (File.Exists(_dbPath)) File.Delete(_dbPath); }

        [Fact]
        public void InsertAndGet_ManualItem_RoundTrips()
        {
            var item = new ManualQuantityEntry { SessionId = _sessionId, EpCode = "X.01", EpDescription = "Test", Unit = "mq", Quantity = 10.5, UnitPrice = 20.0, Notes = "nota" };
            var id = _repo.InsertManualItem(item);
            id.Should().BeGreaterThan(0);
            var loaded = _repo.GetManualItems(_sessionId);
            loaded.Should().ContainSingle();
            loaded[0].EpCode.Should().Be("X.01");
            loaded[0].Quantity.Should().BeApproximately(10.5, 0.001);
        }

        [Fact]
        public void UpdateManualItem_ChangesQuantityAndPrice()
        {
            var id = _repo.InsertManualItem(new ManualQuantityEntry { SessionId = _sessionId, EpCode = "Y.01", EpDescription = "D", Unit = "ml", Quantity = 5.0, UnitPrice = 10.0, Notes = "" });
            var item = _repo.GetManualItems(_sessionId).First(m => m.Id == id);
            item.Quantity = 99.9;
            item.UnitPrice = 50.0;
            _repo.UpdateManualItem(item);
            var reloaded = _repo.GetManualItems(_sessionId).First(m => m.Id == id);
            reloaded.Quantity.Should().BeApproximately(99.9, 0.001);
            reloaded.UnitPrice.Should().BeApproximately(50.0, 0.001);
        }

        [Fact]
        public void DeleteManualItem_SoftDeletesRow()
        {
            var id = _repo.InsertManualItem(new ManualQuantityEntry { SessionId = _sessionId, EpCode = "Z.01", EpDescription = "D", Unit = "mq", Quantity = 1.0, UnitPrice = 1.0, Notes = "" });
            _repo.DeleteManualItem(id);
            _repo.GetManualItems(_sessionId).Should().BeEmpty();
        }
    }
}
