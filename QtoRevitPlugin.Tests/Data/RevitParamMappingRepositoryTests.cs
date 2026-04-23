using FluentAssertions;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using System.IO;
using System.Linq;
using Xunit;

namespace QtoRevitPlugin.Tests.Data
{
    /// <summary>
    /// Test per il CRUD di <see cref="RevitParamMapping"/> (tabella v9 del .cme).
    /// Backend per la feature "selector inline" della scheda Informazioni Progetto
    /// — al momento non ancora collegato alla UI, ma il repository deve essere
    /// testato perché la UI finale dipenderà da queste invariant.
    /// </summary>
    public class RevitParamMappingRepositoryTests : System.IDisposable
    {
        private readonly string _dbPath;
        private readonly QtoRepository _repo;
        private readonly int _sessionId;

        public RevitParamMappingRepositoryTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"cme_parammap_{System.Guid.NewGuid():N}.db");
            _repo = new QtoRepository(_dbPath);
            _sessionId = _repo.InsertSession(new WorkSession
            {
                ProjectPath = "test.rvt",
                SessionName = "Test",
                CreatedAt = System.DateTime.UtcNow
            });
        }

        public void Dispose()
        {
            _repo.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Fact]
        public void GetRevitParamMappings_EmptyByDefault()
        {
            _repo.GetRevitParamMappings(_sessionId).Should().BeEmpty();
        }

        [Fact]
        public void Upsert_InsertsNewMapping()
        {
            _repo.UpsertRevitParamMapping(new RevitParamMapping
            {
                SessionId = _sessionId,
                FieldKey = ProjectInfoFieldKeys.Committente,
                ParamName = "ClientName",
                IsBuiltIn = true,
                SkipIfFilled = true
            });

            var all = _repo.GetRevitParamMappings(_sessionId);
            all.Should().HaveCount(1);

            var m = all.Single();
            m.FieldKey.Should().Be(ProjectInfoFieldKeys.Committente);
            m.ParamName.Should().Be("ClientName");
            m.IsBuiltIn.Should().BeTrue();
            m.SkipIfFilled.Should().BeTrue();
        }

        [Fact]
        public void Upsert_ReplacesExistingMappingForSameFieldKey()
        {
            // Prima: map Committente → ClientName (BuiltIn)
            _repo.UpsertRevitParamMapping(new RevitParamMapping
            {
                SessionId = _sessionId,
                FieldKey = ProjectInfoFieldKeys.Committente,
                ParamName = "ClientName",
                IsBuiltIn = true
            });
            _repo.GetRevitParamMappings(_sessionId).Should().HaveCount(1);

            // Dopo: cambia il source a shared param "CME_Cliente" (not builtin)
            _repo.UpsertRevitParamMapping(new RevitParamMapping
            {
                SessionId = _sessionId,
                FieldKey = ProjectInfoFieldKeys.Committente,
                ParamName = "CME_Cliente",
                IsBuiltIn = false
            });

            var all = _repo.GetRevitParamMappings(_sessionId);
            all.Should().HaveCount(1, "UNIQUE(SessionId, FieldKey) deve impedire duplicati");
            all.Single().ParamName.Should().Be("CME_Cliente");
            all.Single().IsBuiltIn.Should().BeFalse();
        }

        [Fact]
        public void Upsert_PreservesSkipIfFilledFlag()
        {
            _repo.UpsertRevitParamMapping(new RevitParamMapping
            {
                SessionId = _sessionId,
                FieldKey = ProjectInfoFieldKeys.Rup,
                ParamName = "CME_RUP",
                IsBuiltIn = false,
                SkipIfFilled = false // forza override
            });

            _repo.GetRevitParamMappings(_sessionId).Single().SkipIfFilled.Should().BeFalse();
        }

        [Fact]
        public void Delete_RemovesMapping()
        {
            _repo.UpsertRevitParamMapping(new RevitParamMapping
            {
                SessionId = _sessionId,
                FieldKey = ProjectInfoFieldKeys.Cig,
                ParamName = "CME_CIG"
            });
            _repo.GetRevitParamMappings(_sessionId).Should().HaveCount(1);

            _repo.DeleteRevitParamMapping(_sessionId, ProjectInfoFieldKeys.Cig);
            _repo.GetRevitParamMappings(_sessionId).Should().BeEmpty();
        }

        [Fact]
        public void Delete_UnknownFieldKey_NoOp()
        {
            // Non throw, semplicemente 0 righe rimosse
            _repo.DeleteRevitParamMapping(_sessionId, "NonEsiste");
            _repo.GetRevitParamMappings(_sessionId).Should().BeEmpty();
        }

        [Fact]
        public void Upsert_AllElevenFieldKeys_AllPersisted()
        {
            // Smoke test: tutti i FieldKey definiti in ProjectInfoFieldKeys.All
            // devono poter essere persistiti come mapping (nessuna collision, no errori).
            foreach (var key in ProjectInfoFieldKeys.All)
            {
                _repo.UpsertRevitParamMapping(new RevitParamMapping
                {
                    SessionId = _sessionId,
                    FieldKey = key,
                    ParamName = $"Param_{key}",
                    IsBuiltIn = false
                });
            }

            var all = _repo.GetRevitParamMappings(_sessionId);
            all.Should().HaveCount(ProjectInfoFieldKeys.All.Length);
            all.Select(m => m.FieldKey).Should().BeEquivalentTo(ProjectInfoFieldKeys.All);
        }

        [Fact]
        public void GetRevitParamMappings_OtherSession_Isolated()
        {
            // Le mapping sono per-sessione; un'altra sessione non deve vederle.
            var otherSessionId = _repo.InsertSession(new WorkSession
            {
                ProjectPath = "other.rvt",
                SessionName = "Other",
                CreatedAt = System.DateTime.UtcNow
            });

            _repo.UpsertRevitParamMapping(new RevitParamMapping
            {
                SessionId = _sessionId,
                FieldKey = ProjectInfoFieldKeys.Committente,
                ParamName = "ClientName",
                IsBuiltIn = true
            });

            _repo.GetRevitParamMappings(_sessionId).Should().HaveCount(1);
            _repo.GetRevitParamMappings(otherSessionId).Should().BeEmpty();
        }

        [Fact]
        public void Upsert_ParamNameNull_AllowedForExplicitNoMapping()
        {
            // FieldKey con ParamName=null significa "l'utente ha scelto (manuale)"
            // esplicitamente — persistito vs mai settato.
            _repo.UpsertRevitParamMapping(new RevitParamMapping
            {
                SessionId = _sessionId,
                FieldKey = ProjectInfoFieldKeys.RiferimentoPrezzario,
                ParamName = null,
                IsBuiltIn = false
            });

            var m = _repo.GetRevitParamMappings(_sessionId).Single();
            m.FieldKey.Should().Be(ProjectInfoFieldKeys.RiferimentoPrezzario);
            m.ParamName.Should().BeNull();
        }
    }
}
