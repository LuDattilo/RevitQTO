using FluentAssertions;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace QtoRevitPlugin.Tests.Computo
{
    public class ManualItemsRepositoryTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly QtoRepository _repo;
        private readonly int _sessionId;

        public ManualItemsRepositoryTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"manual_{Guid.NewGuid():N}.db");
            _repo = new QtoRepository(_dbPath);
            _sessionId = _repo.InsertSession(new WorkSession
            {
                ProjectPath = "t.rvt", SessionName = "t", CreatedAt = DateTime.UtcNow
            });
        }

        public void Dispose()
        {
            _repo.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Fact]
        public void GetManualItems_EmptyByDefault()
        {
            _repo.GetManualItems(_sessionId).Should().BeEmpty();
        }

        [Fact]
        public void InsertManualItem_RoundTripsAllFields()
        {
            var item = new ManualQuantityEntry
            {
                SessionId = _sessionId,
                EpCode = "OS.001",
                EpDescription = "Oneri sicurezza COVID",
                Quantity = 1,
                Unit = "cad",
                UnitPrice = 1200,
                Notes = "Verbale 12/03/2026",
                AttachmentPath = "C:/verbali/verbale_120326.pdf",
                CreatedBy = "mario.rossi"
            };

            var id = _repo.InsertManualItem(item);
            id.Should().BeGreaterThan(0);

            var loaded = _repo.GetManualItems(_sessionId).Single();
            loaded.EpCode.Should().Be("OS.001");
            loaded.EpDescription.Should().Be("Oneri sicurezza COVID");
            loaded.Quantity.Should().Be(1);
            loaded.Unit.Should().Be("cad");
            loaded.UnitPrice.Should().Be(1200);
            loaded.Total.Should().Be(1200); // computed
            loaded.Notes.Should().Be("Verbale 12/03/2026");
            loaded.AttachmentPath.Should().Be("C:/verbali/verbale_120326.pdf");
            loaded.CreatedBy.Should().Be("mario.rossi");
            loaded.IsDeleted.Should().BeFalse();
        }

        [Fact]
        public void UpdateManualItem_ChangesQuantityAndSetsModifiedAt()
        {
            var item = new ManualQuantityEntry
            {
                SessionId = _sessionId, EpCode = "X.1", Quantity = 1, UnitPrice = 10
            };
            item.Id = _repo.InsertManualItem(item);

            item.Quantity = 5;
            _repo.UpdateManualItem(item);

            var loaded = _repo.GetManualItems(_sessionId).Single();
            loaded.Quantity.Should().Be(5);
            loaded.ModifiedAt.Should().NotBeNull();
        }

        [Fact]
        public void DeleteManualItem_IsSoftDelete()
        {
            var id = _repo.InsertManualItem(new ManualQuantityEntry
            {
                SessionId = _sessionId, EpCode = "Z.1", Quantity = 1
            });
            _repo.GetManualItems(_sessionId).Should().HaveCount(1);

            _repo.DeleteManualItem(id);

            // GetManualItems filtra IsDeleted=0 → non la vediamo più
            _repo.GetManualItems(_sessionId).Should().BeEmpty();

            // Ma la riga è ancora nel DB (audit trail): verifico via SELECT diretto
            // Nota: il repo non espone un "GetAllIncludingDeleted" — test-only lookup qui.
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection(
                $"Data Source={_dbPath};Pooling=False");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT IsDeleted FROM ManualItems WHERE Id = @Id;";
            cmd.Parameters.AddWithValue("@Id", id);
            var isDeleted = (long)cmd.ExecuteScalar()!;
            isDeleted.Should().Be(1);
        }

        [Fact]
        public void GetManualItems_OrderedByEpCode()
        {
            _repo.InsertManualItem(new ManualQuantityEntry { SessionId = _sessionId, EpCode = "B", Quantity = 1 });
            _repo.InsertManualItem(new ManualQuantityEntry { SessionId = _sessionId, EpCode = "A", Quantity = 1 });
            _repo.InsertManualItem(new ManualQuantityEntry { SessionId = _sessionId, EpCode = "C", Quantity = 1 });

            _repo.GetManualItems(_sessionId)
                .Select(m => m.EpCode)
                .Should().ContainInOrder("A", "B", "C");
        }

        [Fact]
        public void SessionIsolation_OtherSessionNotVisible()
        {
            var otherId = _repo.InsertSession(new WorkSession
            {
                ProjectPath = "o.rvt", SessionName = "o", CreatedAt = DateTime.UtcNow
            });
            _repo.InsertManualItem(new ManualQuantityEntry { SessionId = _sessionId, EpCode = "X", Quantity = 1 });
            _repo.InsertManualItem(new ManualQuantityEntry { SessionId = otherId, EpCode = "Y", Quantity = 1 });

            _repo.GetManualItems(_sessionId).Should().ContainSingle().Which.EpCode.Should().Be("X");
            _repo.GetManualItems(otherId).Should().ContainSingle().Which.EpCode.Should().Be("Y");
        }
    }
}
