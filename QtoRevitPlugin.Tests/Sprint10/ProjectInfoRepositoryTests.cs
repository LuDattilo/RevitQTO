using FluentAssertions;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using System;
using System.IO;
using Xunit;

namespace QtoRevitPlugin.Tests.Sprint10
{
    public class ProjectInfoRepositoryTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly QtoRepository _repo;
        private readonly int _sessionId;

        public ProjectInfoRepositoryTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"pinfo_test_{Guid.NewGuid()}.db");
            _repo = new QtoRepository(_dbPath);
            _sessionId = _repo.InsertSession(new WorkSession { ProjectPath = "p.rvt", ProjectName = "p" });
        }

        public void Dispose()
        {
            _repo.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Fact]
        public void Get_WhenAbsent_ReturnsNull()
        {
            _repo.GetProjectInfo(_sessionId).Should().BeNull();
        }

        [Fact]
        public void Upsert_Insert_ThenGet_RoundtripsAllFields()
        {
            var info = new ProjectInfo
            {
                SessionId = _sessionId,
                DenominazioneOpera = "Nuova sede comunale",
                Committente = "Comune di Firenze",
                Impresa = "ACME Costruzioni S.p.A.",
                RUP = "Ing. Mario Rossi",
                DirettoreLavori = "Arch. Laura Bianchi",
                Luogo = "Via Roma 10",
                Comune = "Firenze",
                Provincia = "FI",
                DataComputo = new DateTime(2026, 3, 15, 10, 0, 0, DateTimeKind.Utc),
                DataPrezzi = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                RiferimentoPrezzario = "Toscana 2024",
                CIG = "ABC1234567",
                CUP = "X12E34567890123",
                RibassoPercentuale = 12.5m,
                LogoPath = @"C:\logo.png"
            };

            _repo.UpsertProjectInfo(info);
            var reloaded = _repo.GetProjectInfo(_sessionId);

            reloaded.Should().NotBeNull();
            reloaded!.DenominazioneOpera.Should().Be("Nuova sede comunale");
            reloaded.Committente.Should().Be("Comune di Firenze");
            reloaded.Impresa.Should().Be("ACME Costruzioni S.p.A.");
            reloaded.RUP.Should().Be("Ing. Mario Rossi");
            reloaded.DirettoreLavori.Should().Be("Arch. Laura Bianchi");
            reloaded.Comune.Should().Be("Firenze");
            reloaded.Provincia.Should().Be("FI");
            reloaded.CIG.Should().Be("ABC1234567");
            reloaded.CUP.Should().Be("X12E34567890123");
            reloaded.RibassoPercentuale.Should().Be(12.5m);
            reloaded.RiferimentoPrezzario.Should().Be("Toscana 2024");
            reloaded.DataComputo.Should().NotBeNull();
            reloaded.DataPrezzi.Should().NotBeNull();
        }

        [Fact]
        public void Upsert_Update_ReplacesExistingRow()
        {
            _repo.UpsertProjectInfo(new ProjectInfo
            {
                SessionId = _sessionId,
                DenominazioneOpera = "Prima versione",
                CIG = "OLD"
            });

            _repo.UpsertProjectInfo(new ProjectInfo
            {
                SessionId = _sessionId,
                DenominazioneOpera = "Versione aggiornata",
                CIG = "NEW"
            });

            var reloaded = _repo.GetProjectInfo(_sessionId);
            reloaded!.DenominazioneOpera.Should().Be("Versione aggiornata");
            reloaded.CIG.Should().Be("NEW");
        }

        [Fact]
        public void Upsert_WithNullDates_StoresNullable()
        {
            var info = new ProjectInfo
            {
                SessionId = _sessionId,
                DenominazioneOpera = "Test",
                DataComputo = null,
                DataPrezzi = null
            };
            _repo.UpsertProjectInfo(info);

            var reloaded = _repo.GetProjectInfo(_sessionId);
            reloaded!.DataComputo.Should().BeNull();
            reloaded.DataPrezzi.Should().BeNull();
        }
    }
}
