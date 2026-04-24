using FluentAssertions;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace QtoRevitPlugin.Tests.Sprint10
{
    public class ComputoStructureMoveTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly QtoRepository _repo;
        private readonly int _sessionId;

        public ComputoStructureMoveTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"move_test_{Guid.NewGuid()}.db");
            _repo = new QtoRepository(_dbPath);
            _sessionId = _repo.InsertSession(new WorkSession { ProjectPath = "p.rvt", ProjectName = "p" });
        }

        public void Dispose()
        {
            _repo.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Fact]
        public void SwapSortOrder_BetweenSiblings_ReordersReliably()
        {
            var a = new ComputoChapter { SessionId = _sessionId, Code = "01", Name = "A", Level = 1, SortOrder = 0, CreatedAt = DateTime.UtcNow };
            var b = new ComputoChapter { SessionId = _sessionId, Code = "02", Name = "B", Level = 1, SortOrder = 1, CreatedAt = DateTime.UtcNow };
            a.Id = _repo.InsertComputoChapter(a);
            b.Id = _repo.InsertComputoChapter(b);

            var tmp = a.SortOrder;
            a.SortOrder = b.SortOrder;
            b.SortOrder = tmp;
            _repo.UpdateComputoChapter(a);
            _repo.UpdateComputoChapter(b);

            var list = _repo.GetComputoChapters(_sessionId).ToList();
            list.First(c => c.Id == a.Id).SortOrder.Should().Be(1);
            list.First(c => c.Id == b.Id).SortOrder.Should().Be(0);
        }

        [Fact]
        public void GetComputoChapters_OrderedBySortOrder_AfterSwap()
        {
            var first = new ComputoChapter { SessionId = _sessionId, Code = "01", Name = "First", Level = 1, SortOrder = 0, CreatedAt = DateTime.UtcNow };
            var second = new ComputoChapter { SessionId = _sessionId, Code = "02", Name = "Second", Level = 1, SortOrder = 1, CreatedAt = DateTime.UtcNow };
            first.Id = _repo.InsertComputoChapter(first);
            second.Id = _repo.InsertComputoChapter(second);

            // Sposta "Second" prima di "First": swap SortOrder
            first.SortOrder = 1;
            second.SortOrder = 0;
            _repo.UpdateComputoChapter(first);
            _repo.UpdateComputoChapter(second);

            var list = _repo.GetComputoChapters(_sessionId).OrderBy(c => c.SortOrder).ToList();
            list[0].Name.Should().Be("Second");
            list[1].Name.Should().Be("First");
        }
    }
}
