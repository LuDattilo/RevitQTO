using FluentAssertions;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using System.IO;
using System.Linq;
using Xunit;

namespace QtoRevitPlugin.Tests.Listino
{
    public class UserFavoritesRepositoryTests : System.IDisposable
    {
        private readonly string _dbPath;
        private readonly QtoRepository _repo;

        public UserFavoritesRepositoryTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"cme_fav_{System.Guid.NewGuid():N}.db");
            _repo = new QtoRepository(_dbPath);
        }

        public void Dispose()
        {
            _repo.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Fact]
        public void GetFavorites_EmptyByDefault()
        {
            _repo.GetFavorites().Should().BeEmpty();
        }

        [Fact]
        public void AddFavorite_InsertsRow()
        {
            _repo.AddFavorite(new UserFavorite
            {
                Code = "01.A01.001", Description = "Muro in laterizio",
                Unit = "m³", UnitPrice = 120.50, ListName = "Regione Toscana 2025", ListId = 1
            });

            var all = _repo.GetFavorites();
            all.Should().HaveCount(1);
            all[0].Code.Should().Be("01.A01.001");
            all[0].UnitPrice.Should().Be(120.50);
        }

        [Fact]
        public void AddFavorite_SameCodeAndListId_Idempotent()
        {
            var fav = new UserFavorite { Code = "A1", ListId = 1, Description = "x" };
            _repo.AddFavorite(fav);
            _repo.AddFavorite(fav); // second call: no-op thanks to UNIQUE(Code, ListId)

            _repo.GetFavorites().Should().HaveCount(1);
        }

        [Fact]
        public void AddFavorite_SameCodeDifferentList_Allowed()
        {
            _repo.AddFavorite(new UserFavorite { Code = "A1", ListId = 1 });
            _repo.AddFavorite(new UserFavorite { Code = "A1", ListId = 2 });

            _repo.GetFavorites().Should().HaveCount(2);
        }

        [Fact]
        public void RemoveFavorite_ByIdCorrectly()
        {
            _repo.AddFavorite(new UserFavorite { Code = "A1", ListId = 1 });
            var id = _repo.GetFavorites().Single().Id;

            _repo.RemoveFavorite(id);

            _repo.GetFavorites().Should().BeEmpty();
        }

        [Fact]
        public void IsFavorite_ReturnsTrueForExistingCode()
        {
            _repo.AddFavorite(new UserFavorite { Code = "Z99", ListId = 7 });
            _repo.IsFavorite("Z99", 7).Should().BeTrue();
            _repo.IsFavorite("Z99", 8).Should().BeFalse();
            _repo.IsFavorite("Z100", 7).Should().BeFalse();
        }

        [Fact]
        public void GetFavorites_OrderedByAddedAtDesc()
        {
            _repo.AddFavorite(new UserFavorite { Code = "First", ListId = 1, AddedAt = System.DateTime.UtcNow.AddMinutes(-10) });
            _repo.AddFavorite(new UserFavorite { Code = "Last", ListId = 1, AddedAt = System.DateTime.UtcNow });

            var all = _repo.GetFavorites();
            all[0].Code.Should().Be("Last");
            all[1].Code.Should().Be("First");
        }

        [Fact]
        public void IsFavorite_WithNullListId_WorksCorrectly()
        {
            _repo.AddFavorite(new UserFavorite { Code = "NULLED", ListId = null });

            _repo.IsFavorite("NULLED", null).Should().BeTrue();
            _repo.IsFavorite("NULLED", 1).Should().BeFalse();
            _repo.IsFavorite("OTHER", null).Should().BeFalse();
        }

        [Fact]
        public void AddFavorite_PersistsNoteField()
        {
            _repo.AddFavorite(new UserFavorite
            {
                Code = "NOTED",
                ListId = 1,
                Note = "Da verificare con DL prima dell'emissione"
            });

            var loaded = _repo.GetFavorites().Single();
            loaded.Note.Should().Be("Da verificare con DL prima dell'emissione");
        }

        [Fact]
        public void RemoveFavorites_Bulk_DeletesAllMatchingIds()
        {
            _repo.AddFavorite(new UserFavorite { Code = "A1", ListId = 1 });
            _repo.AddFavorite(new UserFavorite { Code = "A2", ListId = 1 });
            _repo.AddFavorite(new UserFavorite { Code = "A3", ListId = 1 });

            var all = _repo.GetFavorites().ToList();
            all.Count.Should().Be(3);

            // Rimuovi solo A1 e A3
            var idsToDelete = all.Where(f => f.Code != "A2").Select(f => f.Id).ToList();
            var deleted = _repo.RemoveFavorites(idsToDelete);

            deleted.Should().Be(2);
            var remaining = _repo.GetFavorites();
            remaining.Should().ContainSingle().Which.Code.Should().Be("A2");
        }

        [Fact]
        public void RemoveFavorites_EmptyIdList_NoOp()
        {
            _repo.AddFavorite(new UserFavorite { Code = "X", ListId = 1 });
            var deleted = _repo.RemoveFavorites(new int[0]);
            deleted.Should().Be(0);
            _repo.GetFavorites().Should().HaveCount(1);
        }

        [Fact]
        public void GetUsedEpCodes_OnDbWithoutQtoAssignmentsTable_ReturnsEmpty()
        {
            // UserLibrary.db non ha la tabella QtoAssignments — guard deve restituire
            // set vuoto invece di throw (scenario reale: VM chiama da panel Listino
            // ma repo corrente è UserLibrary).
            // NOTA: il repo di test è un .cme completo, quindi la tabella ESISTE.
            // Il test verifica il caso "session inesistente" via sessionId invalido.
            var used = _repo.GetUsedEpCodes(sessionId: 99999);
            used.Should().BeEmpty();
        }

        // ─── Test transazionalità AddFavorite (rev. fix race 2026-04-23) ───

        [Fact]
        public void AddFavorite_NewRow_ReturnsValidId()
        {
            var id = _repo.AddFavorite(new UserFavorite
            {
                Code = "A1", ListId = 1, Description = "x"
            });
            id.Should().BeGreaterThan(0, "una nuova riga deve avere un Id assegnato da AUTOINCREMENT");
            _repo.GetFavorites().Single().Id.Should().Be(id);
        }

        [Fact]
        public void AddFavorite_SameCodeAndListId_ReturnsStableId()
        {
            // Prima chiamata: nuova riga
            var id1 = _repo.AddFavorite(new UserFavorite { Code = "A1", ListId = 1 });
            // Seconda chiamata con stessa key: UNIQUE(Code, ListId) fa scattare IGNORE,
            // ma l'Id ritornato deve essere quello della riga preesistente.
            var id2 = _repo.AddFavorite(new UserFavorite { Code = "A1", ListId = 1 });

            id2.Should().Be(id1, "la seconda chiamata deve risolvere l'Id esistente via SELECT");
            _repo.GetFavorites().Should().HaveCount(1, "UNIQUE deve impedire duplicati");
        }

        [Fact]
        public void AddFavorite_SameCodeWithNullListId_CreatesMultipleRows()
        {
            // Comportamento SQLite nativo: in un UNIQUE(Code, ListId), due righe con lo
            // stesso Code e ListId=NULL NON sono considerate duplicate (NULL ≠ NULL nei
            // UNIQUE constraint SQLite). Questo test DOCUMENTA il comportamento perché
            // nell'UI reale ListId è sempre valorizzato (PriceItem.PriceListId è FK NOT
            // NULL → int non nullable in PriceItemRow), quindi lo scenario non si
            // presenta in produzione. Se un giorno avremo ItemId senza listino
            // (es. voci manuali nei preferiti) dovremo rivedere lo schema con
            // UNIQUE(Code, COALESCE(ListId, -1)) o simile.
            var id1 = _repo.AddFavorite(new UserFavorite { Code = "FREE", ListId = null });
            var id2 = _repo.AddFavorite(new UserFavorite { Code = "FREE", ListId = null });

            id1.Should().NotBe(id2, "SQLite UNIQUE tratta NULL come distinct");
            _repo.GetFavorites().Should().HaveCount(2);
        }

        [Fact]
        public void AddFavorite_DifferentCodes_ReturnDistinctIds()
        {
            var id1 = _repo.AddFavorite(new UserFavorite { Code = "A1", ListId = 1 });
            var id2 = _repo.AddFavorite(new UserFavorite { Code = "A2", ListId = 1 });
            var id3 = _repo.AddFavorite(new UserFavorite { Code = "A1", ListId = 2 });

            id1.Should().NotBe(id2);
            id1.Should().NotBe(id3);
            id2.Should().NotBe(id3);
            _repo.GetFavorites().Should().HaveCount(3);
        }
    }
}
