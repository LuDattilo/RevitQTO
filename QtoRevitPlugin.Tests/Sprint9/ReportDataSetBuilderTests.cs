using FluentAssertions;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Reports;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace QtoRevitPlugin.Tests.Sprint9
{
    public class ReportDataSetBuilderTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly QtoRepository _repo;
        private readonly int _sessionId;

        public ReportDataSetBuilderTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"rpt_test_{Guid.NewGuid()}.db");
            _repo = new QtoRepository(_dbPath);
            _sessionId = _repo.InsertSession(new WorkSession { ProjectPath = "p.rvt", ProjectName = "p" });
        }

        public void Dispose()
        {
            _repo.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Fact]
        public void Build_FiltersActiveOnly_ByDefault()
        {
            _repo.InsertAssignment(MakeAssignment("uid-a", "EP1", 10, AssignmentStatus.Active));
            _repo.InsertAssignment(MakeAssignment("uid-b", "EP2", 5, AssignmentStatus.Superseded));
            _repo.InsertAssignment(MakeAssignment("uid-c", "EP3", 8, AssignmentStatus.Deleted));

            var builder = new ReportDataSetBuilder(_repo);
            var ds = builder.Build(_sessionId, new ReportExportOptions());

            ds.UnchaperedEntries.Should().HaveCount(1);
            ds.UnchaperedEntries.Single().EpCode.Should().Be("EP1");
        }

        [Fact]
        public void Build_GroupsByComputoChapter_ThreeLevels()
        {
            var super = new ComputoChapter { SessionId = _sessionId, Code = "01", Name = "SUPER", Level = 1, CreatedAt = DateTime.UtcNow };
            super.Id = _repo.InsertComputoChapter(super);
            var cat = new ComputoChapter { SessionId = _sessionId, ParentChapterId = super.Id, Code = "01.A", Name = "CAT", Level = 2, CreatedAt = DateTime.UtcNow };
            cat.Id = _repo.InsertComputoChapter(cat);
            var sub = new ComputoChapter { SessionId = _sessionId, ParentChapterId = cat.Id, Code = "01.A.01", Name = "SUB", Level = 3, CreatedAt = DateTime.UtcNow };
            sub.Id = _repo.InsertComputoChapter(sub);

            var a = MakeAssignment("uid-1", "EP1", 10, AssignmentStatus.Active);
            a.ComputoChapterId = sub.Id;
            _repo.InsertAssignment(a);

            var builder = new ReportDataSetBuilder(_repo);
            var ds = builder.Build(_sessionId, new ReportExportOptions());

            ds.Chapters.Should().HaveCount(1);
            ds.Chapters[0].Chapter.Code.Should().Be("01");
            ds.Chapters[0].Children.Should().HaveCount(1);
            ds.Chapters[0].Children[0].Chapter.Code.Should().Be("01.A");
            ds.Chapters[0].Children[0].Children.Should().HaveCount(1);
            ds.Chapters[0].Children[0].Children[0].Chapter.Code.Should().Be("01.A.01");
            ds.Chapters[0].Children[0].Children[0].Entries.Should().HaveCount(1);
        }

        [Fact]
        public void Build_CalculatesSubtotalsAndGrandTotalCorrectly()
        {
            // Total è computed (Quantity * UnitPrice): 10*20=200 + 5*10=50 = 250
            var a1 = MakeAssignment("uid-1", "EP1", 10, AssignmentStatus.Active);
            a1.UnitPrice = 20;
            _repo.InsertAssignment(a1);
            var a2 = MakeAssignment("uid-2", "EP2", 5, AssignmentStatus.Active);
            a2.UnitPrice = 10;
            _repo.InsertAssignment(a2);

            var builder = new ReportDataSetBuilder(_repo);
            var ds = builder.Build(_sessionId, new ReportExportOptions());

            ds.GrandTotal.Should().Be(250m);
        }

        private QtoAssignment MakeAssignment(string uid, string ep, double qty, AssignmentStatus status)
        {
            return new QtoAssignment
            {
                SessionId = _sessionId, ElementId = 1, UniqueId = uid,
                EpCode = ep, EpDescription = ep + " desc", Quantity = qty,
                Unit = "m²", UnitPrice = 10,   // Total è computed: Quantity * UnitPrice
                Source = QtoSource.RevitElement, CreatedBy = "t", CreatedAt = DateTime.UtcNow,
                AuditStatus = status, Version = 1
            };
        }
    }
}
