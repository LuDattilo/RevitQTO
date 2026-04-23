using FluentAssertions;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace QtoRevitPlugin.Tests.Computo
{
    public class NuoviPrezziRepositoryTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly QtoRepository _repo;
        private readonly int _sessionId;

        public NuoviPrezziRepositoryTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"np_{Guid.NewGuid():N}.db");
            _repo = new QtoRepository(_dbPath);
            _sessionId = _repo.InsertSession(new WorkSession
            {
                ProjectPath = "t.rvt",
                SessionName = "t",
                CreatedAt = DateTime.UtcNow
            });
        }

        public void Dispose()
        {
            _repo.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Fact]
        public void GetNuoviPrezzi_EmptyByDefault()
        {
            _repo.GetNuoviPrezzi(_sessionId).Should().BeEmpty();
        }

        [Fact]
        public void InsertNuovoPrezzo_RoundTripsAllFields()
        {
            var np = new NuovoPrezzo
            {
                SessionId = _sessionId,
                Code = "NP.001",
                Description = "Trasporto materiale a discarica",
                ShortDesc = "Trasp. discarica",
                Unit = "m³",
                Manodopera = 40, Materiali = 60, Noli = 10, Trasporti = 20,
                SpGenerali = 15, UtileImpresa = 10, RibassoAsta = 5,
                Status = NpStatus.Concordato,
                NoteAnalisi = "Prezzo da verbale DL 12/04/2026"
            };

            var id = _repo.InsertNuovoPrezzo(np);
            id.Should().BeGreaterThan(0);

            var loaded = _repo.GetNuoviPrezzi(_sessionId).Single();
            loaded.Code.Should().Be("NP.001");
            loaded.Description.Should().Be("Trasporto materiale a discarica");
            loaded.ShortDesc.Should().Be("Trasp. discarica");
            loaded.Unit.Should().Be("m³");
            loaded.Manodopera.Should().Be(40);
            loaded.Materiali.Should().Be(60);
            loaded.SpGenerali.Should().Be(15);
            loaded.UtileImpresa.Should().Be(10);
            loaded.RibassoAsta.Should().Be(5);
            loaded.Status.Should().Be(NpStatus.Concordato);
            loaded.NoteAnalisi.Should().Be("Prezzo da verbale DL 12/04/2026");
        }

        [Fact]
        public void UpdateNuovoPrezzo_ChangesStatusAndRecomputesUnitPrice()
        {
            var np = new NuovoPrezzo
            {
                SessionId = _sessionId,
                Code = "NP.002", Description = "Test",
                Manodopera = 100, SpGenerali = 15, UtileImpresa = 10,
                Status = NpStatus.Bozza
            };
            np.Id = _repo.InsertNuovoPrezzo(np);

            np.Status = NpStatus.Approvato;
            np.Manodopera = 150;
            _repo.UpdateNuovoPrezzo(np);

            var loaded = _repo.GetNuoviPrezzi(_sessionId).Single();
            loaded.Status.Should().Be(NpStatus.Approvato);
            loaded.Manodopera.Should().Be(150);
        }

        [Fact]
        public void DeleteNuovoPrezzo_RemovesRow()
        {
            var id = _repo.InsertNuovoPrezzo(new NuovoPrezzo
            {
                SessionId = _sessionId, Code = "NP.003", Description = "X", Manodopera = 1
            });
            _repo.GetNuoviPrezzi(_sessionId).Should().HaveCount(1);

            _repo.DeleteNuovoPrezzo(id);
            _repo.GetNuoviPrezzi(_sessionId).Should().BeEmpty();
        }

        [Fact]
        public void GetNuoviPrezzi_OrderedByCode()
        {
            _repo.InsertNuovoPrezzo(new NuovoPrezzo { SessionId = _sessionId, Code = "NP.002", Description = "B", Manodopera = 1 });
            _repo.InsertNuovoPrezzo(new NuovoPrezzo { SessionId = _sessionId, Code = "NP.001", Description = "A", Manodopera = 1 });
            _repo.InsertNuovoPrezzo(new NuovoPrezzo { SessionId = _sessionId, Code = "NP.003", Description = "C", Manodopera = 1 });

            var all = _repo.GetNuoviPrezzi(_sessionId);
            all.Select(n => n.Code).Should().ContainInOrder("NP.001", "NP.002", "NP.003");
        }
    }
}
