using FluentAssertions;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace QtoRevitPlugin.Tests.Sprint10
{
    public class SoaCategoriesSeedTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly QtoRepository _repo;

        public SoaCategoriesSeedTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"soa_test_{Guid.NewGuid()}.db");
            _repo = new QtoRepository(_dbPath);
        }

        public void Dispose()
        {
            _repo.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Fact]
        public void SeedOnNewDb_ContainsAllOGCategories()
        {
            var all = _repo.GetSoaCategories();
            var og = all.Where(c => c.Type == "OG").ToList();
            og.Count.Should().Be(13, "D.Lgs. 36/2023 All. II.12 definisce OG 1..13");
            og.Select(c => c.Code).Should().Contain(new[] { "OG 1", "OG 11", "OG 13" });
        }

        [Fact]
        public void SeedOnNewDb_ContainsOSCategoriesWithSubcodes()
        {
            var all = _repo.GetSoaCategories();
            var os = all.Where(c => c.Type == "OS").ToList();
            os.Count.Should().BeGreaterThan(35, "OS include anche sottocodici come OS 2-A, OS 12-B, OS 18-A");
            os.Select(c => c.Code).Should().Contain(new[] { "OS 1", "OS 2-A", "OS 12-A", "OS 18-B", "OS 28", "OS 35" });
        }

        [Fact]
        public void SortOrder_OGComesBeforeOS()
        {
            var all = _repo.GetSoaCategories();
            var first13 = all.Take(13).Select(c => c.Type).Distinct().ToList();
            first13.Should().ContainSingle().Which.Should().Be("OG");
        }

        [Fact]
        public void ComputoChapter_SoaCategoryId_RoundtripsNullAndValue()
        {
            var sessionId = _repo.InsertSession(new WorkSession { ProjectPath = "p.rvt", ProjectName = "p" });
            var soa = _repo.GetSoaCategories().First(c => c.Code == "OG 1");

            // Insert senza SOA
            var ch = new ComputoChapter
            {
                SessionId = sessionId, Code = "01", Name = "Test", Level = 1, CreatedAt = DateTime.UtcNow
            };
            ch.Id = _repo.InsertComputoChapter(ch);
            var reloaded1 = _repo.GetComputoChapters(sessionId).Single();
            reloaded1.SoaCategoryId.Should().BeNull();

            // Update con SOA
            reloaded1.SoaCategoryId = soa.Id;
            _repo.UpdateComputoChapter(reloaded1);

            var reloaded2 = _repo.GetComputoChapters(sessionId).Single();
            reloaded2.SoaCategoryId.Should().Be(soa.Id);
        }
    }
}
