using FluentAssertions;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace QtoRevitPlugin.Tests.Sprint9
{
    public class ComputoChapterRepositoryTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly QtoRepository _repo;
        private readonly int _sessionId;

        public ComputoChapterRepositoryTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"ch_test_{Guid.NewGuid()}.db");
            _repo = new QtoRepository(_dbPath);
            _sessionId = _repo.InsertSession(new WorkSession { ProjectPath = "p.rvt", ProjectName = "p" });
        }

        public void Dispose()
        {
            _repo.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Fact]
        public void Insert_AssignsId_AndRoundtrips()
        {
            var ch = new ComputoChapter
            {
                SessionId = _sessionId,
                Code = "01",
                Name = "DEMOLIZIONI",
                Level = 1,
                SortOrder = 0,
                CreatedAt = DateTime.UtcNow
            };
            var id = _repo.InsertComputoChapter(ch);
            id.Should().BeGreaterThan(0);

            var list = _repo.GetComputoChapters(_sessionId);
            list.Should().ContainSingle(c => c.Code == "01" && c.Name == "DEMOLIZIONI");
        }

        [Fact]
        public void Update_PersistsChanges()
        {
            var ch = new ComputoChapter
            {
                SessionId = _sessionId, Code = "02", Name = "Vecchio", Level = 1, CreatedAt = DateTime.UtcNow
            };
            ch.Id = _repo.InsertComputoChapter(ch);
            ch.Name = "Nuovo";
            _repo.UpdateComputoChapter(ch);

            var fetched = _repo.GetComputoChapters(_sessionId).First(c => c.Id == ch.Id);
            fetched.Name.Should().Be("Nuovo");
        }

        [Fact]
        public void Delete_WithAssignments_SetsAssignmentChapterIdToNull()
        {
            var ch = new ComputoChapter
            {
                SessionId = _sessionId, Code = "03", Name = "Test", Level = 1, CreatedAt = DateTime.UtcNow
            };
            ch.Id = _repo.InsertComputoChapter(ch);

            var assignment = new QtoAssignment
            {
                SessionId = _sessionId, ElementId = 42, UniqueId = "uid-1",
                EpCode = "EP1", Quantity = 1, Unit = "m", UnitPrice = 10,
                ComputoChapterId = ch.Id, CreatedBy = "test", CreatedAt = DateTime.UtcNow,
                AuditStatus = AssignmentStatus.Active, Version = 1
            };
            var assignmentId = _repo.InsertAssignment(assignment);

            _repo.DeleteComputoChapter(ch.Id);

            var updated = _repo.GetAssignments(_sessionId).First(a => a.Id == assignmentId);
            updated.ComputoChapterId.Should().BeNull();
        }

        [Fact]
        public void GetForSession_ReturnsOrderedBySortOrder()
        {
            _repo.InsertComputoChapter(new ComputoChapter { SessionId = _sessionId, Code = "10", Name = "C", Level = 1, SortOrder = 2, CreatedAt = DateTime.UtcNow });
            _repo.InsertComputoChapter(new ComputoChapter { SessionId = _sessionId, Code = "11", Name = "A", Level = 1, SortOrder = 0, CreatedAt = DateTime.UtcNow });
            _repo.InsertComputoChapter(new ComputoChapter { SessionId = _sessionId, Code = "12", Name = "B", Level = 1, SortOrder = 1, CreatedAt = DateTime.UtcNow });

            var list = _repo.GetComputoChapters(_sessionId).ToList();
            list.Select(c => c.Name).Should().ContainInOrder("A", "B", "C");
        }
    }
}
