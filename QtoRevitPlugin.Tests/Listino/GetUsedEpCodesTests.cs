using FluentAssertions;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using System;
using System.IO;
using Xunit;

namespace QtoRevitPlugin.Tests.Listino
{
    /// <summary>
    /// Verifica il metodo GetUsedEpCodes usato dal panel Preferiti per marcare
    /// "Usato/Non usato" ogni FavoriteRowVm e abilitare il bulk "Rimuovi inutilizzati".
    /// </summary>
    public class GetUsedEpCodesTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly QtoRepository _repo;
        private readonly int _sessionId;

        public GetUsedEpCodesTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"used_ep_{Guid.NewGuid():N}.db");
            _repo = new QtoRepository(_dbPath);
            _sessionId = _repo.InsertSession(new WorkSession
            {
                ProjectPath = "test.rvt",
                SessionName = "Test",
                CreatedAt = DateTime.UtcNow
            });
        }

        public void Dispose()
        {
            _repo.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Fact]
        public void GetUsedEpCodes_NoAssignments_ReturnsEmpty()
        {
            _repo.GetUsedEpCodes(_sessionId).Should().BeEmpty();
        }

        [Fact]
        public void GetUsedEpCodes_WithActiveAssignments_ReturnsDistinctCodes()
        {
            _repo.InsertAssignment(NewAssignment("EP-001"));
            _repo.InsertAssignment(NewAssignment("EP-001")); // duplicato: stesso EpCode su altro elemento
            _repo.InsertAssignment(NewAssignment("EP-002"));

            var used = _repo.GetUsedEpCodes(_sessionId);

            used.Should().HaveCount(2);
            used.Should().Contain("EP-001").And.Contain("EP-002");
        }

        [Fact]
        public void GetUsedEpCodes_IgnoresNonActiveAssignments()
        {
            _repo.InsertAssignment(NewAssignment("ACTIVE"));
            var deleted = NewAssignment("DELETED");
            deleted.AuditStatus = AssignmentStatus.Deleted;
            _repo.InsertAssignment(deleted);

            var used = _repo.GetUsedEpCodes(_sessionId);
            used.Should().Contain("ACTIVE");
            used.Should().NotContain("DELETED");
        }

        [Fact]
        public void GetUsedEpCodes_IsCaseInsensitive()
        {
            _repo.InsertAssignment(NewAssignment("tos25_pr.p04.003.025"));
            var used = _repo.GetUsedEpCodes(_sessionId);

            // HashSet è stato creato con StringComparer.OrdinalIgnoreCase
            used.Contains("TOS25_PR.P04.003.025").Should().BeTrue();
            used.Contains("tos25_pr.p04.003.025").Should().BeTrue();
        }

        [Fact]
        public void GetUsedEpCodes_ScopesBySession()
        {
            var otherSessionId = _repo.InsertSession(new WorkSession
            {
                ProjectPath = "other.rvt",
                SessionName = "Other",
                CreatedAt = DateTime.UtcNow
            });
            _repo.InsertAssignment(NewAssignment("EP-THIS", _sessionId));
            _repo.InsertAssignment(NewAssignment("EP-OTHER", otherSessionId));

            _repo.GetUsedEpCodes(_sessionId).Should().Contain("EP-THIS").And.NotContain("EP-OTHER");
            _repo.GetUsedEpCodes(otherSessionId).Should().Contain("EP-OTHER").And.NotContain("EP-THIS");
        }

        private QtoAssignment NewAssignment(string epCode, int? sessionOverride = null) => new QtoAssignment
        {
            SessionId = sessionOverride ?? _sessionId,
            UniqueId = Guid.NewGuid().ToString(),
            ElementId = 1,
            Category = "Walls",
            FamilyName = "Muro",
            EpCode = epCode,
            Quantity = 1,
            Unit = "m²",
            UnitPrice = 10,
            CreatedBy = "test",
            CreatedAt = DateTime.UtcNow,
            AuditStatus = AssignmentStatus.Active,
            Version = 1
        };
    }
}
